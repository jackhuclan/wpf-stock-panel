namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 像素对齐：把笔画的中线坐标推到设备像素网格上，让轴线、网格线、柱边画出来是<b>锐</b>的而不是糊的。
/// </summary>
/// <remarks>
/// <para>
/// 纯静态纯函数，<b>不引用任何 Avalonia 类型</b>：入参出参都是 <see cref="double"/>，
/// 可以在没有 UI 线程的单元测试里直接跑。六个图表控件的每一条轴线、网格线与柱边都要过这里，
/// 所以它是「图表看起来清不清爽」这件事的单点。
/// </para>
/// <para>
/// <b>为什么要对齐。</b>一条 1px 宽的线以整数坐标为中线时，笔画的覆盖区间是
/// <c>[c - 0.5, c + 0.5]</c>——它的中线正好压在相邻两列设备像素的<b>交界</b>上，
/// 于是每一列各分到半条线，抗锯齿把两列都涂成 50% 灰：看上去是一条 2px 的糊边。
/// 把中线挪到 <c>c + 0.5</c>，覆盖区间变成 <c>[c, c + 1]</c>，正好与一列设备像素重合，线就是实心的一列。
/// </para>
/// <para>
/// <b>奇偶规则（不要"简化"掉那个 + 0.5：它不是笔误，而是整条规则的由来）。</b>
/// 笔画以中线为中心对称展开，覆盖 <c>[c - t/2, c + t/2]</c>：
/// </para>
/// <list type="bullet">
/// <item><description><b>奇数宽度</b>（1、3、5…）：<c>t/2</c> 是半整数，覆盖区间的两端都落在像素交界上，
/// 只有当 <c>c</c> 自身处在半像素偏移上时，两端才同时落到<b>整数</b>坐标上、笔画才铺满整数列设备像素。
/// 所以奇数宽度要 <c>+0.5</c>。</description></item>
/// <item><description><b>偶数宽度</b>（2、4…）：<c>t/2</c> 是整数，覆盖区间的两端本来就是整数，
/// 笔画已经对称地铺满整数列设备像素，坐标原地不动；再加半格反而把它推到交界上、重新糊掉。</description></item>
/// </list>
/// <para>
/// <b>退化入参一律原样返回，不抛异常</b>：厚度非有限或非正数时根本没有笔画可对齐，
/// 此时推半个像素等于把一段偏移硬塞进一段本就没有笔画的几何里；非整数厚度则没有可判定的奇偶，
/// 也原样返回——本函数只实现「整数宽度下的奇偶规则」，不现场发明一套取整规则。
/// 本函数跑在 <c>Render()</c> 里，一个异常就是一整窗白屏。
/// </para>
/// </remarks>
public static class PixelAlign
{
    /// <summary>半个设备像素：奇数宽笔画的中线必须落在「整数 + 0.5」上。</summary>
    private const double HalfPixel = 0.5d;

    /// <summary>
    /// 把一条整数宽笔画的中线坐标对齐到设备像素网格上。
    /// </summary>
    /// <param name="coordinate">
    /// 笔画中线的坐标（控件局部坐标系的 X 或 Y）。非有限值原样透传，不做修正、不抛异常。
    /// </param>
    /// <param name="thickness">
    /// 笔画宽度（设备像素）。<b>只有正整数</b>才有对齐规则：奇数宽度返回 <c>coordinate + 0.5</c>，
    /// 偶数宽度返回 <paramref name="coordinate"/> 本身；<c>0</c>、负数、<c>NaN</c> / <c>±Infinity</c>、
    /// 以及一切非整数宽度都<b>原样返回</b>。
    /// </param>
    /// <returns>对齐后的笔画中线坐标。</returns>
    public static double SnapStroke(double coordinate, double thickness)
    {
        if (!double.IsFinite(thickness) || thickness <= 0d)
        {
            // 没有笔画，就没有要对齐的东西：原样返回。
            return coordinate;
        }

        double integral = Math.Floor(thickness);
        if (integral != thickness)
        {
            // 非整数宽度没有可判定的奇偶，不发明取整规则。
            return coordinate;
        }

        return IsOdd(integral) ? coordinate + HalfPixel : coordinate;
    }

    /// <summary>一个**整数**宽度是不是奇数（奇数的笔画中心要偏半个像素）。</summary>
    private static bool IsOdd(double integralThickness) => integralThickness % 2d != 0d;
}
