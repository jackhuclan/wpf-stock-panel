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
/// <b>像素对齐在这里有两套规则，别互相套用。</b>柱是<b>填充</b>的，填充要锐利就得让四条边落在
/// 设备像素边界（100% 缩放下即整数）上——因此 <see cref="LayoutBar"/> 对横向两条边取
/// <see cref="Math.Round(double)"/>、对纵向那条<b>远离零线</b>的边取向外取整
/// （<see cref="Math.Floor(double)"/> / <see cref="Math.Ceiling(double)"/>）。
/// 而 <see cref="PixelAlign.SnapStroke"/> 是<b>笔画中线</b>的半像素偏移，只用于画线
/// （零线、网格线）：把它用在填充边上会把整根柱平移半个像素、并把那条边涂成 50% 的灰。
/// 两者的差别与各自的由来见 <see cref="LayoutBar"/> 与 <see cref="RenderChart"/>。
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
    /// 算出一根柱的矩形与零线的行号：<b>下标 → 横向槽位</b>、<b>值 → 纵向端点</b>，并做像素对齐。
    /// </summary>
    /// <param name="index">柱的下标（<c>0</c> 是序列里的第一根）。必须落在 <c>[0, count - 1]</c> 内。</param>
    /// <param name="count">柱的根数。非正时返回一根零尺寸的柱，不抛异常（见备注）。</param>
    /// <param name="value">这根柱的数据值。可以是非有限值，含义是"这一根没有值"。</param>
    /// <param name="range">纵轴的值域，<b>必须是 <see cref="ZeroAnchored"/> 处理过、含 0 的那一个</b>
    /// （通常是 <see cref="ChartFrame.ComputeWindow(ValueRange, int)"/> 给出的窗口）。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。宽度为 0 时所有柱退化成零宽，本函数照算不误。</param>
    /// <param name="gapRatio">柱在槽里让出的空白比例；非有限值按 0 处理，其余夹到 <c>[0, 1]</c>。</param>
    /// <returns>
    /// 柱的矩形（横向已对齐到整像素、纵向已从零线向外取整）与零线的行号（已对齐到整像素）。
    /// <b>两根柱的矩形永不重叠</b>，横向也恒在 <paramref name="plotArea"/> 之内（纵向见备注）。
    /// <b><paramref name="value"/> 非有限时返回的矩形 <c>Y</c> 与 <c>Height</c> 都是非有限值</b>
    /// （与 <see cref="ChartHitTest.YFor"/> 原样透传非有限的纪律一致）——调用方据此跳过，
    /// 判据是 <c>!double.IsFinite(bar.Y)</c>。<b>任何输入都不抛异常。</b>
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>零线的位置。</b>取 <see cref="ChartHitTest.YFor"/> 给 <c>0</c> 的行号，再夹进
    /// <paramref name="plotArea"/> 的上下边、取整。夹紧是给<b>兜底窗口</b>留的：正常路径上
    /// <paramref name="range"/> 含 0（<see cref="ZeroAnchored"/> 保证），零线本就在框里；
    /// 量级极端到 <see cref="AxisTicks.Nice"/> 退回固定窗口 <c>(0, 1)</c> 时它仍可能落在框外，
    /// 那时夹到<b>靠近 0 的那条边</b>，柱照样从那条边量起，而不是整块图什么都画不出来。
    /// </para>
    /// <para>
    /// <b>横向：两条边各自独立地取整到最近的整数像素</b>（不是"先取整左边界再加宽度"）。
    /// 这样相邻两根柱相邻的那条边得到的是<b>同一个</b> <see cref="Math.Round(double)"/> 结果，
    /// 于是"不重叠"是结构上的必然，而不是靠留白算得巧；横向不越框也来自同一件事
    /// （槽的左右端点本来就落在框边上，取整不会把它推出去）。
    /// 代价写在下面：槽宽不足 1 像素时，某些柱会退化成零宽而<b>看不见</b>——
    /// 宁可少画一根，也不画一根压住邻居的柱。
    /// </para>
    /// <para>
    /// <b>纵向：远离零线的那条边<b>向外</b>取整</b>（值大于 0 时向上取 <see cref="Math.Floor(double)"/>，
    /// 小于 0 时向下取 <see cref="Math.Ceiling(double)"/>）。向外而不是就近，一举两得：
    /// 柱高恒为整数像素（填充锐利），且<b>任何非零的柱至少占 1 像素</b>——
    /// 一根量级只有半个像素的柱若按就近取整就会塌成零高、凭空消失，而"柱短"与"没有这根柱"
    /// 在读图上是两件完全不同的事。<b>值为 0 时高恒为 0</b>（零线就在这里，柱长为零是实话），
    /// 这条不受上面那条最小高度规则影响。
    /// </para>
    /// <para>
    /// <b>纵向可能越出绘图区不到 1 像素</b>：<paramref name="plotArea"/> 的上下边不是整数时，
    /// 向外取整会把它顶出去最多一个像素。这是"填充锐利"与"严格不越框"之间的取舍，
    /// 取前者——越出去的那点落在基座的内边距里，看不见，而糊掉的柱边是看得见的。
    /// 横向没有这个问题（那里用的是取整到最近）。
    /// </para>
    /// <para>
    /// <b>退化值域（全等值、单点）</b>时 <see cref="ChartHitTest.YFor"/> 把一切值映到绘图区垂直正中，
    /// 零线也在那里，于是每根柱都是零高——零跨度下所有柱一样长这件事本就无从表达。
    /// 正常路径到不了这里：窗口由刻度定出，而 <see cref="AxisTicks.Nice"/> 保证窗口非退化。
    /// </para>
    /// <para>
    /// <b><paramref name="count"/> 非正时</b>返回一根零尺寸的柱（位置取绘图区左边、零线上），
    /// 不抛异常也不做除法——<c>绘图区宽度 / 0</c> 会得到 <c>Infinity</c>，再往下就是一根无限宽的柱。
    /// 调用方本来就该先判空（"有没有数据"是 <see cref="HasData"/> 的事），
    /// 这里只是不给一个走错路的调用方留下毒值。<b><paramref name="index"/> 不校验</b>：
    /// 它来自 <c>for (i = 0; i &lt; count; i++)</c>，越界的下标会画到框外去，那是调用方的问题。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// 本函数<b>不做</b>"要不要画"的政策决定（零尺寸绘图区照算，收手是 <see cref="RenderChart"/> 的事）。
    /// </para>
    /// </remarks>
    public static (Rect Bar, double Baseline) LayoutBar(
        int index,
        int count,
        double value,
        ValueRange range,
        Rect plotArea,
        double gapRatio)
    {
        double baseline = BaselineY(range, plotArea);

        if (count <= 0 || !double.IsFinite(plotArea.Width) || plotArea.Width <= 0d)
        {
            return (new Rect(plotArea.X, baseline, 0d, 0d), baseline);
        }

        // 间距：非有限按 0 处理（与 ChartSurface.PlotPadding 同一口径），其余夹到 [0, 1]。
        // 不夹的话负间距会让柱宽超过槽宽、相邻柱直接叠在一起；超过 1 的间距则给出负宽度的矩形。
        double gap = double.IsFinite(gapRatio) ? Math.Clamp(gapRatio, 0d, 1d) : 0d;

        double slot = plotArea.Width / count;
        double barWidth = slot * (1d - gap);
        double slotLeft = plotArea.X + (index * slot);
        double leftEdge = slotLeft + ((slot - barWidth) / 2d);
        double rightEdge = leftEdge + barWidth;

        double left = Math.Round(leftEdge);
        double right = Math.Round(rightEdge);

        double outer = ChartHitTest.YFor(value, range, plotArea.Y, plotArea.Height);
        double top;
        double bottom;

        if (!double.IsFinite(outer))
        {
            // "这一根没有值"：原样透传非有限，让调用方用 !IsFinite 一跳了之。
            // 折成正中或零高都会画出读者无从分辨的假柱。
            top = outer;
            bottom = outer;
        }
        else if (outer < baseline)
        {
            top = Math.Floor(outer);
            bottom = baseline;
        }
        else if (outer > baseline)
        {
            top = baseline;
            bottom = Math.Ceiling(outer);
        }
        else
        {
            top = baseline;
            bottom = baseline;
        }

        return (new Rect(left, top, right - left, bottom - top), baseline);
    }

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
        double baseline = BaselineY(window, plotArea);
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
    /// 零线的行号：值 <c>0</c> 在 <paramref name="range"/> 里的位置，夹进绘图区、对齐到整像素。
    /// </summary>
    /// <param name="range">纵轴值域，正常路径上含 0（<see cref="ZeroAnchored"/> 保证）。</param>
    /// <param name="plotArea">绘图矩形。</param>
    /// <returns>零线所在的行号，<b>整数</b>（柱的两端要对齐到它，见 <see cref="LayoutBar"/>）。</returns>
    /// <remarks>
    /// <para>
    /// <b>夹紧的两端就是"靠近 0 的那条边"</b>：<see cref="ChartHitTest.YFor"/> 把值域上界映到顶边、
    /// 下界映到底边，所以 0 落在上界之外时夹到顶边、落在下界之外时夹到底边，正是"取靠近 0 的边界"。
    /// </para>
    /// <para>
    /// <b>取整到整数</b>，与柱的纵向端点同一格网格：零线若不是整数，柱贴零线的那一端就会骑在两个
    /// 设备像素之间、糊出一条边——而柱与零线相接的那条边是每根柱都要读的地方。
    /// 画零线的<b>笔画</b>再在 <see cref="RenderChart"/> 里补半格（<see cref="PixelAlign.SnapStroke"/>），
    /// 填充对齐与笔画对齐的区别见 <see cref="LayoutBar"/>。
    /// </para>
    /// <para>
    /// 上下界用 <see cref="Math.Min(double, double)"/> / <see cref="Math.Max(double, double)"/> 排序而不是
    /// 直接 <c>Math.Clamp(值, plotArea.Top, plotArea.Bottom)</c>：后者在 <c>min &gt; max</c> 时<b>抛异常</b>，
    /// 而高为负的矩形（坏掉的绘图区几何）就会走到那一步。本方法跑在 <c>Render()</c> 路径上，
    /// 那里一个异常就是一整窗白屏。
    /// </para>
    /// </remarks>
    private static double BaselineY(ValueRange range, Rect plotArea)
    {
        double low = Math.Min(plotArea.Top, plotArea.Bottom);
        double high = Math.Max(plotArea.Top, plotArea.Bottom);
        double y = ChartHitTest.YFor(0d, range, plotArea.Y, plotArea.Height);

        return Math.Round(Math.Clamp(y, low, high));
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
