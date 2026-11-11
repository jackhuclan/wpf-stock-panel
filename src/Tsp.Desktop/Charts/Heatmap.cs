using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 热力图：把一个序列铺成一张等大的方格网，每格的深浅表示它在整条序列里的相对高低。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的一件事：把每格铺到该在的位置、按归一化值给一层深浅。
/// 温度色取基座的 <see cref="ChartSurface.PrimaryBrush"/>（"一切数据序列的颜色"都取它与
/// <see cref="ChartSurface.SecondaryBrush"/> 之一，<b>不另起画笔名</b>）——见 <see cref="RenderChart"/>
/// 里对"只有一支色"的说明。
/// </para>
/// <para>
/// <b>它与折线 / 柱的根本差别：不"定框"，只"归一化"。</b>热力图编码的是"在整条序列里排第几"，
/// 所以它不需要 <see cref="ChartFrame"/> 那套值轴窗口与整齐刻度（<see cref="Normalize"/> 直接用
/// 数据自身的极值），也不画网格线与刻度——方格本身就是刻度，位置只由下标决定。
/// </para>
/// <para>
/// <b>格子按整数像素对齐</b>（<see cref="LayoutCells"/>），这是它唯一一处与像素有关的事：
/// 相邻两格共用的那条边若落在小数坐标上，两格的半透明覆盖各自只铺一半，合成之后那条缝会
/// 比两侧都暗——一张满屏都是缝的热力图。对齐到整格之后缝隙在结构上不存在。
/// </para>
/// <para>
/// <b>非有限值的那一格不画</b>（留白，底面透出来）：<c>NaN</c> 的含义是"这条没有值"，
/// 把它当成最小、最大或中间色都是在替数据编一个它没有的读数。见 <see cref="LayoutCells"/>。
/// </para>
/// </remarks>
public sealed class Heatmap : ChartSurface
{
    /// <summary>最冷那一格的不透明度下限，默认 0.15。</summary>
    /// <remarks>
    /// 不取 0 是刻意的：完全透明的一格与"这里没有格子"在屏幕上是同一个样子，
    /// 而这两件事在读图时的意思完全不同（一个是"这条数据最冷"，一个是"这条没有数据"）。
    /// 非有限值走的是后一条，它连格子都不画。
    /// </remarks>
    private const double MinCellOpacity = 0.15d;

    /// <summary>待铺成方格网的序列（每个元素一格，按行优先顺序）。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Heatmap, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>每行几格。小于 1 时排不出格子（见 <see cref="LayoutCells"/>）。</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<Heatmap, int>(nameof(Columns));

    static Heatmap()
    {
        // 数据与列数都会改变画面，两个都要登记：列数是几何参数，少了它改列数也不重排
        // （AffectsRender 的注册状态没有公开查询接口，漏登记在运行期没有任何信号）。
        AffectsRender<Heatmap>(ValuesProperty, ColumnsProperty);
    }

    /// <summary>待铺成方格网的序列，<b>每个元素一格</b>，按行优先顺序（一行铺满再换行）。</summary>
    /// <remarks>
    /// 序列里允许出现非有限值（<c>NaN</c> / <c>±Infinity</c>）：它们表示"这一格没有值"，
    /// <b>不画</b>，但仍然占自己的位置——后面的格子不会因此往前挤（见 <see cref="LayoutCells"/>）。
    /// 这样一条序列中间缺几个数，整张网的行列结构不会跟着错位。
    /// </remarks>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// 每行几格，默认 <c>0</c>（也就是"没设"）。
    /// </summary>
    /// <remarks>
    /// <b>默认值就是 <c>0</c>，而 <c>0</c> 排不出格子</b>：一张热力图的"每行几格"是页面才知道的事
    /// （几个概念、几个行业），没有哪个默认值能替它猜对。所以这里不给一个看着像模像样的数字，
    /// 而是让"忘了设"表现为<b>一块空面板</b>——看得见，而不是悄悄排成某个别别扭扭的形状。
    /// 行数由数据长度与列数算出，不需要（也不接受）单独指定。
    /// </remarks>
    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的格子：序列里**至少要有一个有限值**（Task 7–10 的统一口径，见计划全局约束）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与其余图表同一个判据，理由见 <see cref="LineChart.HasData"/>。
    /// </para>
    /// <para>
    /// <b>这里有一处与折线图不同的后果，明说</b>：<see cref="Columns"/> 没设（或小于 1）时
    /// <see cref="LayoutCells"/> 返回空表，于是 <see cref="HasData"/> 为 <c>true</c> 却一格都不画，
    /// 画面是一块空面板。<b>这是有意的</b>：面板本身就表示"这里有一张热力图，只是它排不出来"，
    /// 比整块控件凭空消失要诚实。口径统一成"有没有有限值"，是为了别让六个图表各判一套。
    /// </para>
    /// </remarks>
    protected override bool HasData =>
        Values is { Count: > 0 } values && values.Any(double.IsFinite);

