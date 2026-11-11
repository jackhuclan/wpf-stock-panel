using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 柱状图：把一条数据序列画成一排竖直的柱，柱长从<b>零线</b>量起、正负分向两侧。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的四件事：网格线、零线、柱、以及"哪根柱没有值不画"。
/// 柱色取基座的 <see cref="ChartSurface.PrimaryBrush"/>（"一切数据序列的颜色"都取它与
/// <see cref="ChartSurface.SecondaryBrush"/> 之一，<b>不另起画笔名</b>）。
/// </para>
/// <para>
/// <b>零线锚定：值轴窗口里一定含 0（<see cref="ZeroAnchored"/>）。</b>
/// 这是本控件与折线图在数据口径上唯一的差别，也是柱状图读数的前提——柱表达的是<b>量</b>，
/// 而量只有在相对一条<b>看得见的零线</b>比较时才有意义。直接把数据的值域交给
/// <see cref="ChartFrame.ComputeWindow(IReadOnlyList{double}, int)"/> 会毁掉这件事：
/// 全正的数据得到 <c>[0.5, 3.2]</c> 这样的窗口，零线被顶到框外，最小值那根柱<b>连一个像素都画不出来</b>
/// （它的 y 正好落在被夹到框边的零线上）；全负的数据同理，最接近 0 那根消失。
/// 把 0 并进值域之后，窗口的两端之一就是零线本身，最短的柱也量得出长度。
/// </para>
/// <para>
/// <b>柱怎么排、怎么对齐，规则都在 <see cref="ColumnLayout"/> 里</b>（K 线的实体与成交量柱用的是同一套）。
/// 这里只强调那条最容易混的分界：<b>柱的边缘走"填充对齐"（落在整像素上），零线与网格线走"笔画对齐"
/// （中线补半格）</b>——<see cref="PixelAlign.SnapStroke"/> 是后者的工具，用在填充边上会把整根柱
/// 平移半个像素、并把那条边涂成 50% 的灰。两种口径各自的由来见 <see cref="ColumnLayout.SnapSpan"/>
/// 与 <see cref="PixelAlign"/>。
/// </para>
/// <para>
/// <b>非有限值 = "这一根没有值"</b>（指标预热期合法地产生 <c>NaN</c>），不进几何——
/// 判据与折线图一致：<c>!double.IsFinite(…)</c>，不是"是否越出值域"。见 <see cref="HasData"/>
/// 与 <see cref="LayoutBar"/>。
/// </para>
/// <para>
/// <b>能测的部分全被推进了纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，<c>RenderChart</c> 里画的每一笔在自动化里都断言不了——
/// 于是"窗口怎么定"（<see cref="ZeroAnchored"/> + <see cref="ChartFrame.ComputeWindow(ValueRange, int)"/>）
/// 与"每根柱落在哪、零线在哪"（<see cref="LayoutBar"/>）都是纯函数，由 <c>BarLayoutTests</c> 直接调用。
/// <c>RenderChart</c> 只剩「取哪个值、按画笔是否为 <c>null</c> 决定画不画、按什么顺序画」。
/// </para>
/// </remarks>
public sealed class BarChart : ChartSurface
{
    /// <summary>
    /// 网格线的目标条数。取 4：<see cref="AxisTicks.Nice"/> 只保证"大致想要几条"（实际条数会在目标值附近浮动，
    /// 向外取整还会两端各多一格），4 落在 3–6 条这个区间里——在几十像素高的绘图区里读得清，也不至于挤成一团。
    /// 与 <see cref="LineChart"/> 取同一个值，是为了让同一页里并列的两个图表网格疏密一致。
    /// </summary>
    private const int GridLineTargetCount = 4;

    /// <summary>柱与柱之间让出的槽宽比例，默认 0.2（槽宽的 20% 是空白）。</summary>
    private const double DefaultGapRatio = 0.2d;

    /// <summary>柱的数据（按时间顺序）与零线位置的依据。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>每根柱在自己的槽里让出的空白比例，默认 0.2。</summary>
    public static readonly StyledProperty<double> GapRatioProperty =
        AvaloniaProperty.Register<BarChart, double>(nameof(GapRatio), DefaultGapRatio);

    static BarChart()
    {
        // 数据与间距都会改变画面，两个都要登记：少了它，赋了新数据也不重绘
        // （AffectsRender 的注册状态没有公开查询接口，漏登记在运行期没有任何信号）。
        AffectsRender<BarChart>(ValuesProperty, GapRatioProperty);
    }

