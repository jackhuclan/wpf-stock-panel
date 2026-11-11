using System.Reflection;
using Avalonia;
using Tsp.Desktop.Charts;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="BarChart"/> 的柱位映射（<see cref="BarChart.LayoutBar"/>）与"含 0"的窗口口径
/// （<see cref="BarChart.ZeroAnchored"/>），外加控件自身几条可离线断言的契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 的构造函数不可访问，所以 <c>RenderChart</c> 里画的每一笔在本工程里
/// <b>都断言不了</b>——「画笔为 <c>null</c> 时不抛异常」「柱真的填了色」只能靠实机验证。
/// 于是整条链压进纯函数：柱位与零线在 <see cref="BarChart.LayoutBar"/>（本文件），
/// 窗口与刻度在 <see cref="ChartFrame"/>（<c>ChartFrameTests</c>），
/// <c>RenderChart</c> 只剩下「取哪个值、按画笔是否为 <c>null</c> 决定画不画、按什么顺序画」。
/// </para>
/// <para>
/// <b>本文件覆盖</b>：柱的方向（正负分向零线两侧）、全负数据不塌成零高、零值柱零高、
/// 不重叠与不越框、槽宽/宽度退化、亚像素柱的最小可见高度、像素对齐到整格、
/// 零线在框外的夹紧与退化值域的落点、间距的夹紧、非有限值原样透传、"含 0"窗口对
/// 全正数据的实际效果、<c>HasData</c> 的口径（非空**且至少一个有限值**）、
/// 两个 <c>StyledProperty</c> 的注册。
/// <b>不覆盖</b>任何绘制行为（画笔为 <c>null</c>、柱真的画在哪、零线画没画、有没有真的变锐）
/// ——那部分只有实机验证一条路。
/// </para>
/// <para>
/// 装饰用的绘图区沿用 <see cref="ChartSurfaceContractTests"/> 里那个 <c>(8, 8, 84, 34)</c>
/// （默认内边距 8 下 100×50 的控件对应的绘图区）：左 8、上 8、右 92、下 42、垂直中心 25。
/// 除了专门验证像素对齐的那两条，其余用例都在这块整格绘图区上断言<b>精确相等</b>。
/// </para>
/// </remarks>
public sealed class BarLayoutTests
{
    /// <summary>默认内边距下 100×50 控件的绘图区：左 8、上 8、右 92、下 42。</summary>
    private static readonly Rect _plotArea = new(8d, 8d, 84d, 34d);

    /// <summary>绘图区的垂直中心。零值域（全等值）的零线落在这里。</summary>
    private const double PlotCentreY = 25d;

    /// <summary>默认的槽内空白比例，与 <see cref="BarChart.GapRatio"/> 的默认值一致。</summary>
    private const double DefaultGap = 0.2d;

    [Fact]
    public void Positive_and_negative_values_grow_in_opposite_directions_from_the_baseline()
    {
        // Review Focus 第 3 项「柱高方向反了」的落点。零线两侧的柱必须从零线各往一头长：
        // 值的符号决定方向，而"哪一头"由 YFor 的翻转给出（值越大 y 越小）。
        // 值域含 0 时零线落在绘图区正中（窗口 (-1, 1) → y = 25）。
        ValueRange window = new(-1d, 1d);

        (Rect up, double baseline) = BarChart.LayoutBar(0, 2, 1d, window, _plotArea, DefaultGap);
        (Rect down, _) = BarChart.LayoutBar(1, 2, -1d, window, _plotArea, DefaultGap);

        Assert.Equal(PlotCentreY, baseline);

        // 正的向上：柱底贴在零线上、柱顶在零线上方。
        Assert.Equal(baseline, up.Bottom);
        Assert.True(up.Top < baseline, $"正值柱的顶边 {up.Top} 没有在零线 {baseline} 之上。");

        // 负的向下：柱顶贴在零线上、柱底在零线下方。
        Assert.Equal(baseline, down.Top);
        Assert.True(down.Bottom > baseline, $"负值柱的底边 {down.Bottom} 没有在零线 {baseline} 之下。");

        // 对称的输入给出一样长的柱——方向翻了、长度不该跟着变。
        Assert.Equal(up.Height, down.Height);

        // 两头都摸到绘图区的边：±1 正好是窗口的上下界。
        Assert.Equal(_plotArea.Top, up.Top);
        Assert.Equal(_plotArea.Bottom, down.Bottom);
    }