    /// <summary>
    /// 把序列铺成方格网：<b>下标 → 行列</b>、<b>值 → 归一化深浅</b>，格子按整数像素对齐。
    /// </summary>
    /// <param name="values">待铺的序列，每个元素一格，按行优先顺序。非有限值的那一格不产出（见备注）。</param>
    /// <param name="columns">每行几格。小于 1 时返回空表。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。宽或高为 0 时所有格子退化成零尺寸，本函数照算不误。</param>
    /// <returns>
    /// 每格一个 <c>(格子矩形, 归一化值)</c>，<b>按原下标顺序</b>排列（被跳过的非有限值不占位，
    /// 但它的位置仍然空着——后面的格子不会往前挤）。
    /// 行数为 <c>⌈格数 / 列数⌉</c>，最后一行的空位<b>不产出格子</b>。
    /// 归一化值在 <c>[0, 1]</c> 内：整条序列的最小值给 <c>0</c>、最大值给 <c>1</c>；
    /// 序列全等值（或跨度溢出、取不到有限值时）一律给 <c>0.5</c>。任何输入都不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>归一化只按数据自身的极值</b>（<see cref="ValueRange.From"/>），不做任何"固定标尺"：
    /// 热力图要回答的是"在这批数据里谁高谁低"，尺子就是这批数据自己。全等值时没有高低可言，
    /// 一律给中间值 <c>0.5</c>——那是"看不出差别"在这块画布上唯一说得通的位置，也避开了
    /// <c>0/0</c>。跨度溢出成非有限值（<c>[-double.MaxValue, double.MaxValue]</c>）走同一条退路，
    /// 与 <see cref="ChartHitTest.YFor"/> 对非有限跨度的处置一致。
    /// </para>
    /// <para>
    /// <b>格子按整数像素对齐，两条边各自独立取整</b>（不是"先取整左边界再加宽度"）。
    /// 相邻两格相邻的那条边于是由同一个 <see cref="Math.Round(double)"/> 得到同一个整数，
    /// "无缝无叠"是结构上的必然。代价与柱状图那一处相同：格宽会有 ±1 像素的出入
    /// （在一格十几像素的热力图上读不出来），而绘图区边界不是整数时，最外一条边可能被推到
    /// 框外不到一个像素——那是"格子之间不留缝"换来的，缝比这点越界显眼得多。
    /// </para>
    /// <para>
    /// <b>非有限值不产格子，但位置留着。</b>每个格子的行列只由它自己的下标算出，与别人无关；
    /// 所以跳过一格不会让后面整体前移——一条中间缺数的序列，热的形状仍然落在对的位置上。
    /// 把它画成最小色或中间色都是在替数据编一个它没有的读数。
    /// </para>
    /// <para>
    /// <b><paramref name="columns"/> 小于 1 时返回空表，而不是夹成 1 列</b>：夹紧会把"列数忘设了"
    /// 这个笔误静默成一张一列的长条，那形状看着像一个正经结果。拒绝则让它在画面上明确地空着
    /// （见 <see cref="Columns"/> 的备注）。它同时是这里的除零防线：<c>下标 / 0</c> 会得到
    /// <c>Infinity</c> 行号。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// 本函数<b>不做</b>"要不要画"的政策决定（零尺寸绘图区照算，收手是 <see cref="RenderChart"/> 的事）。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(Rect Cell, double Normalized)> LayoutCells(
        IReadOnlyList<double> values,
        int columns,
        Rect plotArea)
    {
        if (values is null || values.Count == 0 || columns < 1)
        {
            return [];
        }

        ValueRange range = ValueRange.From(values);
        int rowCount = ((values.Count - 1) / columns) + 1;
        double cellWidth = plotArea.Width / columns;
        double cellHeight = plotArea.Height / rowCount;

        var cells = new List<(Rect Cell, double Normalized)>(values.Count);

        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];

            if (!double.IsFinite(value))
            {
                continue;
            }

            int row = i / columns;
            int column = i % columns;

            // 两条边从**同一个表达式**出发各自取整：右边界与下一格的左边界算出来必然是同一个整数，
            // 相邻格因此严丝合缝——不是"留白留得巧"，而是它们本来就是同一个数。
            double left = Math.Round(plotArea.X + (column * cellWidth));
            double right = Math.Round(plotArea.X + ((column + 1) * cellWidth));
            double top = Math.Round(plotArea.Y + (row * cellHeight));
            double bottom = Math.Round(plotArea.Y + ((row + 1) * cellHeight));

            cells.Add((new Rect(left, top, right - left, bottom - top), Normalize(value, range)));
        }

        return cells;
    }

    /// <summary>
    /// 画这张热力图：每格一层 <see cref="ChartSurface.PrimaryBrush"/>，深浅由归一化值定。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>只有一支色，深浅走不透明度。</b>温度色取 <see cref="ChartSurface.PrimaryBrush"/>，
    /// 用 <c>PushOpacity</c> 按归一化值铺一层薄厚（最冷 <see cref="MinCellOpacity"/>、最热全不透明）。
    /// 用不透明度而不是在两种颜色之间插值，是因为后者要<b>从画笔里读出颜色</b>来做通道算术
    /// （只有纯色画刷读得出，渐变画刷读不出），而那会绕开"配色只在样式库里出现"这条线：
    /// 色相与主题一起变这件事，这里靠的仍然是那一支令牌画刷。
    /// <b>代价说清楚</b>：一支色画不出"红涨绿跌"那种双色渐变，本阶段的用法是单色深浅（概念 / 行业热度）。
    /// 真需要双色渐变时该改的是这里的渲染层，而不是在控件里写字面色。
    /// </para>
    /// <para>
    /// <b>不画格线。</b>格子已经并排铺满，再叠 1px 边框就是在每一格上多两笔噪声——
    /// 而热力图读的是整片深浅的分布，不是每格的轮廓。格与格的分界由颜色本身给出。
    /// </para>
    /// <para>
    /// <b>绘制顺序就是数据顺序</b>：格子互不重叠（<see cref="LayoutCells"/> 保证），
    /// 所以顺序不影响结果，按顺序画只是为了让它与数据的对应关系一眼可读。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<double>? values = Values;

        // 三条收手理由，性质不同：
        //  - values 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是 LayoutCells 的必要条件——它的入参不可空；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定；
        //  - 画笔为 null 是"这一层不画"（基座对画笔的既有口径，配色只在样式库里出现）。
        if (values is null || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        if (PrimaryBrush is not { } heatBrush)
        {
            return;
        }

        foreach ((Rect cell, double normalized) in LayoutCells(values, Columns, plotArea))
        {
            // 归一化值恒在 [0, 1]（LayoutCells 的契约），所以这里不夹紧——夹紧会掩盖那种契约被破坏
            // 的情形，而它的表现（整片颜色一样深）本来就该在测试里红掉，不该在渲染层被吃掉。
            using (context.PushOpacity(MinCellOpacity + ((1d - MinCellOpacity) * normalized)))
            {
                context.FillRectangle(heatBrush, cell);
            }
        }
    }

    /// <summary>
    /// 一个值在 <paramref name="range"/> 里排第几：<c>0</c> 是最小、<c>1</c> 是最大。
    /// </summary>
    /// <param name="value">要归一化的值，<b>必须有限</b>（调用方已经把非有限值挡在外面）。</param>
    /// <param name="range">整条序列的值域，来自 <see cref="ValueRange.From"/>。</param>
    /// <returns><c>[0, 1]</c> 内的深浅；无法归一化时（零跨度、跨度溢出）返回 <c>0.5</c>。</returns>
    /// <remarks>
    /// 跨度非有限时必须一起走退路：<c>(值 - 最小) / (最大 - 最小)</c> 在跨度溢出成 <c>+Infinity</c>
    /// 时算出来是 <c>Infinity / Infinity = NaN</c>，而负载着 <c>NaN</c> 的不透明度会让这一格整格消失
    /// （且不抛异常）。这与 <see cref="ChartHitTest.YFor"/> 处理非有限跨度的理由是同一条。
    /// </remarks>
    private static double Normalize(double value, ValueRange range)
    {
        double span = range.Max - range.Min;

        if (range.IsDegenerate || !double.IsFinite(span))
        {
            return 0.5d;
        }

        return (value - range.Min) / span;
    }
}
