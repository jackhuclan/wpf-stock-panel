using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 环形图：把一个总量按各分项的值切成一段段圆弧，每段取一种分片色。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的一件事：把 <see cref="ComputeSlices"/> 给出的起止角画成环上的扇形。
/// 分片色取本控件自己的 <see cref="SliceBrushes"/>（基座允许派生控件<b>追加</b>画笔，
/// 多彩分片正是"追加"的那一类；通用的六个画笔名一个都不占用）。
/// </para>
/// <para>
/// <b>它与折线 / 柱的根本差别：不"定框"，只"归一化"。</b>折线与柱要先由数据定出一个值轴窗口
/// （<see cref="ChartFrame"/>），环图不需要——各分项占比是相对总量算出来的纯比例，
/// 与量级无关，所以这里既不取刻度也不画网格线。同样的原因，它只用一块<b>正方</b>区域：
/// 圆要正圆，绘图区的长短边之差全给留白。
/// </para>
/// <para>
/// <b>角度口径与 <see cref="PolarGeometry"/> 一致</b>：弧度、<c>0</c> 是 12 点方向、顺时针为正。
/// 起始角从 12 点开始依次排下去，读完一整圈。
/// </para>
/// <para>
/// <b>非有限值与负值一律按"没有量"处理</b>（占 0，不占角度也不反向）：
/// 负的占比在环图上没有可表达的形式，反向画出来会与正值的弧叠在一起、把总量读错。
/// 见 <see cref="ComputeSlices"/>。
/// </para>
/// </remarks>
public sealed class DonutChart : ChartSurface
{
    /// <summary>一整圈。分片角以弧度计，总量对应的角度就是它。</summary>
    private const double FullTurn = 2d * Math.PI;

    /// <summary>环内圈半径占外圈半径的比例，默认 0.6（一个中等厚度的环）。</summary>
    private const double DefaultInnerRadiusRatio = 0.6d;

    /// <summary>圆弧分段的最大跨度：每一段都不超过四分之一圈。</summary>
    /// <remarks>
    /// 画弧用的是 <see cref="StreamGeometryContext.ArcTo"/>，它要靠"是否大于半圈"（large-arc）这个标志
    /// 在两条可能的弧里挑一条。把一个分片切成不超过四分之一圈的若干段之后，每段都是<b>小弧</b>，
    /// 那个标志恒为 <c>false</c>，于是"挑错弧"这件事在结构上不可能发生。
    /// 它同时解决了<b>整圆</b>这个坑：一整圈的首尾端点重合，单条 <c>ArcTo</c> 画出来是<b>空</b>的
    /// （起点终点相同，弧退化成一点），切成四段之后每段的端点都互不相同，一整圈才画得出来。
    /// </remarks>
    private const double MaxArcSpan = Math.PI / 2d;

    /// <summary>各分项的值（与 <see cref="SliceBrushes"/> 按下标对齐）。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>分片色，按下标与 <see cref="Values"/> 对齐。</summary>
    public static readonly StyledProperty<IReadOnlyList<IBrush>?> SliceBrushesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<IBrush>?>(nameof(SliceBrushes));

