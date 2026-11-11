using Avalonia;
using Avalonia.Media;
using Tsp.Contracts.Market;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// K 线图：上方画蜡烛（实体 + 上下影线），下方一条成交量柱，涨跌各用一种色。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的四件事：价格面板的网格线、蜡烛、成交量面板的地板线、成交量柱。
/// 涨跌色是本控件<b>追加</b>的画笔（<see cref="BullBrush"/> / <see cref="BearBrush"/>，
/// 基座允许派生控件追加，且这两个语义色<b>只用于价格与 K 线</b>）；基座的
/// <see cref="ChartSurface.PrimaryBrush"/> <b>留给均线</b>——本阶段没有均线（指标流水线在 P3），
/// 所以它此刻没有使用者，这是刻意的：位子先留着，等指标接上时不必再改签名。
/// </para>
/// <para>
/// <b>涨跌按昨收判，不按开盘判</b>（<see cref="IsBull"/>）：A 股行情软件都是相对<b>昨收</b>着色，
/// 而且只有这条规则画得出<b>一字板</b>（开盘 = 最高 = 最低 = 收盘，相对开盘永远是"平"）。
/// 详见 <see cref="IsBull"/>。
/// </para>
/// <para>
/// <b>数据形状是列表，不是定长序列——折线图留给 K 线的那道题因此不适用。</b>
/// <see cref="Bars"/> 里只有真实存在的交易日：停牌那天不在表里，所以没有"空洞"可画，
/// x 轴按下标排的语义是"第 n 根 K 线"而不是"第 n 个交易日"。
/// <see cref="LineChart.BuildPoints"/> 那条"跨过缺口、不断开"的刻意偏离是<b>折线图独有的</b>，
/// 这里既不继承、也不需要改任何签名（详见该方法的文档末尾）。
/// </para>
/// <para>
/// <b>能测的部分全被推进了纯静态函数。</b><c>DrawingContext</c> 不可构造，
/// <c>RenderChart</c> 里画的每一笔在自动化里都断言不了，于是"哪根涨、哪根跌"（<see cref="IsBull"/>）、
/// "两个面板怎么分"（<see cref="SplitPanels"/>）、"窗口盖住哪些价"（<see cref="PriceRange"/> /
/// <see cref="VolumeRange"/>）、"一根蜡烛落在哪"（<see cref="LayoutCandle"/>）都是纯函数，
/// 由 <c>KLineLayoutTests</c> 直接调用。<c>RenderChart</c> 只剩「取哪一根、按哪支画笔、按什么顺序画」。
/// </para>
/// </remarks>
public sealed class KLineChart : ChartSurface
{
    /// <summary>价格面板占绘图区高度的比例，余下的留给成交量面板（再扣掉中间的缝）。</summary>
    /// <remarks>
    /// 0.72 是"价格是主角、成交量是配角"的常见配比：留给成交量的高度够看出放量的轮廓，
    /// 又不至于把价格压扁——价格面板里要放四价加上影线与实体。
    /// </remarks>
    private const double PricePanelRatio = 0.72d;

    /// <summary>两个面板之间的缝（像素）：不给缝的话蜡烛会直接贴在成交量柱上。</summary>
    private const double PanelGap = 4d;

    /// <summary>实体在槽里让出的空白比例。与柱状图同一个默认，K 线之间也留一点点缝。</summary>
    private const double CandleGapRatio = 0.2d;

    /// <summary>影线宽度。整数宽（1px）才走得出 <see cref="PixelAlign.SnapStroke"/> 的奇偶规则。</summary>
    private const double WickStrokeThickness = 1d;

    /// <summary>价格面板网格线的目标条数，与折线 / 柱状图一致，好让并排的图疏密一样。</summary>
    private const int PriceGridLineTargetCount = 4;

    /// <summary>成交量面板网格线的目标条数。那条面板只有几十像素高，四格会把它画成一张格子纸。</summary>
    private const int VolumeGridLineTargetCount = 2;

    /// <summary>K 线的数据（按时间顺序）。</summary>
    public static readonly StyledProperty<IReadOnlyList<DailyBar>?> BarsProperty =
        AvaloniaProperty.Register<KLineChart, IReadOnlyList<DailyBar>?>(nameof(Bars));

    /// <summary>涨（收在昨收之上）的画笔，必须由样式指向 <c>BullBrush</c> 令牌。</summary>
    public static readonly StyledProperty<IBrush?> BullBrushProperty =
        AvaloniaProperty.Register<KLineChart, IBrush?>(nameof(BullBrush));

