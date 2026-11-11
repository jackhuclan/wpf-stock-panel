namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 图表几何的两条主干：把像素位置映射回数据下标（悬停命中），把数据值映射到屏幕纵坐标（绘制落点）。
/// </summary>
/// <remarks>
/// <para>
/// 纯静态纯函数，<b>不引用任何 Avalonia 类型</b>：入参出参都是 <see cref="double"/> 与 <see cref="ValueRange"/>，
/// 可以在没有 UI 线程的单元测试里直接跑。Avalonia 的 <c>Point</c> / <c>Rect</c> 到 Task 4 的
/// <c>ChartSurface</c> 才出现，所以这里也<b>不提供</b>收 / 发 <c>Point</c> 的便捷重载——
/// 一个只在渲染层好用的重载会把 UI 依赖反向拖进这个可测层。
/// </para>
/// <para>
/// 与 <see cref="AxisTicks"/> 的分工：那边决定<b>画哪些刻度</b>，本文件决定<b>某一点落在哪个下标 / 哪一行像素</b>。
/// 两者假定的是同一个可画窗口：调用方拿 <see cref="AxisTicks.Nice"/> 的首尾刻度定出窗口，
/// 再把窗口的左 / 宽 / 顶 / 高喂给这里。
/// </para>
/// <para>
/// 两个方法都跑在热路径上——<see cref="IndexAtX"/> 每次指针移动一次，<see cref="YFor"/> 每帧每个点一次——
/// 因此<b>都不抛异常、都不返回毒值</b>：坏的输入换回 <c>null</c> 或一个确定的位置。
/// </para>
/// </remarks>
public static class ChartHitTest
{
    /// <summary>
    /// 命中测试：给定画布横坐标，返回它<b>最接近</b>的数据下标。
    /// </summary>
    /// <param name="x">画布横坐标（像素，通常来自指针事件）。</param>
    /// <param name="plotLeft">绘图区左边缘的横坐标。</param>
    /// <param name="plotWidth">绘图区宽度，必须为正。</param>
    /// <param name="count">数据点个数。</param>
    /// <returns>
    /// <c>[0, count - 1]</c> 内的下标；<b>窗口画不出来或指针不在窗口里</b>时返回 <c>null</c>。
    /// 具体是这几种：<paramref name="count"/> 非正、<paramref name="plotWidth"/> 非正或非有限、
    /// 以及 <paramref name="x"/> 落在 <c>[plotLeft, plotLeft + plotWidth]</c> 之外（含 <c>NaN</c>）。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>两端是精确的、闭区间的</b>：<c>x == plotLeft</c> 得到 <c>0</c>，
    /// <c>x == plotLeft + plotWidth</c> 得到 <c>count - 1</c>。这不是「四舍五入碰巧对上」，
    /// 而是靠取整后再夹紧保证的——右边缘错一格，悬停高亮的就是旁边那根 K 线。
    /// </para>
    /// <para>
    /// 中间的点按<b>最近邻</b>吸附，正好落在两点的正中间时取靠后的那个下标（<see cref="MidpointRounding.AwayFromZero"/>），
    /// 免得默认的「银行家舍入」让间隔在中点处左右跳变、悬停手感忽左忽右。
    /// </para>
    /// <para>
    /// <b>越界返回 <c>null</c> 而不是抛异常</b>：这个方法每次指针移动都要跑一遍，
    /// 鼠标滑出绘图区、滑到窗口外、或数据还没到（<paramref name="count"/> 为 0）都是常态，
    /// 那里抛出去就是「一动鼠标就崩」。返回 <c>null</c> 也<b>不会</b>顺手夹到首尾下标去——
    /// 没命中就是没命中，画一条贴着边缘的高亮线是在撒谎。
    /// </para>
    /// </remarks>
    public static int? IndexAtX(double x, double plotLeft, double plotWidth, int count)
    {
        if (count <= 0)
        {
            // 空数据（或调用方笔误传了负数）：既没有可命中的下标，也不能让下面的
            // count - 1 变成 -1 去参与映射。
            return null;
        }

        if (!double.IsFinite(plotLeft) || !double.IsFinite(plotWidth) || plotWidth <= 0d)
        {
            // 宽度为零 / 为负 / 非有限，或者左边缘本身就是 NaN：窗口不成立，任何映射都是假的。
            // 显式排掉非有限值是因为 NaN 与任何数比较都为 false，它会顺着下面的区间判断悄悄漏过去。
            return null;
        }

        double right = plotLeft + plotWidth;

        if (!double.IsFinite(x) || x < plotLeft || x > right)
        {
            return null;
        }

        // 归一到 [0, 1]：0 是左端（第 0 个点），1 是右端（第 count - 1 个点）。
        double t = (x - plotLeft) / plotWidth;

        // t = 1 时 t × (count - 1) 就是 count - 1 本身，中间的点按最近邻吸附。
        int index = (int)Math.Round(t * (count - 1), MidpointRounding.AwayFromZero);

        // 浮点误差可能让端点落在 [0, count - 1] 外一丁点（例如右端点算出的 t 略大于 1），
        // 夹紧一次，把「右边缘必须精确等于 count - 1」变成结构上的保证而不是运气。
        return Math.Clamp(index, 0, count - 1);
    }