    [Fact]
    public void All_negative_values_still_produce_visible_bars()
    {
        // 全负数据（净流出、回撤、差分）不许"全部塌成零高"——那正是"柱长从零线量起"写错时的样子。
        // 这条用**含 0 的窗口** (-3, 0)：零线就是绘图区顶边（0 是这批数据的最大值），
        // 所有柱从顶边往下挂，越长越负；若按就近取整（而不是向外取整），最接近 0 的那根会消失。
        ValueRange window = new(-3d, 0d);

        (Rect longest, double baseline) = BarChart.LayoutBar(2, 3, -3d, window, _plotArea, DefaultGap);
        (Rect middle, _) = BarChart.LayoutBar(1, 3, -2d, window, _plotArea, DefaultGap);
        (Rect shortest, _) = BarChart.LayoutBar(0, 3, -1d, window, _plotArea, DefaultGap);

        Assert.Equal(_plotArea.Top, baseline);

        Assert.All(new[] { longest, middle, shortest }, bar =>
        {
            Assert.True(bar.Height >= 1d, $"柱高成了 {bar.Height}——没有可读的柱。");
            Assert.Equal(baseline, bar.Top);
            Assert.True(bar.Bottom > baseline);
        });

        // 量的顺序必须保住：越负的柱越长。方向对了但长度反了同样是错的图。
        Assert.True(longest.Height > middle.Height && middle.Height > shortest.Height);

        // 精确值：34 / 3 的像素高度向外取整后是 12 / 23 / 34。
        Assert.Equal(12d, shortest.Height);
        Assert.Equal(23d, middle.Height);
        Assert.Equal(34d, longest.Height);
        Assert.Equal(_plotArea.Bottom, longest.Bottom);
    }

    [Fact]
    public void Zero_value_produces_a_zero_height_bar_at_the_baseline()
    {
        // 值为 0 的柱高就是 0 像素——零线就在那里，柱长为零是实话，不是"画不出来"。
        // 更要紧的是方向：零高**不能**画成一条负高度的矩形（那在有的渲染实现里会整根翻过去）。
        ValueRange window = new(-1d, 1d);

        (Rect bar, double baseline) = BarChart.LayoutBar(0, 1, 0d, window, _plotArea, DefaultGap);

        Assert.Equal(0d, bar.Height);
        Assert.Equal(baseline, bar.Y);
        Assert.Equal(baseline, bar.Bottom);
        Assert.True(bar.Height >= 0d, "柱高成了负数——矩形翻了过去。");
        Assert.Equal(PlotCentreY, baseline);
    }

    [Fact]
    public void Bars_do_not_overlap_and_stay_inside_the_plot_area()
    {
        // 相邻柱不许重叠（重叠会把两根柱读成一根），也不许越出绘图区的左右边界。
        // 这两件事都不是"留白算得巧"，而是横向两条边各自独立取整的必然：相邻柱相邻的那条边
        // 由同一个 Round 得到同一个整数，于是"挨着"与"压住"的区别是结构上的。
        ValueRange window = new(-5d, 5d);
        double[] values = [1d, 2d, 3d, 4d, 5d];

        Rect[] bars = [.. values.Select((value, index) =>
            BarChart.LayoutBar(index, values.Length, value, window, _plotArea, DefaultGap).Bar)];

        Assert.Equal([10d, 26d, 43d, 60d, 77d], bars.Select(bar => bar.Left));
        Assert.Equal([23d, 40d, 57d, 74d, 90d], bars.Select(bar => bar.Right));

        foreach (Rect bar in bars)
        {
            Assert.InRange(bar.Left, _plotArea.Left, _plotArea.Right);
            Assert.InRange(bar.Right, _plotArea.Left, _plotArea.Right);
            Assert.InRange(bar.Top, _plotArea.Top, _plotArea.Bottom);
            Assert.InRange(bar.Bottom, _plotArea.Top, _plotArea.Bottom);
        }

        for (int i = 1; i < bars.Length; i++)
        {
            Assert.True(bars[i].Left >= bars[i - 1].Right, $"第 {i} 根柱压住了第 {i - 1} 根。");
        }
    }

