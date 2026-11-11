using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="AxisTicks"/> 的边界行为。
/// 契约是「永远能画」：<b>非空、严格递增、全部有限、步长为正</b>，
/// 且区间<b>覆盖</b>调用方给的 <c>[min, max]</c>。
/// </summary>
public sealed class AxisTicksTests
{
    [Fact]
    public void Nice_returns_expected_ticks_for_a_plain_range()
    {
        // 0..100 想要 5 条 → 步长吸附到 2 × 10¹ = 20 → 0,20,40,60,80,100（共 6 条）。
        IReadOnlyList<double> ticks = AxisTicks.Nice(0d, 100d, 5);

        AssertSameTicks(ticks, 0d, 20d, 40d, 60d, 80d, 100d);
    }

    [Fact]
    public void Nice_on_a_degenerate_range_returns_a_usable_window()
    {
        // 全 5.0：没有跨度，但轴仍然得能画 —— 上下界要把 5.0 夹在中间。
        IReadOnlyList<double> ticks = AxisTicks.Nice(5d, 5d, 5);

        AssertUsable(ticks);
        Assert.True(ticks[0] < 5d, $"下界 {ticks[0]} 没把 5.0 包进去");
        Assert.True(ticks[^1] > 5d, $"上界 {ticks[^1]} 没把 5.0 包进去");
    }

    [Fact]
    public void Nice_handles_a_huge_magnitude_spread()
    {
        // 0 与 1e9 同存：步长必须跟着量级走，不能退化成 1 或溢出。
        IReadOnlyList<double> ticks = AxisTicks.Nice(0d, 1e9d, 5);

        AssertUsable(ticks);
        Assert.Equal(0d, ticks[0]);
        Assert.Equal(1e9d, ticks[^1]);
    }

    [Fact]
    public void Nice_handles_all_negative_values()
    {
        IReadOnlyList<double> ticks = AxisTicks.Nice(-100d, -1d, 5);

        AssertUsable(ticks);
        Assert.Equal(-100d, ticks[0]);
        Assert.All(ticks, tick => Assert.True(tick <= 0d, $"刻度 {tick} 越过了 0"));
    }

    [Fact]
    public void Nice_accepts_a_reversed_range()
    {
        // 上下界写反是调用方笔误，纠正即可，不必抛异常打断整帧绘制。
        IReadOnlyList<double> ticks = AxisTicks.Nice(100d, 0d, 5);

        AssertSameTicks(ticks, 0d, 20d, 40d, 60d, 80d, 100d);
    }

    [Fact]
    public void Nice_may_return_a_count_other_than_target_count()
    {
        // 条数不保证等于 targetCount —— 这是 nice-number 算法的固有性质，不是 bug。
        IReadOnlyList<double> ticks = AxisTicks.Nice(0d, 100d, 5);

        Assert.NotEqual(5, ticks.Count);
    }

    [Theory]
    [InlineData(0d, 100d, 5)]
    [InlineData(5d, 5d, 5)]
    [InlineData(0d, 0d, 5)]
    [InlineData(-5d, -5d, 3)]
    [InlineData(0d, 1e9d, 5)]
    [InlineData(-100d, -1d, 5)]
    [InlineData(100d, 0d, 5)]                              // 上下界写反
    [InlineData(double.NaN, double.NaN, 5)]                // 两端都是 NaN
    [InlineData(double.NaN, 100d, 5)]                      // 只有一端是 NaN
    [InlineData(0d, double.PositiveInfinity, 5)]           // 只有一端是 Infinity
    [InlineData(double.NegativeInfinity, double.PositiveInfinity, 5)]
    [InlineData(-1e308d, 1e308d, 5)]                       // 区间自身溢出成 +Infinity
    [InlineData(1e307d, 1.7e308d, 2)]                      // 整齐步长自身溢出（2 × 10³⁰⁸）
    [InlineData(1e-9d, 3e-9d, 4)]                          // 极小量级
    [InlineData(-1e9d, 1e9d, 7)]                           // 大量级、跨零
    [InlineData(0d, 1d, 2)]
    [InlineData(0d, 1d, 1)]                                // targetCount 过小
    [InlineData(0d, 1d, int.MaxValue)]                     // targetCount 过大
    [InlineData(100d, 100.00000000000001d, 7)]             // 窄到 1 个 ULP：整齐刻度只落得一条
    [InlineData(2500d, 2500.0000000000005d, 5)]            // 窄到几个 ULP：相邻刻度塌成同一个值
    [InlineData(1e16d, 10000000000000004d, 100)]           // 大量级窄区间：(first + i) * step 吃掉低位
    public void Nice_always_returns_a_non_empty_ordered_list_with_a_positive_step(
        double min, double max, int targetCount)
    {
        IReadOnlyList<double> ticks = AxisTicks.Nice(min, max, targetCount);

        AssertUsable(ticks);
    }

    private static void AssertSameTicks(IReadOnlyList<double> actual, params double[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            double tolerance = 1e-9 * Math.Max(1d, Math.Abs(expected[i]));
            Assert.True(
                Math.Abs(actual[i] - expected[i]) <= tolerance,
                $"第 {i} 条刻度：期望 {expected[i]}，实际 {actual[i]}");
        }
    }

    private static void AssertUsable(IReadOnlyList<double> ticks)
    {
        Assert.True(ticks.Count >= 2, $"刻度不足两条，画不出轴：Count = {ticks.Count}");
        Assert.All(ticks, tick => Assert.True(double.IsFinite(tick), $"刻度 {tick} 不是有限值"));

        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(
                ticks[i] > ticks[i - 1],
                $"刻度未严格递增：第 {i - 1} 条 {ticks[i - 1]} → 第 {i} 条 {ticks[i]}");
        }
    }
}