    /// <summary>内圈半径占外圈半径的比例，默认 0.6。</summary>
    public static readonly StyledProperty<double> InnerRadiusRatioProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(InnerRadiusRatio), DefaultInnerRadiusRatio);

    static DonutChart()
    {
        // 数据、分片色、环宽都会改变画面，三个都要登记：少了它，换一套分片色也不重绘
        // （AffectsRender 的注册状态没有公开查询接口，漏登记在运行期没有任何信号）。
        AffectsRender<DonutChart>(ValuesProperty, SliceBrushesProperty, InnerRadiusRatioProperty);
    }

    /// <summary>各分项的值，<b>每个元素是一片</b>。为 <c>null</c> 或空表示没有数据。</summary>
    /// <remarks>
    /// 序列里允许出现非有限值（<c>NaN</c> / <c>±Infinity</c>）与负值：它们都表示"这一项没有量"，
    /// 占 0 角度、不画——但<b>仍然占自己的下标</b>，于是 <see cref="SliceBrushes"/> 的颜色
    /// 不会因为别的一项变成 0 而整体错位（那会让同一个分项换一次数据就换个颜色）。
    /// </remarks>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>分片色，按下标与 <see cref="Values"/> 对齐；比数据短时按取模循环使用。</summary>
    /// <remarks>
    /// <c>null</c> 或空列表的合法含义是"这一层不画"（与基座对其余画笔的口径一致），
    /// 整块环图因此不画任何分片，而不是退化成某种默认颜色——配色只在样式库里出现。
    /// </remarks>
    public IReadOnlyList<IBrush>? SliceBrushes
    {
        get => GetValue(SliceBrushesProperty);
        set => SetValue(SliceBrushesProperty, value);
    }

    /// <summary>
    /// 内圈半径占外圈半径的比例，默认 0.6。取值会由渲染时夹到 <c>[0, 1]</c>，
    /// 非有限值按 0 处理（见 <see cref="RenderChart"/>）。
    /// </summary>
    public double InnerRadiusRatio
    {
        get => GetValue(InnerRadiusRatioProperty);
        set => SetValue(InnerRadiusRatioProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的分片：序列里**至少要有一个有限值**（Task 7–10 的统一口径，见计划全局约束）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>判据不能只看"长度非零"</b>，理由与 <see cref="LineChart.HasData"/> 完全相同：
    /// 整条序列全是非有限值时没有任何可画的东西，空序列也不该让基座去画一块"有框无内容"的面板。
    /// </para>
    /// <para>
    /// <b>这里有一处与折线图不同的后果，明说</b>：环图只给<b>正</b>值分角度，所以"全是 0"或
    /// "全是负数"的序列虽然 <see cref="HasData"/> 为 <c>true</c>（有有限值），却一片也画不出来
    /// （<see cref="ComputeSlices"/> 的 <c>total</c> 不大于 0，返回空表），画面是一片空白面板。
    /// 这是刻意的：面板本身就表示"这里有一个环图，只是它没有可画的分片"，
    /// 比整块控件凭空消失要诚实。口径统一成"有没有有限值"而不是"这片能不能画出角度"，
    /// 是为了别让四个图表各自发明一套判断。
    /// </para>
    /// </remarks>
    protected override bool HasData =>
        Values is { Count: > 0 } values && values.Any(double.IsFinite);

    /// <summary>
    /// 把各分项的值换算成一段段圆弧的起止角：<b>总量 → 一整圈</b>，<b>分项 → 按占比切出来的弧</b>。
    /// </summary>
    /// <param name="values">各分项的值。<c>null</c> 与空序列返回空表。</param>
    /// <returns>
    /// 与 <paramref name="values"/> <b>长度相同、下标一一对应</b>的起止角表（弧度）：
    /// <c>0</c> 是 12 点方向，顺时针为正，各段首尾相接从 <c>0</c> 排下去，
    /// 总跨度为一整圈（<c>2π</c>，在浮点误差以内）。
    /// <b>非有限值与非正值的那一段</b>跨度恒为 <c>0</c>（它仍占自己的下标，见备注）。
    /// 总量不大于 0（全 0、全负、空）或总量溢出成非有限值时返回<b>空表</b>。任何输入都不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>负值按 0 计，不是反向画。</b>环图表达的是"各部分占整体的多少"，负的占比没有可表达的形式；
    /// 若按它反向画一段弧，那段弧会与正值的弧<b>叠在一起</b>，读图的人会把总占比读错——
    /// 这比不画它糟得多。非有限值同理（<c>NaN</c> 占比会让整块环图的每一段都变成 <c>NaN</c>）。
    /// <b>本文件的口径是"非正 = 没有量"</b>，它在控件文档里写明了。
    /// </para>
    /// <para>
    /// <b>为什么非正值还要占一个下标。</b>分片色是按<b>下标</b>与数据对齐的
    /// （<see cref="SliceBrushes"/>）。若把非正值从结果里剔掉，后面每一项的颜色都会往前挪一格——
    /// 同一份数据里某个分项从正变 0，其余分片的颜色会集体换一次，而数据本身只动了一个数。
    /// 长度与下标对齐，是让"颜色跟着分项走"成为结构上的必然。
    /// </para>
    /// <para>
    /// <b>总量为 0 时返回空表</b>，而不是返回一串跨度 0 的分片：那种输入下每个占比都是 <c>0/0</c>，
    /// 是要防的除零，而"没有什么可画"与"有一段弧，只是它没有长度"在读图上是一回事，
    /// 在调用方那里却不是——空表让渲染方直接收手，不必再判一遍。
    /// </para>
    /// <para>
    /// <b>总和是 <c>2π</c>，在浮点误差以内</b>：各段跨度按 <c>值 / 总量 × 2π</c> 逐个算出后累加，
    /// 误差量级是每段一个 ULP，肉眼与像素都到不了。画出来的环若因此留下一条亚像素的缝，
    /// 那也远在半个像素以下。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(double StartAngle, double SweepAngle)> ComputeSlices(IReadOnlyList<double> values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        double total = 0d;

        foreach (double value in values)
        {
            if (IsShare(value))
            {
                total += value;
            }
        }

        // 总量不大于 0（全 0、全负、全非有限）时每一段占比都是 0/0；总量溢出成非有限值
        // （量级逼近 double.MaxValue 的几项相加）时每一段占比都是 0——两种都没有可画的东西。
        if (!double.IsFinite(total) || total <= 0d)
        {
            return [];
        }

        var slices = new List<(double StartAngle, double SweepAngle)>(values.Count);
        double start = 0d;

        foreach (double value in values)
        {
            double sweep = IsShare(value) ? value / total * FullTurn : 0d;
            slices.Add((start, sweep));
            start += sweep;
        }

        return slices;
    }

    /// <summary>
    /// 画这块环：把每一段起止角画成环上的扇形，颜色按分片色的下标取。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>圆心取绘图区正中、外圈半径取长短边的一半</b>：环必须是正圆，所以短边才是限制——
    /// 长边多出来的部分全留白，环居中。若图本身不是正方形，环不会跟着拉成椭圆
    /// （拉成椭圆的环读不出占比，角度变了但视觉上每一段的长短不再是线性的）。
    /// </para>
    /// <para>
    /// <b>内圈半径比例非有限按 0 处理、其余夹到 <c>[0, 1]</c></b>：<c>NaN</c> 会让每个端点一起变成
    /// <c>NaN</c>（整块图什么都画不出来，且不抛异常），负比例会把内圈翻到外面去（几何自交），
    /// 大于 1 的比例让环宽变成负数。夹紧之后 <c>0</c> 是实心饼，<c>1</c> 是宽度为零的环
    /// （什么都看不见，合法但不建议）。这与基座给 <c>PlotPadding</c> 的处置同一口径。
    /// </para>
    /// <para>
    /// <b>不画分片色的那两种情形</b>：<see cref="SliceBrushes"/> 为 <c>null</c> 或空表时整层不画
    /// （"<c>null</c> 画笔 = 这一层不画"是基座定下的口径，配色只在样式库里出现）；
    /// <see cref="ComputeSlices"/> 返回空表时也直接收手。
    /// </para>
    /// <para>
    /// <b>跨度不大于 0 的分片跳过</b>：值为 0 或负的分项本来就占 0 角度，画出来是一条零面积的
    /// 退化扇形（某些实现会渲染成一条线或一个点）。它们在下标上照旧占位，只是不画。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<double>? values = Values;
        IReadOnlyList<IBrush>? brushes = SliceBrushes;

        // 三条收手理由，性质不同：
        //  - values 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是 ComputeSlices 的必要条件——它的入参不可空；
        //  - 分片色为空是"这一层不画"（基座对画笔的既有口径）；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定。
        if (values is null || brushes is null || brushes.Count == 0
            || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        IReadOnlyList<(double StartAngle, double SweepAngle)> slices = ComputeSlices(values);

        if (slices.Count == 0)
        {
            return;
        }

        double centreX = plotArea.X + (plotArea.Width / 2d);
        double centreY = plotArea.Y + (plotArea.Height / 2d);
        double outerRadius = Math.Min(plotArea.Width, plotArea.Height) / 2d;

        double ratio = InnerRadiusRatio;
        if (!double.IsFinite(ratio))
        {
            ratio = 0d;
        }

        double innerRadius = outerRadius * Math.Clamp(ratio, 0d, 1d);

        for (int i = 0; i < slices.Count; i++)
        {
            (double start, double sweep) = slices[i];

            if (!(sweep > 0d))
            {
                // 0 与负值都不画（负值不该出现——ComputeSlices 已经把它们压成 0，
                // 这里挡的是"以后有人改了那边"）。NaN 也走这条：比较为 false。
                continue;
            }

            // 分片色按下标取，比数据短时取模循环：保证同一个分项的颜色只由它的下标决定，
            // 不随别项的取值变化。
            context.DrawGeometry(
                brushes[i % brushes.Count],
                null,
                BuildSlice(start, sweep, centreX, centreY, outerRadius, innerRadius));
        }
    }

    /// <summary>
    /// 一个分项的扇形轮廓：外圈走一段弧，径向连到内圈，内圈再走回来。
    /// </summary>
    /// <param name="startAngle">起始角（弧度，12 点方向为 0，顺时针为正）。</param>
    /// <param name="sweepAngle">跨度，必须为正。</param>
    /// <param name="centreX">圆心横坐标。</param>
    /// <param name="centreY">圆心纵坐标。</param>
    /// <param name="outerRadius">外圈半径。</param>
    /// <param name="innerRadius">内圈半径，<c>0</c> 时画成实心饼。</param>
    /// <returns>闭合的扇形几何，只填充不描边。</returns>
    /// <remarks>
    /// <para>
    /// <b>弧一律拆成不超过四分之一圈的小段</b>：理由见 <see cref="MaxArcSpan"/>
    /// （小弧用不到 large-arc 标志，"挑错弧"不可能发生；一整圈的首尾端点重合这个坑也一并避开）。
    /// 拆分点用 <see cref="PolarGeometry.OnCircle"/> 直接算出来，各段首尾严格相接，
    /// 不靠"弧长按比例近似"。
    /// </para>
    /// <para>
    /// <b>内外圈各走一遍</b>：外圈顺时针过去、内圈逆时针回来，两条径向边把它们连成一个
    /// 不自交的环带扇形——自交的话填充规则会把某些区域挖空或涂重，画出来是缺一块的环。
    /// 内圈半径为 <c>0</c> 时退化成实心饼，两条径向边收拢到圆心即可，不必再走一遍内圈弧。
    /// </para>
    /// <para>
    /// <b>不做像素对齐</b>：弧上的点是角度算出来的，挪半个像素就是对占比撒谎
    /// （与数据点不对齐同一个理由）。环图没有轴线或网格线，本控件因此一处都不用
    /// <see cref="PixelAlign"/>。
    /// </para>
    /// </remarks>
    private static StreamGeometry BuildSlice(
        double startAngle,
        double sweepAngle,
        double centreX,
        double centreY,
        double outerRadius,
        double innerRadius)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            (double startX, double startY) = PolarGeometry.OnCircle(startAngle, centreX, centreY, outerRadius);
            path.BeginFigure(new Point(startX, startY), isFilled: true);

            AppendArc(path, startAngle, sweepAngle, centreX, centreY, outerRadius, SweepDirection.Clockwise);

            if (innerRadius > 0d)
            {
                (double innerEndX, double innerEndY) =
                    PolarGeometry.OnCircle(startAngle + sweepAngle, centreX, centreY, innerRadius);
                path.LineTo(new Point(innerEndX, innerEndY));

                AppendArc(path, startAngle + sweepAngle, -sweepAngle, centreX, centreY, innerRadius, SweepDirection.CounterClockwise);
            }
            else
            {
                path.LineTo(new Point(centreX, centreY));
            }

            path.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// 沿一条圆弧追加若干段 <see cref="StreamGeometryContext.ArcTo"/>，每段不超过四分之一圈。
    /// </summary>
    /// <param name="path">正在构建的路径。</param>
    /// <param name="fromAngle">弧的起始角（弧度）。</param>
    /// <param name="sweepAngle">跨度，可以为负（内圈就是倒着走回去）。</param>
    /// <param name="centreX">圆心横坐标。</param>
    /// <param name="centreY">圆心纵坐标。</param>
    /// <param name="radius">半径。</param>
    /// <param name="direction">走向。与 <paramref name="sweepAngle"/> 的符号一致，两者必须是同一个意思。</param>
    /// <remarks>
    /// 段数取 <c>⌈|跨度| / (π/2)⌉</c>，至少一段。各段的端点由角度直接算出，于是
    /// <b>一段弧画多长只取决于角度，与段数无关</b>——段数只影响"用多少条弧逼近它"。
    /// </remarks>
    private static void AppendArc(
        StreamGeometryContext path,
        double fromAngle,
        double sweepAngle,
        double centreX,
        double centreY,
        double radius,
        SweepDirection direction)
    {
        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / MaxArcSpan));
        Size size = new(radius, radius);

        for (int i = 1; i <= segments; i++)
        {
            (double x, double y) = PolarGeometry.OnCircle(
                fromAngle + (sweepAngle * i / segments),
                centreX,
                centreY,
                radius);

            // isLargeArc 恒为 false 是拆分的**目的**：每段都不超过四分之一圈，小弧是唯一解，
            // 于是这个标志不必参与判断，也不存在"挑错弧"的可能。
            path.ArcTo(new Point(x, y), size, 0d, isLargeArc: false, direction);
        }
    }

    /// <summary>
    /// 这一项算不算"有量"：有限且大于 0。非正与非有限一律按 0 处理（见 <see cref="ComputeSlices"/>）。
    /// </summary>
    private static bool IsShare(double value) => double.IsFinite(value) && value > 0d;
}