    [Theory]
    [InlineData(0d)]            // 不留白：相邻柱应当正好挨着
    [InlineData(-1d)]           // 负间距：夹到 0，不许把柱撑得比槽还宽
    [InlineData(5d)]            // 过大的间距：夹到 1，柱宽归零而不是变成负宽度
    [InlineData(double.NaN)]    // 非有限：按 0 处理（与 PlotPadding 同一口径）
    public void Extreme_gap_ratios_never_produce_overlapping_bars(double gapRatio)
    {
        // 间距是样式/页面给的，没人保证它在 [0, 1] 里。夹紧是 LayoutBar 的责任：
        // 负间距会让柱宽超过槽宽（相邻柱叠在一起），超过 1 的间距会给出负宽度的矩形。
        ValueRange window = new(-1d, 1d);

        Rect[] bars = [.. Enumerable.Range(0, 3).Select(index =>
            BarChart.LayoutBar(index, 3, 1d, window, _plotArea, gapRatio).Bar)];

        for (int i = 1; i < bars.Length; i++)
        {
            Assert.True(bars[i].Left >= bars[i - 1].Right, $"间距 {gapRatio} 下第 {i} 根柱压住了第 {i - 1} 根。");
        }

        Assert.All(bars, bar => Assert.True(bar.Width >= 0d, $"柱宽成了 {bar.Width}。"));
        Assert.All(bars, bar => Assert.InRange(bar.Left, _plotArea.Left, _plotArea.Right));
        Assert.All(bars, bar => Assert.InRange(bar.Right, _plotArea.Left, _plotArea.Right));
    }

    [Fact]
    public void A_gap_ratio_of_zero_makes_adjacent_bars_touch_exactly()
    {
        // 不留白时相邻柱共享同一条边——这是"两条边各自取整"最直白的一处证明：
        // 若改成"先取整左边界、再加宽度"，这一条会立刻变红（累积的取整误差会留出缝或叠起来）。
        ValueRange window = new(-1d, 1d);

        (Rect first, _) = BarChart.LayoutBar(0, 3, 1d, window, _plotArea, 0d);
        (Rect second, _) = BarChart.LayoutBar(1, 3, 1d, window, _plotArea, 0d);

        Assert.Equal(8d, first.Left);
        Assert.Equal(second.Left, first.Right);
        Assert.Equal(_plotArea.Right, BarChart.LayoutBar(2, 3, 1d, window, _plotArea, 0d).Bar.Right);
    }

    [Fact]
    public void A_bar_shorter_than_a_pixel_still_gets_one_pixel()
    {
        // 远离零线的那条边**向外**取整（值大于 0 时 Floor、小于 0 时 Ceiling），于是任何非零的柱
        // 至少占一个像素。按就近取整的话，量级只有零点几个像素的柱会塌成零高、凭空消失——
        // 而在读图上，"柱很短"与"这里没有柱"是两件完全不同的事。
        ValueRange window = new(-1d, 1d);

        // 值 0.02 → y ≈ 24.66（离零线 25 只有 0.34 像素）→ 顶边 Floor 到 24，高 1 像素。
        (Rect tiny, double baseline) = BarChart.LayoutBar(0, 1, 0.02d, window, _plotArea, DefaultGap);

        Assert.Equal(PlotCentreY, baseline);
        Assert.Equal(24d, tiny.Top);
        Assert.Equal(1d, tiny.Height);

        // 负方向对称：底边 Ceiling 到 26，同样是 1 像素。
        (Rect tinyNegative, _) = BarChart.LayoutBar(0, 1, -0.02d, window, _plotArea, DefaultGap);

        Assert.Equal(26d, tinyNegative.Bottom);
        Assert.Equal(1d, tinyNegative.Height);
    }

