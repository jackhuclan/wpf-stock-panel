using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="ChartHitTest"/> 的边界行为。
/// 这两件事都跑在热路径上：<see cref="ChartHitTest.IndexAtX"/> 每次指针移动都要算一遍，
/// <see cref="ChartHitTest.YFor"/> 每帧对每个点都要算一遍，
/// 所以下面的用例主钉三件事：<b>越界输入返回 null 而不是异常</b>、
/// <b>两端精确落在首尾下标</b>、<b>退化值域不产出 NaN</b>。
/// </summary>
public sealed class ChartHitTestTests
{
    [Fact]
    public void IndexAtX_maps_plot_left_to_zero_and_plot_right_to_count_minus_one()
    {
        // 两个端点是硬契约，且必须精确：命中测试在右边缘错一格，就是悬停时点错了另一根 K 线。
        const double left = 40d;
        const double width = 600d;
        const int count = 7;

        Assert.Equal(0, ChartHitTest.IndexAtX(left, left, width, count));
        Assert.Equal(count - 1, ChartHitTest.IndexAtX(left + width, left, width, count));
    }

    [Fact]
    public void IndexAtX_snaps_to_the_nearest_index()
    {
        // 7 个点、600 宽 → 每段 100：40 + 100 = 140 落在第 1 个点上，正中间 340 落在第 3 个点上。
        Assert.Equal(1, ChartHitTest.IndexAtX(140d, 40d, 600d, 7));
        Assert.Equal(3, ChartHitTest.IndexAtX(340d, 40d, 600d, 7));

        // 单点序列：整条绘图区都只能命中它自己，不能算出 0 以外的下标。
        Assert.Equal(0, ChartHitTest.IndexAtX(40d, 40d, 600d, 1));
        Assert.Equal(0, ChartHitTest.IndexAtX(640d, 40d, 600d, 1));
    }

    [Theory]
    [InlineData(200d, 2)]   // t × (count - 1) = 1.6 → 最近邻是 2（换成截断就会错给 1）
    [InlineData(290d, 3)]   // t × (count - 1) 正好是 2.5 → 约定取靠后的下标
    public void IndexAtX_rounds_to_the_nearest_and_breaks_ties_upward(double x, int expected)
    {
        // 端点用例只覆盖到 t × (count - 1) 恰好是整数的位置，那时截断与取整没有区别；
        // 这两组探针才是「最近邻」与「中点取靠后」两条约定的真正钉子。
        // 290 尤其关键：默认的银行家舍入会把 2.5 舍成 2，边界就随点数奇偶左右跳变。
        Assert.Equal(expected, ChartHitTest.IndexAtX(x, 40d, 600d, 7));
    }

    [Fact]
    public void IndexAtX_returns_null_outside_the_plot_area()
    {
        // 命中测试跑在每一次指针移动上：越界必须得到 null，抛异常就是「鼠标一动就崩」。
        const double left = 40d;
        const double width = 600d;
        const int count = 7;

        Assert.Null(ChartHitTest.IndexAtX(left - 1d, left, width, count));              // 左越界
        Assert.Null(ChartHitTest.IndexAtX(left + width + 1d, left, width, count));       // 右越界
        Assert.Null(ChartHitTest.IndexAtX(left + 300d, left, 0d, count));                // 宽度为零
        Assert.Null(ChartHitTest.IndexAtX(left + 300d, left, -width, count));            // 宽度为负
        Assert.Null(ChartHitTest.IndexAtX(double.NaN, left, width, count));              // 指针坐标是 NaN
        Assert.Null(ChartHitTest.IndexAtX(left + 300d, double.NaN, width, count));       // 绘图区原点是 NaN

        // 宽度是 NaN 或 +Infinity 时，区间判断拦不住：NaN <= 0 为 false、与任何数比较也为 false，
        // 会一路算到 t = NaN、(int)NaN → 0，悄悄返回一个假下标 0。必须由显式的非有限判定拦下。
        Assert.Null(ChartHitTest.IndexAtX(left + 300d, left, double.NaN, count));        // 宽度是 NaN
        Assert.Null(ChartHitTest.IndexAtX(left + 300d, left, double.PositiveInfinity, count)); // 宽度是 +Infinity
    }

    [Fact]
    public void IndexAtX_returns_null_when_count_is_zero()
    {
        // 空数据不得除零：没有点位可命中，返回的是「没命中」而不是第 0 个（那是个不存在的下标）。
        Assert.Null(ChartHitTest.IndexAtX(100d, 40d, 600d, 0));

        // 负数是同样非法的「元素个数」，不能因为 count - 1 变成 -2 就绕开前面的检查。
        Assert.Null(ChartHitTest.IndexAtX(100d, 40d, 600d, -1));
    }