    /// <summary>跌（收在昨收之下或与之相等）的画笔，必须由样式指向 <c>BearBrush</c> 令牌。</summary>
    public static readonly StyledProperty<IBrush?> BearBrushProperty =
        AvaloniaProperty.Register<KLineChart, IBrush?>(nameof(BearBrush));

    static KLineChart()
    {
        // 数据与两支涨跌画笔都会改变画面，三个都要登记：少了它，换一套涨跌色也不重绘
        // （AffectsRender 的注册状态没有公开查询接口，漏登记在运行期没有任何信号）。
        AffectsRender<KLineChart>(BarsProperty, BullBrushProperty, BearBrushProperty);
    }

    /// <summary>K 线的数据，按时间顺序排列；<b>每个元素是一根 K 线</b>。为 <c>null</c> 或空表示没有数据。</summary>
    /// <remarks>
    /// 用 P0 已冻结的 <see cref="DailyBar"/>（四价 + 成交量 + 成交额）：价格是 <c>decimal</c>，
    /// 契约说"缺字段取默认值"，于是缺字段的那根会是一根全 0 的 K 线——它<b>照画不误</b>
    /// （零价会被窗口包含进去，画面上看得见一根塌到底的线），不在这里替数据层做校验：
    /// 合法性是 P2 的 API 边界职责。
    /// </remarks>
    public IReadOnlyList<DailyBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    /// <summary>涨的画笔。赋值只发生在样式库里（<b>不得</b>在 C# 里写字面色值）。</summary>
    public IBrush? BullBrush
    {
        get => GetValue(BullBrushProperty);
        set => SetValue(BullBrushProperty, value);
    }