    [Fact]
    public void LayoutBar_snaps_every_edge_onto_whole_pixels()
    {
        // 柱是**填充**的：填充要锐利，四条边就得落在设备像素边界（100% 缩放下即整数）上。
        // 这条用一块**非整格**的绘图区来验——整格绘图区上取整是空操作，看不出取整有没有发生。
        // 横向两条边取整到最近，纵向那条离零线的边向外取整，两条规则的结果都必须是整数。
        Rect fractional = new(8.3d, 8.3d, 84d, 34d);
        ValueRange window = new(-1d, 1d);

        foreach (double value in new[] { 1d, 0.4d, 0d, -0.4d, -1d })
        {
            (Rect bar, double baseline) = BarChart.LayoutBar(1, 3, value, window, fractional, DefaultGap);

            Assert.True(IsWholePixel(bar.Left), $"左边界 {bar.Left} 不在整像素上。");
            Assert.True(IsWholePixel(bar.Right), $"右边界 {bar.Right} 不在整像素上。");
            Assert.True(IsWholePixel(bar.Top), $"顶边 {bar.Top} 不在整像素上。");
            Assert.True(IsWholePixel(bar.Bottom), $"底边 {bar.Bottom} 不在整像素上。");
            Assert.True(IsWholePixel(baseline), $"零线 {baseline} 不在整像素上。");
            Assert.True(IsWholePixel(bar.Width) && IsWholePixel(bar.Height), "柱的宽高不在整像素上。");
        }
    }

    [Fact]
    public void Baseline_is_clamped_onto_the_near_edge_when_zero_is_outside_the_window()
    {
        // 零线取"值域里 0 的位置"，0 落在窗口外时取**靠近 0 的那条边**：全负数据夹到顶边、
        // 全正数据夹到底边。夹紧之后柱照样从那条边量起，而不是整块图什么都画不出来。
        // 正常路径上到不了这里（窗口由 ZeroAnchored 保证含 0），它是给兜底窗口留的保险。
        (_, double allNegative) = BarChart.LayoutBar(0, 3, -2d, new ValueRange(-3d, -1d), _plotArea, DefaultGap);
        (_, double allPositive) = BarChart.LayoutBar(0, 3, 2d, new ValueRange(1d, 3d), _plotArea, DefaultGap);

        Assert.Equal(_plotArea.Top, allNegative);
        Assert.Equal(_plotArea.Bottom, allPositive);

        // 夹紧之后柱的方向仍按值的符号：全负数据从顶边往下挂。
        (Rect bar, _) = BarChart.LayoutBar(0, 3, -2d, new ValueRange(-3d, -1d), _plotArea, DefaultGap);

        Assert.Equal(_plotArea.Top, bar.Top);
        Assert.True(bar.Height >= 1d);
    }

