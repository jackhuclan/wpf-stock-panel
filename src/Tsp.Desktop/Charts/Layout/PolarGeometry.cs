namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 极坐标到屏幕坐标的那一步换算：给定角度与半径，算出圆上那一点的坐标。
/// </summary>
/// <remarks>
/// <para>
/// 纯静态纯函数，<b>不引用任何 Avalonia 类型</b>：入参出参全是 <see cref="double"/>，
/// 可以在没有 UI 线程的单元测试里直接跑。返回的是一对 <see cref="double"/> 而不是
/// <c>Avalonia.Point</c>，理由与本目录其余文件一致——那个层次的坐标只是两个数，
/// 包成值结构只会让这一层多一个可以不要的依赖（本目录里唯一收 Avalonia 值结构的是
/// <see cref="ChartFrame"/>，它收 <c>Rect</c> 是因为"四个数来自同一个矩形"是一条真不变量）。
/// </para>
/// <para>
/// <b>它服务的是"环形 / 雷达"这一类图</b>：环形图（<c>DonutChart</c>）要靠它把分片的起止角
/// 落成圆弧端点与内圈端点，雷达图（<c>RadarChart</c>）要靠它把每个维度的顶点放到外圈上。
/// 这两个控件各写一份三角函数是同一个错误的两次机会：忘掉 y 轴向下（少一个负号）、
/// 把 12 点方向写成 3 点方向、分量接反——三种错法都画得出图，只是整块图形整体转了 90°
/// 或整体镜像，光看代码很难发现，光看图也未必立刻反应过来。放在这里，它就只有一份，
/// 而且有直接的用例钉住四个象限。
/// </para>
/// <para>
/// <b>约定（本仓库图表的统一角度口径，<c>DonutChart</c> 的 <c>ComputeSlices</c> 也用它）</b>：
/// 角度以<b>弧度</b>计，<c>0</c> 是<b>12 点方向</b>，<b>顺时针为正</b>——与屏幕坐标
/// （y 向下）一致，也与读环图、雷达图的习惯一致。
/// </para>
/// <para>
/// <b>退化与非有限入参一律原样传出去，不抛异常</b>：非有限的角度算出非有限坐标
/// （与 <see cref="ChartHitTest.YFor"/> 对非有限值的口径一致，由调用方判掉），
/// 半径为 <c>0</c> 时所有点都落在圆心上。它跑在 <c>Render()</c> 路径上，
/// 那里一个异常就是一整窗白屏——所以这里只做换算，不做校验、不做政策决定。
/// </para>
/// </remarks>
public static class PolarGeometry
{
    /// <summary>
    /// 圆上一点：角度从 12 点方向起算、顺时针为正。
    /// </summary>
    /// <param name="angle">弧度角，<c>0</c> 是 12 点方向，顺时针增大。非有限值原样透传。</param>
    /// <param name="centreX">圆心横坐标。非有限值原样透传。</param>
    /// <param name="centreY">圆心纵坐标。非有限值原样透传。</param>
    /// <param name="radius">半径，按原样使用（负值会把点映到对侧去，调用方负责给正半径）。</param>
    /// <returns>该角度在圆上的点。<b>不作像素对齐</b>：它落在哪就是数据算出来的哪。</returns>
    public static (double X, double Y) OnCircle(double angle, double centreX, double centreY, double radius) =>
        (centreX + (radius * Math.Sin(angle)), centreY - (radius * Math.Cos(angle)));
}
