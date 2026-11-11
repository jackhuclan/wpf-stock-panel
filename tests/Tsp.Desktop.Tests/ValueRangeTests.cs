using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="ValueRange"/> 的边界行为。
/// 这些函数跑在 <c>Render()</c> 里，抛出去就是整窗白屏（P0 花过力气堵这一类故障），
/// 所以下面的用例主钉两件事：<b>任何数据都不抛异常</b>、<b>任何数据都不产出 NaN</b>。
/// </summary>
public sealed class ValueRangeTests
{
    [Fact]
    public void Empty_input_yields_a_degenerate_range_not_a_throw()
    {
        // 任务书把这个用例写作「degenerate」，但任务的 Resolutions 第 1 条把空集
        // 明确定为「一个可用窗口」(0, 1)，而不是 (NaN, NaN)。空集是「没有数据」，
        // 与「数据平到零跨度」（IsDegenerate）不是一回事，
        // 所以这里断言的是「有界、非 NaN、可画」，而不是 IsDegenerate。
        ValueRange range = ValueRange.From(Array.Empty<double>());

        Assert.Equal(0d, range.Min);
        Assert.Equal(1d, range.Max);
        Assert.False(range.IsDegenerate);
    }

    [Fact]
    public void Single_point_yields_a_degenerate_range()
    {
        ValueRange range = ValueRange.From(new[] { 42.5d });

        Assert.Equal(42.5d, range.Min);
        Assert.Equal(42.5d, range.Max);
        Assert.True(range.IsDegenerate);
    }

    [Fact]
    public void All_equal_values_yield_a_degenerate_range()
    {
        ValueRange range = ValueRange.From(new[] { 5d, 5d, 5d, 5d });

        Assert.Equal(5d, range.Min);
        Assert.Equal(5d, range.Max);
        Assert.True(range.IsDegenerate);
    }

    [Fact]
    public void From_ignores_NaN_and_Infinity()
    {
        // 一个坏点不得毁掉整条线：非有限值被丢掉，值域仍由剩下的有限值决定。
        ValueRange range = ValueRange.From(
            new[] { 1d, double.NaN, 3d, double.PositiveInfinity, -4d, double.NegativeInfinity, 2d });

        Assert.Equal(-4d, range.Min);
        Assert.Equal(3d, range.Max);
        Assert.False(range.IsDegenerate);
    }

    [Fact]
    public void From_treats_an_all_non_finite_series_as_empty()
    {
        // 全是坏点等价于没有数据：退回可用窗口，不是 (NaN, NaN)。
        ValueRange range = ValueRange.From(
            new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity });

        Assert.Equal(0d, range.Min);
        Assert.Equal(1d, range.Max);
        Assert.False(range.IsDegenerate);
    }

    [Fact]
    public void From_treats_a_null_sequence_as_empty()
    {
        // 「不抛异常」是硬约束：取数链路里一个漏判的 null 不应该打死整帧绘制。
        ValueRange range = ValueRange.From(null!);

        Assert.Equal(0d, range.Min);
        Assert.Equal(1d, range.Max);
        Assert.False(range.IsDegenerate);
    }

    [Fact]
    public void From_enumerates_the_sequence_only_once()
    {
        // 单遍扫描：惰性序列（比如 LINQ 链或逐段解析）不能被物化两次。
        var enumerations = 0;

        IEnumerable<double> Values()
        {
            enumerations++;
            yield return 3d;
            yield return 1d;
            yield return 2d;
        }

        ValueRange range = ValueRange.From(Values());

        Assert.Equal(1, enumerations);
        Assert.Equal(1d, range.Min);
        Assert.Equal(3d, range.Max);
    }

    [Fact]
    public void Default_value_is_degenerate()
    {
        // default(ValueRange) 是 (0, 0)：没填过数据的范围没有跨度。
        Assert.True(default(ValueRange).IsDegenerate);
    }
}