    [Fact]
    public void A_degenerate_range_puts_the_baseline_in_the_vertical_centre()
    {
        // 全等值（停牌、单点）的值域零跨度，YFor 把一切值映到绘图区垂直正中，零线也在那里，
        // 于是每根柱都是零高——零跨度下"所有柱一样长"本就没有可表达的形式。
        // 关键在于它**不抛异常、不做零除**，给的是一个确定的落点。
        ValueRange degenerate = new(5d, 5d);

        (Rect bar, double baseline) = BarChart.LayoutBar(0, 2, 5d, degenerate, _plotArea, DefaultGap);

        Assert.Equal(PlotCentreY, baseline);
        Assert.Equal(0d, bar.Height);
        Assert.Equal(baseline, bar.Y);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void LayoutBar_on_a_non_finite_value_returns_non_finite_geometry(double badValue)
    {
        // "这一根没有值"（指标预热期的 NaN、坏点带来的 ±Infinity）原样透传成非有限的**纵向**几何，
        // 调用方据此跳过——判据是非有限判定，不是"是否越出绘图区"：NaN 与任何数比较都是 false，
        // 区间比较拦不住它，而折成零高或折到零线上都会画出一根读者无从分辨的假柱。
        // 横向坐标照算（它有定义），于是"跳过"这件事只剩一个判据，不必再看别处。
        ValueRange window = new(-1d, 1d);

        (Rect bar, double baseline) = BarChart.LayoutBar(0, 3, badValue, window, _plotArea, DefaultGap);

        Assert.False(double.IsFinite(bar.Y), $"Y 成了 {bar.Y}，非有限值没有被透传。");
        Assert.False(double.IsFinite(bar.Height), $"Height 成了 {bar.Height}，非有限值没有被透传。");
        Assert.True(double.IsFinite(bar.X) && double.IsFinite(bar.Width), "横向坐标不该受值的影响。");
        Assert.True(double.IsFinite(baseline), $"零线成了 {baseline}。");
    }

    [Fact]
    public void LayoutBar_on_an_empty_series_returns_a_zero_sized_bar_without_throwing()
    {
        // 契约是"调用方先判空"（有没有数据是 HasData 的事），但一个走错路的调用方不该拿到毒值：
        // 槽宽 = 绘图区宽度 / 0 会得到 Infinity，再往下就是一根无限宽的柱。
        ValueRange window = new(-1d, 1d);

        (Rect bar, double baseline) = BarChart.LayoutBar(0, 0, 0d, window, _plotArea, DefaultGap);

        Assert.Equal(0d, bar.Width);
        Assert.Equal(0d, bar.Height);
        Assert.Equal(_plotArea.Left, bar.X);
        Assert.Equal(PlotCentreY, baseline);
    }

    [Fact]
    public void LayoutBar_on_a_zero_sized_plot_area_returns_a_zero_sized_bar_without_throwing()
    {
        // 控件还没排过版（或比两倍内边距还小）时绘图区尺寸为 0，含义是"这块地方画不出来"。
        // **收手是渲染方的职责**（RenderChart 在尺寸不大于 0 时直接返回）：LayoutBar 保持成一个
        // 纯粹、无分支的映射——零宽高把柱压成零尺寸，不抛异常，也不替调用方做政策决定。
        Rect zeroSized = new(8d, 8d, 0d, 0d);

        (Rect bar, double baseline) = BarChart.LayoutBar(0, 3, 1d, new ValueRange(-1d, 1d), zeroSized, DefaultGap);

        Assert.Equal(0d, bar.Width);
        Assert.Equal(0d, bar.Height);
        Assert.True(double.IsFinite(baseline));
    }

    [Theory]
    [InlineData(0.5, 3.2, 0d, 3.2)]     // 全正：往 0 撑
    [InlineData(-3d, -1d, -3d, 0d)]     // 全负：往下撑
    [InlineData(-2d, 5d, -2d, 5d)]      // 已经跨零：一个字不动
    [InlineData(7d, 7d, 0d, 7d)]        // 退化值域：撑成以 0 为下界的区间
    [InlineData(0d, 0d, 0d, 0d)]        // 全是 0：撑无可撑，仍是 (0, 0)
    public void ZeroAnchored_stretches_the_range_to_include_zero(double min, double max, double expectedMin, double expectedMax)
    {
        ValueRange anchored = BarChart.ZeroAnchored(new ValueRange(min, max));

        Assert.Equal(expectedMin, anchored.Min);
        Assert.Equal(expectedMax, anchored.Max);
        Assert.True(anchored.Min <= 0d && anchored.Max >= 0d, $"并完 0 之后 [{anchored.Min}, {anchored.Max}] 里仍然没有 0。");
    }

    [Fact]
    public void ZeroAnchored_keeps_the_fallback_window_of_an_empty_series()
    {
        // 空序列（以及全非有限值）拿到的是 ValueRange.From 的兜底窗口 (0, 1)——本就含 0，
        // 本方法原样返回，不再撑开一次。
        Assert.Equal(new ValueRange(0d, 1d), BarChart.ZeroAnchored(ValueRange.From([])));
        Assert.Equal(new ValueRange(0d, 1d), BarChart.ZeroAnchored(ValueRange.From([double.NaN])));
    }

    [Fact]
    public void The_zero_anchored_window_keeps_every_bar_of_an_all_positive_series_visible()
    {
        // 这条走的是控件真正的那条链（ZeroAnchored → ComputeWindow → LayoutBar），
        // 也是"柱状图为什么要并 0"的实证：全正数据 [1, 2, 3] 若不并 0，窗口就是 (1, 3)，
        // 零线被夹到框底、值 1 那根柱的 y 正好也在框底，**柱高 0 像素、直接消失**。
        // 并了 0 之后窗口是 (0, 3)，零线就是框底，三根柱的高度是 12 / 23 / 34，最短的那根也看得见。
        double[] values = [1d, 2d, 3d];

        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow(
            BarChart.ZeroAnchored(ValueRange.From(values)),
            4);

        Assert.Equal(new ValueRange(0d, 3d), window);
        Assert.Equal([0d, 1d, 2d, 3d], ticks);

        Rect[] bars = [.. values.Select((value, index) =>
            BarChart.LayoutBar(index, values.Length, value, window, _plotArea, DefaultGap).Bar)];

        (_, double baseline) = BarChart.LayoutBar(0, values.Length, values[0], window, _plotArea, DefaultGap);

        Assert.Equal(_plotArea.Bottom, baseline);
        Assert.Equal([12d, 23d, 34d], bars.Select(bar => bar.Height));
        Assert.All(bars, bar =>
        {
            Assert.True(bar.Height >= 1d, $"柱高成了 {bar.Height}——窗口没含 0 时就是这个样子。");
            Assert.Equal(baseline, bar.Bottom);
        });
    }

    [Fact]
    public void BarChart_has_data_only_when_at_least_one_value_is_finite()
    {
        // HasData 是基座用来做"空数据短路"的钩子：为 false 时底色、边框、零线、网格线一概不画。
        // 没有数据却画出一套网格，比什么都不画更容易被误读成"数据全是 0"
        // （ChartSurface.HasData 的文档原话）。
        //
        // 判据与 LineChart 同一个口径（计划已把它写进全局约束）：**非空不够，必须至少有一个有限值**。
        // 整条序列全是非有限值时，ValueRange.From 会拿兜底的 (0, 1) 窗口撑起一套与序列毫无关系的刻度。
        // （HasData 是 protected，只能在派生类里用 nameof 取名字；BarChart 是 sealed，派生不了，
        // 所以按名字反射读——与 LineChartPathTests 读同一个成员的做法一致。）
        BarChart chart = new();

        Assert.False(HasDataOf(chart));

        chart.Values = [];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        Assert.False(HasDataOf(chart));

        // 只要有一个有限值就算有数据——一根柱也足够画出一根有意义的轴。
        chart.Values = [double.NaN, 5d];
        Assert.True(HasDataOf(chart));

        chart.Values = [0d];
        Assert.True(HasDataOf(chart));

        chart.Values = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void BarChart_declares_values_and_gap_ratio_as_styled_properties()
    {
        // 两者都必须是 StyledProperty 且注册在 BarChart 自己名下：
        // 数据与间距改了就重绘，靠的是静态构造里的 AffectsRender<BarChart>(…)
        // （登记有没有写由 ChartsSourceGuardTests 静态守卫，登记了会不会真的重绘只能实机验证）。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<double>?>>(BarChart.ValuesProperty);
        Assert.Equal(typeof(IReadOnlyList<double>), BarChart.ValuesProperty.PropertyType);
        Assert.Equal(nameof(BarChart.Values), BarChart.ValuesProperty.Name);
        Assert.Equal(typeof(BarChart), BarChart.ValuesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<double>>(BarChart.GapRatioProperty);
        Assert.Equal(typeof(double), BarChart.GapRatioProperty.PropertyType);
        Assert.Equal(nameof(BarChart.GapRatio), BarChart.GapRatioProperty.Name);
        Assert.Equal(typeof(BarChart), BarChart.GapRatioProperty.OwnerType);
    }

    [Fact]
    public void BarChart_is_a_sealed_chart_surface_with_the_documented_defaults()
    {
        // 无 UI 线程下能被构造出来，说明 BarChart 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(BarChart).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(BarChart).BaseType);

        BarChart chart = new();

        Assert.Null(chart.Values);

        // 默认留出两成空白：柱贴柱会连成一片，读者分不出一根柱的边界。
        Assert.Equal(0.2d, chart.GapRatio);
    }

    /// <summary>一个坐标是不是落在整像素上（有限且小数部分为 0）。</summary>
    private static bool IsWholePixel(double coordinate) =>
        double.IsFinite(coordinate) && Math.Floor(coordinate) == coordinate;

    /// <summary>
    /// 读一个 <see cref="BarChart"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(BarChart chart)
    {
        PropertyInfo hasData = typeof(BarChart).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("BarChart 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
