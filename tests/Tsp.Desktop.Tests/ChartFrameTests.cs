using Avalonia;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="ChartFrame"/> 的两段纯算术：纵向可画窗口的选择，与网格线行号的换算 + 像素对齐。
/// </summary>
/// <remarks>
/// <para>
/// <b>这些用例是从 <c>LineChartPathTests</c> 整块搬过来的，断言一个字没改</b>
/// （只把调用前缀从 <c>LineChart.</c> 换成 <c>ChartFrame.</c>）——这次移动是一次纯粹的搬家，
/// 没有行为改动，而"测试原样通过"正是唯一能证明这一点的手段：纯重构的风险就是无声漂移，
/// 别把"能编译"当成"行为没变"。
/// </para>
/// <para>
/// <b>为什么不跟控件放一起测。</b><see cref="ChartFrame"/> 在 <c>Charts/Layout/</c>，
/// 是折线 / 柱 / K 线三个图表共用的"框"算术，与任何控件都无关——所以本文件
/// <b>不引用任何控件类型</b>，只调纯函数，在没有 UI 线程的测试里直接跑。
/// 唯一跨类型的那条用例（<c>GridLineYs_and_BuildPoints_place_the_data_inside_the_same_plot_area</c>）
/// 故意留在 <c>LineChartPathTests</c>：它要同时用到 <c>LineChart.BuildPoints</c>，
/// 放在这里会让 Layout 的测试反过来依赖控件。
/// </para>
/// <para>
/// 绘图区沿用 <see cref="ChartSurfaceContractTests"/> 里那个 <c>(8, 8, 84, 34)</c>
/// （默认内边距 8 下 100×50 的控件对应的绘图区）：左 8、上 8、右 92、下 42。
/// </para>
/// </remarks>
public sealed class ChartFrameTests
{
    /// <summary>默认内边距下 100×50 控件的绘图区：左 8、上 8、右 92、下 42。</summary>
    private static readonly Rect _plotArea = new(8d, 8d, 84d, 34d);