    /// <summary>跌的画笔。赋值只发生在样式库里。</summary>
    public IBrush? BearBrush
    {
        get => GetValue(BearBrushProperty);
        set => SetValue(BearBrushProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的数据：序列里有 K 线就够。
    /// </summary>
    /// <remarks>
    /// <b>这里不需要其余图表那条"至少一个有限值"的判据</b>（Task 7 定下的全局约束）：
    /// 那条判据防的是"整条序列都是 <c>NaN</c> 却撑起一套假刻度"，而 <see cref="DailyBar"/> 的价格是
    /// <c>decimal</c>——它<b>不可能</b>是 <c>NaN</c> 或 <c>±Infinity</c>，那条失效模式在这条数据上不存在。
    /// 挡 <c>null</c> 与空表即可（空表同样会让基座整块收手：没有数据却画出一套刻度，
    /// 比什么都不画更容易被误读成"价格全是 0"）。
    /// </remarks>
    protected override bool HasData => Bars is { Count: > 0 };

    /// <summary>
    /// 第 <paramref name="index"/> 根 K 线是不是<b>涨</b>的：收盘高于<b>昨收</b>。
    /// </summary>
    /// <param name="bars">按时间顺序的 K 线，至少 <paramref name="index"/> + 1 根。</param>
    /// <param name="index">第几根，从 <c>0</c> 起。</param>
    /// <returns>涨返回 <c>true</c>（取 <see cref="BullBrush"/> 色），其余一律 <c>false</c>（取 <see cref="BearBrush"/> 色）。</returns>
    /// <remarks>
    /// <para>
    /// <b>按昨收判，不按开盘判。</b>A 股行情软件都是相对<b>昨收</b>着色（当天的涨跌幅就是相对昨收算的），
    /// 而且这条规则才能正确处理<b>一字板</b>：开盘 = 最高 = 最低 = 收盘的那种 K 线，
    /// 相对开盘永远是"平"，相对昨收却明确地是涨停或跌停。按开盘判会把一根一字涨停画成绿的。
    /// </para>
    /// <para>
    /// <b>第一根没有昨收</b>，退回"收盘 <b>&gt;</b> 开盘"：这不是偏好，而是手上只有这两个价可用，
    /// 而它至少回答了"这根是阳线还是阴线"这个更弱的问题。判据取<b>严格大于</b>，
    /// 与下面"平盘取跌色"是同一条口径（第一根收平同样取跌色）。
    /// </para>
    /// <para>
    /// <b>平盘取跌色。</b>只有两支画笔，而"平"既不是涨也不是跌；二元口径下只能把它归到一边，
    /// 归给跌是因为"涨"是一个需要证据的断言（必须真的高于昨收），其余都算没有涨。
    /// 这条同时定住了"第一根收平"的行为。本文件把它写明，是为了不让它变成一处没人知道的口径。
    /// </para>
    /// </remarks>
    public static bool IsBull(IReadOnlyList<DailyBar> bars, int index)
    {
        DailyBar bar = bars[index];

        return index > 0
            ? bar.Close > bars[index - 1].Close
            : bar.Close > bar.Open;
    }

    /// <summary>
    /// 价格面板的值域：<b>四价全参与</b>。
    /// </summary>
    /// <param name="bars">按时间顺序的 K 线。</param>
    /// <returns>
    /// 全部 K 线的开盘 / 最高 / 最低 / 收盘里的最小与最大。空表返回 <see cref="ValueRange.From"/> 的兜底窗口。
    /// </returns>
    /// <remarks>
    /// <b>为什么不只用最高 / 最低。</b>合法数据里开盘与收盘都夹在最高最低之间，只看影线就够了；
    /// 但契约允许缺字段（那会得到一根全 0 的 K 线）与脏数据（收盘跑到最高之上），
    /// 那时只按影线定窗口会让实体画到框外去——而"窗口盖住画出来的每一样东西"是这里唯一的不变量。
    /// 代价是一帧一次 O(n) 的四路比较，在每帧重建刻度面前可以忽略。
    /// </remarks>
    public static ValueRange PriceRange(IReadOnlyList<DailyBar> bars)
    {
        if (bars is null || bars.Count == 0)
        {
            return ValueRange.From([]);
        }

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (DailyBar bar in bars)
        {
            min = Math.Min(min, Math.Min(Math.Min((double)bar.Open, (double)bar.Close), Math.Min((double)bar.Low, (double)bar.High)));
            max = Math.Max(max, Math.Max(Math.Max((double)bar.Open, (double)bar.Close), Math.Max((double)bar.Low, (double)bar.High)));
        }

        return new ValueRange(min, max);
    }

    /// <summary>
    /// 成交量面板的值域：量里<b>并入 0</b>。
    /// </summary>
    /// <param name="bars">按时间顺序的 K 线。</param>
    /// <returns>含 0 的量域（柱长要从一条看得见的零线量起，理由见 <c>BarChart.ZeroAnchored</c>）。</returns>
    /// <remarks>
    /// 成交量柱与柱状图的柱是同一件事：<b>柱长只有相对零线才读得出</b>。不并 0 的话，
    /// 一个量一直很高的区间会把窗口抬到离 0 很远的地方，柱子集体贴着底边、彼此看不出差别。
    /// 全 0 的量得到 <c>(0, 0)</c>，由 <see cref="AxisTicks.Nice"/> 撑成可画窗口。
    /// </remarks>
    public static ValueRange VolumeRange(IReadOnlyList<DailyBar> bars)
    {
        if (bars is null || bars.Count == 0)
        {
            return ValueRange.From([]);
        }

        double min = 0d;
        double max = 0d;

        foreach (DailyBar bar in bars)
        {
            double volume = bar.Volume;
            min = Math.Min(min, volume);
            max = Math.Max(max, volume);
        }

        return new ValueRange(min, max);
    }

    /// <summary>
    /// 把绘图区切成"上价格、下成交量"两块，中间留一条缝。
    /// </summary>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <returns>
    /// 两块矩形：价格面板在上、占高度 <c>PricePanelRatio</c>；成交量面板占余下的高度再扣掉缝。
    /// <b>两块的高度都夹到不小于 0</b>（控件太矮时成交量面板会退化成零高，而不是负高度）。
    /// </returns>
    /// <remarks>
    /// 缝取 <c>min(PanelGap, 总高 / 10)</c>：控件很矮时按固定 4px 留缝会把小面板吃光，
    /// 按比例收窄能让两个面板都还留得住。这个上限也让"两块高度都非负"在任何输入下成立。
    /// </remarks>
    public static (Rect PricePanel, Rect VolumePanel) SplitPanels(Rect plotArea)
    {
        double gap = Math.Min(PanelGap, plotArea.Height / 10d);
        double priceHeight = Math.Max(0d, (plotArea.Height * PricePanelRatio) - (gap / 2d));
        double volumeTop = plotArea.Y + priceHeight + gap;
        double volumeHeight = Math.Max(0d, plotArea.Bottom - volumeTop);

        return (new Rect(plotArea.X, plotArea.Y, plotArea.Width, priceHeight),
            new Rect(plotArea.X, volumeTop, plotArea.Width, volumeHeight));
    }

    /// <summary>
    /// 算出一根 K 线蜡烛的三件几何：实体矩形、影线的横坐标、影线的上下端点。
    /// </summary>
    /// <param name="index">第几根，从 <c>0</c> 起。</param>
    /// <param name="count">一共几根。<b>非正</b>时给出一根零尺寸的实体（不抛异常、不做除法）。</param>
    /// <param name="bar">这根 K 线的四价。</param>
    /// <param name="range">价格面板的值域，通常来自 <see cref="PriceRange"/>。</param>
    /// <param name="priceArea">价格面板矩形（控件局部坐标）。</param>
    /// <param name="gapRatio">实体在槽里让出的空白比例，见 <see cref="ColumnLayout.Slot"/>。</param>
    /// <returns>
    /// 实体的矩形（横向已对齐整像素、纵向已向外取整，<b>高恒不小于 1 像素</b>）、
    /// 影线的横坐标（<c>整数 + 0.5</c>，1px 笔画在 100% 缩放下正好铺满一列设备像素）、
    /// 影线的上下端点（<b>跨度也恒不小于 1 像素</b>）。任何输入都不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>实体与影线都取"向外取整"</b>（<see cref="ColumnLayout.SnapSpan"/>）：这不只是锐利的问题，
    /// 它保证<b>开盘等于收盘的十字星、以及四价全等的一字板都看得见</b>——按就近取整的话，
    /// 前者的实体高 0 像素、后者的实体与影线一起消失，而这两种 K 线在 A 股里一点都不罕见
    /// （一字板就是这样，它必须画得出来）。
    /// </para>
    /// <para>
    /// <b>影线横坐标要两步：先取整、再补半格。</b><see cref="PixelAlign.SnapStroke"/> 是半像素的
    /// <b>偏移</b>，前提是喂进去的坐标已经落在像素原点上；槽中心是 <c>(左 + 右) / 2</c>，
    /// 大概率是 <c>整数 + 0.5</c>（左右边界都是整数，宽度是奇数时中心就是半整数），
    /// 直接递给它只会得到"半整数 + 0.5"= 整数，照样骑在两列像素之间。
    /// </para>
    /// <para>
    /// <b>影线竖着画、穿过实体</b>：它先画还是后画都无所谓——同一根 K 线的实体与影线取同一个颜色。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// 它<b>不做</b>"要不要画"的政策决定（零尺寸面板照算，收手是 <see cref="RenderChart"/> 的事）。
    /// </para>
    /// </remarks>
    public static (Rect Body, double WickX, double HighY, double LowY) LayoutCandle(
        int index,
        int count,
        DailyBar bar,
        ValueRange range,
        Rect priceArea,
        double gapRatio)
    {
        if (count <= 0 || !double.IsFinite(priceArea.Width) || priceArea.Width <= 0d)
        {
            // 调用方本该先判空（有没有数据是 HasData 的事），但一个走错路的调用方不该拿到毒值：
            // 槽宽 = 面板宽度 / 0 会得到 Infinity，再往下就是一根无限宽的实体。
            return (new Rect(priceArea.X, priceArea.Y, 0d, 0d), priceArea.X, priceArea.Y, priceArea.Y);
        }

        (double left, double right) = ColumnLayout.Slot(index, count, priceArea, gapRatio);

        double openY = ChartHitTest.YFor((double)bar.Open, range, priceArea.Y, priceArea.Height);
        double closeY = ChartHitTest.YFor((double)bar.Close, range, priceArea.Y, priceArea.Height);
        double highY = ChartHitTest.YFor((double)bar.High, range, priceArea.Y, priceArea.Height);
        double lowY = ChartHitTest.YFor((double)bar.Low, range, priceArea.Y, priceArea.Height);

        (double bodyTop, double bodyBottom) = ColumnLayout.SnapSpan(openY, closeY);
        (double wickTop, double wickBottom) = ColumnLayout.SnapSpan(highY, lowY);

        // 十字星（开盘 = 收盘）与一字板（四价全等）在这里各差一步就看不见了：跨度取整之后可能是 0。
        // 补一像素，让"这根 K 线存在"这件事在画面上成立——看不见的 K 线比画得略粗一像素糟得多。
        if (bodyBottom == bodyTop)
        {
            bodyBottom += 1d;
        }

        if (wickBottom == wickTop)
        {
            wickBottom += 1d;
        }

        double wickX = PixelAlign.SnapStroke(Math.Round((left + right) / 2d), WickStrokeThickness);

        return (new Rect(left, bodyTop, right - left, bodyBottom - bodyTop), wickX, wickTop, wickBottom);
    }

    /// <summary>
    /// 画整块 K 线：价格网格 → 成交量地板线 → 蜡烛 → 成交量柱。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>两个面板各有一个值窗口，都是纯函数给的</b>：价格用 <see cref="PriceRange"/> + 整齐刻度，
    /// 成交量用 <see cref="VolumeRange"/>（含 0）+ 整齐刻度。它们<b>不共用</b>——价格的面板与量的面板
    /// 是两条独立的轴，共用一个窗口会让量柱的高度跟着价格的比例跑。
    /// </para>
    /// <para>
    /// <b>面板高度不够就整块不画</b>：价格面板或成交量面板退化成零高时，画出来的会是一块压扁的
    /// 假图（蜡烛挤在一条线上）。"这块地方画不出 K 线"是渲染方的政策决定，与基座对零尺寸绘图区的
    /// 处置同一口径。
    /// </para>
    /// <para>
    /// <b>涨跌画笔各自为 <c>null</c> 时那一类 K 线不画</b>（基座对画笔的既有口径：
    /// <c>null</c> 的含义是"这一层不画"）。两支都空时画面上只剩网格线与地板线。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<DailyBar>? bars = Bars;

        // 两条收手理由，性质不同：
        //  - bars 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是下面按下标取值的必要条件；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定。
        if (bars is null || bars.Count == 0 || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        (Rect pricePanel, Rect volumePanel) = SplitPanels(plotArea);

        if (pricePanel.Height <= 0d || volumePanel.Height <= 0d)
        {
            return;
        }

        (ValueRange priceWindow, IReadOnlyList<double> priceTicks) =
            ChartFrame.ComputeWindow(PriceRange(bars), PriceGridLineTargetCount);
        (ValueRange volumeWindow, IReadOnlyList<double> volumeTicks) =
            ChartFrame.ComputeWindow(VolumeRange(bars), VolumeGridLineTargetCount);

        // 两个面板各画各的网格线，用的是同一支笔与同一条对齐规则（行号由 ChartFrame 给）。
        // 成交量面板的刻度目标取 2：那条面板只有几十像素高，四格会把它画成一张格子纸。
        DrawHorizontalLines(context, ChartFrame.GridLineYs(priceTicks, priceWindow, pricePanel), pricePanel);
        DrawHorizontalLines(context, ChartFrame.GridLineYs(volumeTicks, volumeWindow, volumePanel), volumePanel);

        int count = bars.Count;
        IBrush? bull = BullBrush;
        IBrush? bear = BearBrush;

        if (AxisBrush is { } axisBrush)
        {
            // 成交量面板的地板线就是它的零线（量域含 0，见 VolumeRange）。0 落在窗口里时
            // 它一定也是上面那批网格线里的一条，同色同宽重叠画两次不产生视觉差异；
            // 显式画它是为了让兜底窗口（量级极端到刻度退回固定窗口）下地板线不跟着消失。
            double floor = PixelAlign.SnapStroke(ColumnLayout.BaselineY(volumeWindow, volumePanel), ChartFrame.GridStrokeThickness);

            context.DrawLine(
                new Pen(axisBrush, ChartFrame.GridStrokeThickness),
                new Point(volumePanel.X, floor),
                new Point(volumePanel.Right, floor));
        }

        for (int i = 0; i < count; i++)
        {
            DailyBar bar = bars[i];
            IBrush? brush = IsBull(bars, i) ? bull : bear;

            if (brush is null)
            {
                continue;
            }

            (Rect body, double wickX, double highY, double lowY) =
                LayoutCandle(i, count, bar, priceWindow, pricePanel, CandleGapRatio);

            context.DrawLine(
                new Pen(brush, WickStrokeThickness),
                new Point(wickX, highY),
                new Point(wickX, lowY));

            context.FillRectangle(brush, body);

            Rect column = ColumnLayout
                .Anchored(i, count, bar.Volume, volumeWindow, volumePanel, CandleGapRatio)
                .Column;

            context.FillRectangle(brush, column);
        }
    }

    /// <summary>
    /// 在每个行号处画一条横贯绘图区的线（网格线与地板线共用）。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="rows">行号（屏幕 y），由 <see cref="ChartFrame.GridLineYs"/> 给出——已对齐、已筛过非有限值。</param>
    /// <param name="panel">要横贯的面板矩形。</param>
    /// <remarks>
    /// 笔的宽度取 <see cref="ChartFrame.GridStrokeThickness"/>，与行号的对齐<b>共用同一个值</b>：
    /// 那半格是按笔画奇偶推的，两者用了不同的宽度，推出来的半格就与笔画的真实覆盖对不上。
    /// 画笔为 <c>null</c> 时整层不画。
    /// </remarks>
    private void DrawHorizontalLines(DrawingContext context, IReadOnlyList<double> rows, Rect panel)
    {
        if (AxisBrush is not { } axisBrush)
        {
            return;
        }

        Pen pen = new(axisBrush, ChartFrame.GridStrokeThickness);

        foreach (double row in rows)
        {
            context.DrawLine(pen, new Point(panel.X, row), new Point(panel.Right, row));
        }
    }
}