    /// <summary>柱的数据，按时间顺序排列；<b>每个元素是一根柱</b>。为 <c>null</c> 或空表示没有数据。</summary>
    /// <remarks>
    /// 序列里允许出现非有限值（<c>NaN</c> / <c>±Infinity</c>）：那表示"这一根没有值"，
    /// 它<b>不会</b>被画出来——见 <see cref="LayoutBar"/>。下标与柱位的对应关系不变
    /// （被跳过的柱照样占自己的槽），否则一根预热期的 <c>NaN</c> 会把后面所有柱整体挪位。
    /// </remarks>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// 每根柱在自己的槽里让出的空白比例，默认 0.2。取值会由 <see cref="LayoutBar"/> 夹到 <c>[0, 1]</c>，
    /// 非有限值按 0 处理（见该方法的备注）。
    /// </summary>
    public double GapRatio
    {
        get => GetValue(GapRatioProperty);
        set => SetValue(GapRatioProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的柱：序列里**至少要有一个有限值**（与 <see cref="LineChart.HasData"/> 同一口径）。
    /// </summary>
    /// <remarks>
    /// 空序列时基座会整块收手（底色、边框、零线、网格线一概不画）——没有数据却画出一套刻度，
    /// 比什么都不画更容易被误读成"数据全是 0"（<see cref="ChartSurface.HasData"/> 的文档原话）。
    /// <b>判据不能只看"长度非零"</b>：整条序列全是非有限值时，<see cref="ValueRange.From"/>
    /// 会拿它兜底的 <c>(0, 1)</c> 窗口去撑起一套网格线，而那套刻度与序列毫无关系。
    /// 一个有限值就足够画出一根有意义的轴（其余柱跳过即可）。代价是每帧一次 O(n) 扫描，
    /// 相对于每帧重建刻度列表的开销可以忽略。
    /// </remarks>
    protected override bool HasData =>
        Values is { Count: > 0 } values && values.Any(double.IsFinite);

    /// <summary>
    /// 把"含 0"这件事并进数据值域，供值轴窗口使用。
    /// </summary>
    /// <param name="dataRange">数据的值域，通常来自 <see cref="ValueRange.From"/>。</param>
    /// <returns>把 <c>0</c> 并进去之后的值域：两端各自向 0 撑开，已经含 0 时原样返回。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么柱状图必须这么做。</b>柱长是从零线量起的量，零线跑到框外，柱长就没有可比的对象——
    /// 而且不是"看不清"，是<b>直接消失</b>：全正数据 <c>[0.5, 3.2]</c> 的窗口就是 <c>[0.5, 3.2]</c>，
    /// 零线被 <see cref="LayoutBar"/> 夹到框底，值 <c>0.5</c> 那根柱的 y 正好也在框底，柱高 0 像素。
    /// 全负数据对称地丢掉最接近 0 的那根。<b>折线图不需要这一条</b>：折线比的是相邻点之间的走势，
    /// 零线在哪不影响读数，所以那条链上不并 0。
    /// </para>
    /// <para>
    /// <b>它只撑开值域，不动数据</b>：柱的位置、数量、顺序全按原序列来，少一个点也不会挪位。
    /// 非有限值不参与（<see cref="ValueRange.From"/> 已经滤掉），全非有限输入拿到的是它的兜底窗口
    /// <c>(0, 1)</c>——本就含 0，本方法原样返回。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，在没有 UI 线程的测试里可直接调用。
    /// </para>
    /// </remarks>
    public static ValueRange ZeroAnchored(ValueRange dataRange) =>
        new(Math.Min(0d, dataRange.Min), Math.Max(0d, dataRange.Max));

    /// <summary>
    /// 算出一根柱的矩形与零线的行号。
    /// </summary>
    /// <param name="index">柱的下标（<c>0</c> 是序列里的第一根）。</param>
    /// <param name="count">柱的根数。非正时返回一根零尺寸的柱，不抛异常。</param>
    /// <param name="value">这根柱的数据值。可以是非有限值，含义是"这一根没有值"。</param>
    /// <param name="range">纵轴的值域，<b>必须是 <see cref="ZeroAnchored"/> 处理过、含 0 的那一个</b>。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。</param>
    /// <param name="gapRatio">柱在槽里让出的空白比例。</param>
    /// <returns>柱的矩形与零线的行号；非有限的 <paramref name="value"/> 给出非有限的纵向几何（调用方据此跳过）。</returns>
    /// <remarks>
    /// <b>本方法是 <see cref="ColumnLayout.Anchored"/> 的委托，规则全在那里</b>
    /// （横向两条边各自取整到最近、纵向从零线向外取整、间距夹到 <c>[0, 1]</c>、
    /// 非有限值原样透传、<c>count</c> 非正时不给毒值）。留在本控件上的是<b>名字</b>：
    /// 它是本控件的公开契约（与 Task 7 的计划一致），而实现与 K 线共用一份——
    /// 让 K 线去调 <c>BarChart.LayoutBar</c> 才是要避开的那种"控件引用控件"。
    /// </remarks>
    public static (Rect Bar, double Baseline) LayoutBar(
        int index,
        int count,
        double value,
        ValueRange range,
        Rect plotArea,
        double gapRatio) =>
        ColumnLayout.Anchored(index, count, value, range, plotArea, gapRatio);

    /// <summary>
    /// 画框里的东西：网格线 → 零线 → 柱。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>窗口先并入 0</b>（<see cref="ZeroAnchored"/>），网格线的刻度与柱的映射共用这同一个窗口
    /// ——两者必须同源，否则刻度说的值与柱落的位置是两回事，且没有任何报错。理由见 <see cref="ZeroAnchored"/>。
    /// </para>
    /// <para>
    /// <b>绘制顺序是"网格 → 零线 → 柱"。</b>零线在柱之前画，于是负值柱会盖住它穿过的格子——
    /// 但因为与 0 刻度那条网格线<b>同色同宽、且行号相同</b>（<c>0</c> 落在窗口内时
    /// <see cref="AxisTicks.Nice"/> 一定产出 <c>0</c> 这条刻度），重叠画两次不产生任何视觉差异。
    /// 仍然显式画一次，是为了兜底窗口（量级极端到刻度退回固定窗口）下零线不跟着消失。
    /// </para>
    /// <para>
    /// <b>零线与网格线走的是笔画对齐</b>（<see cref="PixelAlign.SnapStroke"/>：行号先由
    /// <see cref="ChartFrame.GridLineYs"/> 取整、这里再补半格），<b>柱边走的是填充对齐</b>
    /// （<see cref="LayoutBar"/> 里的取整）。两套规则各自的由来见各自的文档，不要互相套用。
    /// </para>
    /// <para>
    /// <b>跳过没有值的柱</b>：<see cref="LayoutBar"/> 对非有限值返回带非有限 <c>Y</c> / <c>Height</c>
    /// 的矩形，这里据此跳过——判据是非有限判定，不是"是否越出绘图区"
    /// （<c>NaN</c> 与任何数比较都是 <c>false</c>，区间比较拦不住它）。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<double>? values = Values;

        // 两条收手理由，性质不同：
        //  - values 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是下面按下标取值的必要条件；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定。
        if (values is null || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow(
            ZeroAnchored(ValueRange.From(values)),
            GridLineTargetCount);

        DrawHorizontalLines(context, ChartFrame.GridLineYs(ticks, window, plotArea), plotArea);

        int count = values.Count;
        double baseline = ColumnLayout.BaselineY(window, plotArea);
        double gapRatio = GapRatio;

        if (AxisBrush is { } axisBrush)
        {
            double row = PixelAlign.SnapStroke(baseline, ChartFrame.GridStrokeThickness);
            context.DrawLine(
                new Pen(axisBrush, ChartFrame.GridStrokeThickness),
                new Point(plotArea.X, row),
                new Point(plotArea.Right, row));
        }

        if (PrimaryBrush is not { } barBrush)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Rect bar = LayoutBar(i, count, values[i], window, plotArea, gapRatio).Bar;

            if (!double.IsFinite(bar.Y) || !double.IsFinite(bar.Height))
            {
                continue;
            }

            context.FillRectangle(barBrush, bar);
        }
    }

    /// <summary>
    /// 在每个行号处画一条横贯绘图区的线（<b>网格线</b>；零线走 <see cref="RenderChart"/> 里那一次调用）。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="rows">行号（屏幕 y），由 <see cref="ChartFrame.GridLineYs"/> 给出——已对齐、已筛过非有限值。</param>
    /// <param name="plotArea">绘图矩形。</param>
    /// <remarks>
    /// 笔的宽度取 <see cref="ChartFrame.GridStrokeThickness"/>，与行号的对齐<b>共用同一个值</b>：
    /// 那半格是按笔画奇偶推的，两者用了不同的宽度，推出来的半格就与笔画的真实覆盖对不上。
    /// 画笔为 <c>null</c> 时整层不画（<c>null</c> 的含义是"这一层不画"，不是黑色）。
    /// </remarks>
    private void DrawHorizontalLines(DrawingContext context, IReadOnlyList<double> rows, Rect plotArea)
    {
        if (AxisBrush is not { } axisBrush)
        {
            return;
        }

        Pen pen = new(axisBrush, ChartFrame.GridStrokeThickness);

        foreach (double row in rows)
        {
            context.DrawLine(pen, new Point(plotArea.X, row), new Point(plotArea.Right, row));
        }
    }
}