    [Fact]
    public void YFor_inverts_because_screen_y_grows_downward()
    {
        // 屏幕 y 向下增长、值轴向上增长，所以同一个值域里，值更大的点 y 更小。
        // 取 25 / 75 这种二进制上精确的值，端点算术才能断言到相等而不是「差不多」。
        var range = new ValueRange(0d, 100d);

        double low = ChartHitTest.YFor(25d, range, 20d, 200d);
        double high = ChartHitTest.YFor(75d, range, 20d, 200d);

        Assert.True(high < low, $"值更大的点 y 反而更大：YFor(75) = {high}，YFor(25) = {low}");
        Assert.Equal(170d, low);   // 20 + (1 - 0.25) × 200
        Assert.Equal(70d, high);   // 20 + (1 - 0.75) × 200
    }

    [Fact]
    public void YFor_maps_the_range_ends_to_the_plot_edges()
    {
        // 值域的端点就是可画窗口的上下边缘：上界贴顶、下界贴底，不多不少。
        var range = new ValueRange(-50d, 150d);

        Assert.Equal(20d, ChartHitTest.YFor(150d, range, 20d, 200d));    // 上界 → 顶边
        Assert.Equal(220d, ChartHitTest.YFor(-50d, range, 20d, 200d));   // 下界 → 底边
    }

    [Fact]
    public void YFor_puts_a_degenerate_range_on_the_vertical_centre()
    {
        // 零跨度做除法是 0/0 = NaN，NaN 一旦进了几何就会把一整帧的坐标全毁掉。
        // 退化值域（停牌、单点、全等值）落在绘图区正中：线画在中间，而不是消失。
        var flat = new ValueRange(5d, 5d);

        double y = ChartHitTest.YFor(5d, flat, 20d, 200d);

        Assert.Equal(120d, y);                          // 20 + 200 / 2
        Assert.True(double.IsFinite(y), $"退化值域产出了非有限值：{y}");

        // 落在正中的位置与传入的值无关 —— 没有跨度就没有可区分的位置。
        Assert.Equal(120d, ChartHitTest.YFor(999d, flat, 20d, 200d));
    }

    [Fact]
    public void YFor_passes_a_non_finite_value_through_so_the_caller_can_skip_it()
    {
        // 与上一条正好相反：值域没问题、倒是值本身没有值（指标预热期的 NaN）。
        // 这种点不折成绘图区正中 —— 折了就会画出一条垂到中间的假线，
        // 读者看到的是错误的价格走势，比缺点难查得多。
        // 契约是「把非有限原样透传出去」，跳过该点是调用方的义务
        //（Task 6 的 BuildPoints 就按 !double.IsFinite(y) 跳过）。
        // 只断言「非有限」，不锁具体是 NaN 还是 ±Infinity：调用方要的就是这个判定。
        var range = new ValueRange(0d, 100d);

        Assert.False(double.IsFinite(ChartHitTest.YFor(double.NaN, range, 20d, 200d)));
        Assert.False(double.IsFinite(ChartHitTest.YFor(double.PositiveInfinity, range, 20d, 200d)));
        Assert.False(double.IsFinite(ChartHitTest.YFor(double.NegativeInfinity, range, 20d, 200d)));
    }

    [Fact]
    public void YFor_uses_the_centre_when_the_span_overflows()
    {
        // 上下界各自有限、跨度却溢出成 +Infinity —— ValueRange.From 不会把这种值域滤成兜底窗口，
        // 而它照公式算是 (max - min) = +Infinity、t = Infinity / Infinity = NaN，一个点都画不出来。
        // 所以「跨度非有限」与「退化」走同一条退路：返回的是一个确定的位置，而不是毒值。
        var range = new ValueRange(-1e308d, 1e308d);

        Assert.Equal(120d, ChartHitTest.YFor(1e308d, range, 20d, 200d));
        Assert.Equal(120d, ChartHitTest.YFor(-1e308d, range, 20d, 200d));
        Assert.Equal(120d, ChartHitTest.YFor(0d, range, 20d, 200d));
    }

    [Theory]
    [InlineData(0d, 100d)]
    [InlineData(5d, 5d)]            // 退化：零跨度
    [InlineData(-5d, -5d)]          // 退化：零跨度，且值域整体在负数侧
    [InlineData(0d, 0d)]            // 退化：正好是 default(ValueRange)
    [InlineData(-1e9d, 1e9d)]       // 大量级、跨零
    [InlineData(-1e308d, 1e308d)]   // 上下界各自有限，跨度却溢出成 +Infinity
    [InlineData(-100d, -1d)]
    public void YFor_is_always_finite_and_inside_the_plot(double min, double max)
    {
        var range = new ValueRange(min, max);
        // 只取值域内的点：值域外的值本来就该落在绘图区外（这里不夹紧），不在本用例的射程内。
        double[] values = { min, max, (min + max) / 2d };

        foreach (double value in values)
        {
            double y = ChartHitTest.YFor(value, range, 20d, 200d);

            Assert.True(double.IsFinite(y), $"值 {value} 在值域 [{min}, {max}] 上得到非有限的 y：{y}");
            Assert.InRange(y, 20d, 220d);
        }
    }
}
