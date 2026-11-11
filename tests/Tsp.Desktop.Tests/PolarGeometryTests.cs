using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="PolarGeometry.OnCircle"/>：环图与雷达图共用的那一步极坐标换算。
/// </summary>
/// <remarks>
/// 这里的用例只钉四件事：角度原点在 12 点、方向是顺时针、y 轴向下（也就是那个负号）、
/// 以及退化入参不抛异常。<b>前三条正是这类三角函数最容易错的三种方式</b>——
/// 错任何一个都还画得出图，只是整块图形整体转 90° 或上下镜像，代码上不容易看出来。
/// </remarks>
public sealed class PolarGeometryTests
{
    /// <summary>每像素的容差：三角函数的结果不可能是精确的 0 / 1，只断言到浮点误差以内。</summary>
    private const double Tolerance = 1e-12d;

    private const double CentreX = 50d;
    private const double CentreY = 30d;
    private const double Radius = 20d;

    [Fact]
    public void Angle_zero_is_the_twelve_oclock_position_not_three_oclock()
    {
        // 角度原点取 12 点而不是数学上的 3 点（或屏幕习惯的 3 点）：换一个原点，
        // 环图每一个分片的起始位置全都偏 90°，而图形本身看着仍然"是个环"。
        (double x, double y) = PolarGeometry.OnCircle(0d, CentreX, CentreY, Radius);

        Assert.Equal(CentreX, x, Tolerance);
        Assert.Equal(CentreY - Radius, y, Tolerance);
    }

    [Fact]
    public void A_quarter_turn_goes_clockwise_on_screen()
    {
        // 顺时针：四分之一圈之后到 3 点方向（x 增大）。屏幕 y 向下，所以"顺时针"这条
        // 与数学上"逆时针为正"的惯例是反的——这里的方向由 y 分量那个负号定死。
        (double x, double y) = PolarGeometry.OnCircle(Math.PI / 2d, CentreX, CentreY, Radius);

        Assert.Equal(CentreX + Radius, x, Tolerance);
        Assert.Equal(CentreY, y, Tolerance);
    }

    [Theory]
    [InlineData(0d, 0d, -1d)]                   // 12 点
    [InlineData(Math.PI / 2d, 1d, 0d)]          // 3 点（顺时针四分之一圈）
    [InlineData(Math.PI, 0d, 1d)]               // 6 点
    [InlineData(Math.PI * 1.5d, -1d, 0d)]       // 9 点
    [InlineData(Math.PI * 2d, 0d, -1d)]         // 一整圈之后回到 12 点
    public void The_four_cardinal_directions_land_where_they_should(double angle, double expectedUnitX, double expectedUnitY)
    {
        // 用单位半径直接对"单位圆上的方向"，四个象限各一格，错一个方向这条就会红。
        (double x, double y) = PolarGeometry.OnCircle(angle, 0d, 0d, 1d);

        Assert.Equal(expectedUnitX, x, Tolerance);
        Assert.Equal(expectedUnitY, y, Tolerance);
    }

    [Fact]
    public void A_zero_radius_puts_every_angle_on_the_centre()
    {
        // 半径为 0 时所有点都在圆心上：环宽为 0 的退化情形，不抛异常、也不返回毒值。
        foreach (double angle in new[] { 0d, Math.PI / 3d, Math.PI, -2d })
        {
            (double x, double y) = PolarGeometry.OnCircle(angle, CentreX, CentreY, 0d);

            Assert.Equal(CentreX, x, Tolerance);
            Assert.Equal(CentreY, y, Tolerance);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_angle_passes_through_without_throwing(double angle)
    {
        // 与 ChartHitTest.YFor 对非有限值的口径一致：原样透传非有限结果，让调用方判掉。
        // 这里折成圆心或 0 才是错的——那会把"这个角没有意义"画成一个看似正常的点。
        (double x, double y) = PolarGeometry.OnCircle(angle, CentreX, CentreY, Radius);

        Assert.False(double.IsFinite(x) || double.IsFinite(y), $"({x}, {y}) 里出现了有限坐标。");
    }

    [Fact]
    public void On_the_same_circle_every_point_is_exactly_the_radius_away_from_the_centre()
    {
        // 不变量：不管角度取什么，点到圆心的距离恒等于半径。这条能同时盖住分量接反、
        // 少乘半径、把半径加到圆心而不是乘到分量上等好几种错法。
        for (double angle = -4d; angle <= 4d; angle += 0.13d)
        {
            (double x, double y) = PolarGeometry.OnCircle(angle, CentreX, CentreY, Radius);
            double distance = Math.Sqrt(((x - CentreX) * (x - CentreX)) + ((y - CentreY) * (y - CentreY)));

            Assert.Equal(Radius, distance, 1e-9d);
        }
    }
}
