using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="PixelAlign.SnapStroke"/> 的像素对齐行为。
/// 一条整数宽的线画在整数坐标上会骑在两列设备像素之间，渲染出来是 2px 的糊边；
/// 下面的用例钉住的就是这条「奇数宽 +0.5、偶数宽不动」的规则，
/// 以及所有**不该**对齐的退化入参——它们必须原样返回，而不是被推到一个没有笔画可对齐的偏移上。
/// </summary>
public sealed class PixelAlignTests
{
    [Theory]
    [InlineData(10.0, 1.0, 10.5)]   // 1px 线落在半像素上才锐利
    [InlineData(10.0, 2.0, 10.0)]   // 偶数宽不需要偏移
    public void SnapStroke_aligns_odd_thickness_to_half_pixels(double input, double thickness, double expected)
    {
        Assert.Equal(expected, PixelAlign.SnapStroke(input, thickness));
    }

    [Theory]
    [InlineData(3.0, 1.0, 3.5)]
    [InlineData(3.0, 3.0, 3.5)]
    [InlineData(3.0, 5.0, 3.5)]
    [InlineData(3.0, 7.0, 3.5)]
    [InlineData(-4.0, 1.0, -3.5)]   // 负坐标同样只加半个像素，不取绝对值、不吸到整数
    [InlineData(0.0, 1.0, 0.5)]     // 原点上的轴线
    public void SnapStroke_offsets_every_odd_integer_thickness(double input, double thickness, double expected)
    {
        Assert.Equal(expected, PixelAlign.SnapStroke(input, thickness));
    }

    [Theory]
    [InlineData(10.0, 2.0)]
    [InlineData(10.0, 4.0)]
    [InlineData(10.0, 6.0)]
    [InlineData(10.0, 100.0)]
    [InlineData(-4.0, 2.0)]
    public void SnapStroke_leaves_even_integer_thickness_alone(double input, double thickness)
    {
        // 偶数宽度的笔画本来就对称地铺满整数像素，再推半格反而糊。
        Assert.Equal(input, PixelAlign.SnapStroke(input, thickness));
    }

    [Theory]
    [InlineData(0.0)]                        // 没有笔画
    [InlineData(-1.0)]                       // 负数宽度是调用方笔误
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SnapStroke_leaves_degenerate_thickness_alone(double thickness)
    {
        // 没有可对齐的笔画时推半个像素，等于把偏移硬塞进一段本就没有笔画的几何里。
        // 这里的硬约束是：不抛异常，且原样返回。
        Assert.Equal(10.0d, PixelAlign.SnapStroke(10.0, thickness));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.5)]
    [InlineData(2.5)]
    [InlineData(3.25)]
    public void SnapStroke_leaves_fractional_thickness_alone(double thickness)
    {
        // 奇偶规则只对整数宽度成立：非整数宽度没有可判定的奇偶，原样返回，
        // 不要现场发明一套取整规则。
        Assert.Equal(10.0d, PixelAlign.SnapStroke(10.0, thickness));
    }

    [Fact]
    public void SnapStroke_passes_a_non_finite_coordinate_through()
    {
        // 坐标本身是坏值时，函数不负责修正它，也不因它抛异常：NaN 照旧是 NaN，
        // 这样画出来的是「什么都不画」，而不是画在 0.5 这个莫名其妙的坐标上。
        Assert.True(double.IsNaN(PixelAlign.SnapStroke(double.NaN, 1d)));
        Assert.True(double.IsNaN(PixelAlign.SnapStroke(double.NaN, 2d)));
    }
}