    [Fact]
    public void ComputeWindow_returns_the_ticks_that_define_its_own_window()
    {
        // 网格线与折线**必须共用同一个窗口**，否则网格线说的刻度与曲线落的位置是两回事。
        // 窗口就一条链：数据极值 → ValueRange.From → AxisTicks.Nice → 首尾刻度，
        // 而窗口**就是**那两条首尾刻度本身（这是"共用"这条不变量的落点，别在调用处另算一次）。
        double[] values = [3d, 11d, 7d, 5d, 9d];

        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow(values, 4);

        Assert.True(ticks.Count >= 2, "刻度至少两条——一条刻度就是零高度窗口。");
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);
        Assert.False(window.IsDegenerate);
        Assert.True(window.Min <= 3d && window.Max >= 11d, $"窗口 [{window.Min}, {window.Max}] 没盖住数据。");
        Assert.All(ticks, tick => Assert.True(double.IsFinite(tick), $"刻度成了 {tick}。"));
    }

    [Theory]
    [InlineData(new double[] { })]                                  // 空序列 → ValueRange.From 的兜底窗口
    [InlineData(new[] { 7d })]                                      // 单点 → 退化值域
    [InlineData(new[] { 7d, 7d, 7d })]                              // 全等值 → 退化值域
    [InlineData(new[] { double.NaN, double.PositiveInfinity })]     // 一个有限值都没有
    [InlineData(new[] { double.MaxValue, double.MinValue })]        // 跨度溢出成 +Infinity
    public void ComputeWindow_always_returns_a_drawable_window(double[] values)
    {
        // 无论数据多糟糕，窗口都必须可画：**有限且非退化**。零跨度窗口会把折线推去 0/0 = NaN，
        // 非有限窗口会让每一行网格线一起变成 NaN——而这里跑在 Render() 路径上，
        // 一个异常或一个毒值就是一整窗白屏。
        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow(values, 4);

        Assert.True(double.IsFinite(window.Min) && double.IsFinite(window.Max), $"窗口成了 [{window.Min}, {window.Max}]。");
        Assert.True(window.Min < window.Max, $"窗口 [{window.Min}, {window.Max}] 是退化的。");
        Assert.True(ticks.Count >= 2);
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);

        // 盖住每一个有限值（非有限值不参与值域，由 ValueRange.From 自己滤掉）。
        double[] finite = [.. values.Where(double.IsFinite)];
        if (finite.Length > 0)
        {
            Assert.True(
                window.Min <= finite.Min() && window.Max >= finite.Max(),
                $"窗口 [{window.Min}, {window.Max}] 没盖住数据 [{finite.Min()}, {finite.Max()}]。");
        }
    }

    [Fact]
    public void ComputeWindow_on_values_too_large_to_stretch_falls_back_to_the_fixed_window()
    {
        // 唯一一个"可画但不盖住数据"的角落，来自 AxisTicks.Nice 自己的兜底：
        // 全等值且量级逼近 double.MaxValue 时，向外撑开的值域会溢出成 ±Infinity，
        // 它于是退回固定窗口 (0, 1)。窗口本身仍然可画（有限、非退化），但不覆盖数据——
        // 这是 Task 2 已经写明的让步，不在本控件手里；记在这里是因为它是"窗口覆盖数据"
        // 这条不变量**唯一的例外**，别让后来者以为它无条件成立。
        // 现实中到不了：|值| ≈ 1.8e308 的价格或指标没有意义（真出现也是脏数据）。
        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow([double.MaxValue, double.MaxValue], 4);

        Assert.Equal(new ValueRange(0d, 1d), window);
        Assert.True(ticks.Count >= 2);
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);
    }

    [Fact]
    public void GridLineYs_snaps_every_tick_row_and_puts_the_ends_on_the_plot_edges()
    {
        // 一个刻度一行，一行不落。对齐分两步：行号先吸到最近的整数（100% 缩放下的设备像素网格），
        // 再按笔画奇偶推半格，于是每个行号都是"整数 + 0.5"，1px 的笔画覆盖 [⌊y⌋, ⌊y⌋ + 1]、
        // 正好铺满一列设备像素。
        // 首尾两行正好落在绘图区的下 / 上边缘上——这只在"窗口 == 首尾刻度"时成立，
        // 所以这条同时钉住了"网格线与折线共用一个窗口"。
        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow([3d, 11d, 7d, 5d, 9d], 4);

        IReadOnlyList<double> rows = ChartFrame.GridLineYs(ticks, window, _plotArea);

        Assert.Equal(ticks.Count, rows.Count);
        Assert.Equal(_plotArea.Bottom + 0.5d, rows[0]);     // 窗口下界 → 底边
        Assert.Equal(_plotArea.Top + 0.5d, rows[^1]);       // 窗口上界 → 顶边
        Assert.All(rows, y =>
        {
            Assert.True(double.IsFinite(y), $"网格线落在 {y}。");
            Assert.Equal(0.5d, y - Math.Floor(y));          // 1px 笔画的中线必须落在"整数 + 0.5"上
        });

        // 刻度值递增、屏幕 y 向下，所以行号必须严格递减（值轴朝上）。
        for (int i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i] < rows[i - 1], $"第 {i} 行没有在往上走：{rows[i - 1]} → {rows[i]}。");
        }
    }

    [Fact]
    public void GridLineYs_rounds_a_fractional_row_onto_the_pixel_grid_before_nudging()
    {
        // 两条断言都要在，缺一不可：**SnapStroke 是半像素的偏移，不是吸附**——它假定喂进去的
        // 坐标已经落在像素原点（整数）上，只负责按笔画奇偶补那半格。只做它、不做取整，
        // 得到的是"小数 + 0.5"：看着像对齐，实际照样骑在两行设备像素之间。
        // 这里挑一个**一定**映射出小数行号的窗口（3.5 与 5 都不整齐），钉住取整那一步。
        ValueRange window = new(0d, 7d);
        IReadOnlyList<double> ticks = [0d, 1d, 2d];

        IReadOnlyList<double> rows = ChartFrame.GridLineYs(ticks, window, _plotArea);

        // 原始行号是 42、37.142857…、32.285714…（8 + (1 - v/7) × 34）：只有取整之后
        // 才落到 42.5 / 37.5 / 32.5；漏掉取整的话这里会是 37.642857… 与 32.785714…。
        Assert.Equal(new[] { 42.5d, 37.5d, 32.5d }, rows);
    }

    [Fact]
    public void GridLineYs_skips_ticks_that_do_not_map_to_a_finite_row()
    {
        // 非有限的 y = 这一行画不出来。让它带着出生坐标进几何，会把同一帧里所有坐标一起染成 NaN
        // （而绘制是整块画布一起重画的），所以与 BuildPoints 同一条纪律：跳过。
        IReadOnlyList<double> rows = ChartFrame.GridLineYs([0d, double.NaN, 1d], new ValueRange(0d, 1d), _plotArea);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, y => Assert.True(double.IsFinite(y)));
    }

    [Fact]
    public void GridLineYs_on_an_empty_tick_list_returns_empty()
    {
        // 刻度列表为空（调用方直接喂了个空表）：返回空表，不抛异常。
        Assert.Empty(ChartFrame.GridLineYs([], new ValueRange(0d, 1d), _plotArea));
    }
}