    /// <summary>
    /// 把数据值映射到屏幕纵坐标。屏幕 y <b>向下</b>增长、值轴<b>向上</b>增长，所以这里要翻一次。
    /// </summary>
    /// <param name="value">要落点的数据值。</param>
    /// <param name="range">绘图区对应的值域。</param>
    /// <param name="plotTop">绘图区上边缘的纵坐标。</param>
    /// <param name="plotHeight">绘图区高度。</param>
    /// <returns>
    /// 屏幕纵坐标：<paramref name="range"/> 的上界落在 <paramref name="plotTop"/>（顶边），
    /// 下界落在 <paramref name="plotTop"/> + <paramref name="plotHeight"/>（底边）。
    /// 值域退化或不可用时返回绘图区<b>垂直正中</b>。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 值域假定是<b>有序</b>的（<c>Min &lt;= Max</c>）——<see cref="ValueRange.From"/> 的产出总是如此；
    /// 上下界写反的值域会被当成一条反向的轴来映射。
    /// </para>
    /// <para>
    /// <b>不夹紧</b>：值域之外的值会算出绘图区之外的 y（尖峰高出窗口时正是要它如实跑到框外），
    /// 画不画、裁不裁由调用方决定，这里不替它把点悄悄拽回边缘。
    /// </para>
    /// <para>
    /// <b>零跨度绝不参与除法。</b> 退化值域（停牌、单点、全等值）会让 <c>Max - Min</c> 等于 0，
    /// 照着公式算就是 <c>0/0 = NaN</c>，而 NaN 一旦进了几何，一帧里所有坐标会一起变成 NaN——
    /// 整块图什么都画不出来，且不抛异常、不留痕迹，最难查。这里改成返回绘图区的垂直正中：
    /// 平坦的线画在中间，是「零跨度」唯一说得通的位置。非有限的跨度（例如
    /// <c>new ValueRange(0, double.PositiveInfinity)</c>）同样无从映射，一并走这条退路。
    /// </para>
    /// </remarks>
    public static double YFor(double value, ValueRange range, double plotTop, double plotHeight)
    {
        double span = range.Max - range.Min;

        if (range.IsDegenerate || !double.IsFinite(span))
        {
            return plotTop + (plotHeight / 2d);
        }

        double t = (value - range.Min) / span;

        // 1 - t：值越大 t 越大，y 就要越小，所以翻过来再折算到 [plotTop, plotTop + plotHeight]。
        return plotTop + ((1d - t) * plotHeight);
    }
}
