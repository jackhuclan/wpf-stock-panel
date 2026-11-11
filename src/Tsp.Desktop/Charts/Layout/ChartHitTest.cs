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
/// 因此<b>都不抛异常</b>：坏的输入换回 <c>null</c> 或一个确定的位置。
/// </para>
/// <para>
/// 但「不返回毒值」<b>只对几何 / 值域这一侧成立</b>，<b>不覆盖数据值本身</b>：
/// 画不出来的窗口、退化的值域、越界的指针，换回的确实是 <c>null</c> 或一个确定的位置；
/// 而 <see cref="YFor"/> 对非有限的 <c>value</c>（指标预热期的 <c>NaN</c>、坏点带来的 <c>±Infinity</c>）
/// <b>原样透传</b>非有限结果。这是刻意的：那是「这个点没有值」的信号，
/// 若在这里悄悄折成绘图区正中，绘图方会画出一条垂到中间的假线——比缺点更难查。
/// <b>跳过没值的点是调用方的义务</b>，判定方式见 <see cref="YFor"/> 的 <c>&lt;returns&gt;</c>。
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
    /// <c>x == plotLeft + plotWidth</c> 得到 <c>count - 1</c>。这份精确性来自<b>取整到最近邻</b>：
    /// 端点的 <c>t</c> 就是 0 与 1，<c>t × (count - 1)</c> 恰好落在两个整数上，取整不会把它挪走。
    /// 后面那次夹紧<b>不是</b>端点精确的成因——夹紧要真的生效，得让 <c>t</c> 偏出
    /// <c>0.5 / (count - 1)</c> 以上，也就是 <c>count</c> 要到 4.5e15 量级，现实中够不着；
    /// 它是给病态入参兜底的保险，不是机制。别把因果讲反了。
    /// 无论机制是哪一条，右边缘错一格就是悬停高亮到旁边那根 K 线，所以这条契约必须钉死。
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

        // 取整到最近邻：端点精确（t = 0 / 1 分别得 0 与 count - 1）与中间点的吸附都靠它，
        // 换成截断（Floor）会让每个点整体偏左半格。
        int index = (int)Math.Round(t * (count - 1), MidpointRounding.AwayFromZero);

        // 保险，不是机制：端点精确靠的是上面那次取整；夹紧要生效得让 t 偏出 0.5 / (count - 1) 以上
        // （count 到 4.5e15 量级），现实中够不着。留着是为了「下标一定落在 [0, count - 1] 内」
        // 在任何病态入参下都成立，而不是靠浮点运气。
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
    /// <paramref name="range"/> 退化或不可用时返回绘图区<b>垂直正中</b>。
    /// <b><paramref name="value"/> 非有限时原样透传非有限结果</b>（理由见备注）。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>调用方义务：把返回值送进几何之前，必须先用 <c>!double.IsFinite(y)</c> 判一次，非有限就跳过该点。</b>
    /// 判定要用<b>非有限判定</b>，不要用「是否越出绘图区」的区间比较：
    /// <c>±Infinity</c> 虽然确实越界，但 <c>NaN</c> 与任何数比较都是 <c>false</c>，
    /// 靠区间比较<b>拦不住它</b>——它会一路潜进几何，把同一帧里所有坐标一起染成 <c>NaN</c>。
    /// </para>
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
    /// <c>new ValueRange(0, double.PositiveInfinity)</c>，或上下界各自有限、跨度却溢出成
    /// <c>+Infinity</c> 的 <c>(-1e308, 1e308)</c>）同样无从映射，一并走这条退路。
    /// </para>
    /// <para>
    /// <b>非有限的 <paramref name="value"/> 原样透传</b>（<c>NaN</c> / <c>±Infinity</c> 进，非有限出），
    /// 不折成绘图区正中。指标序列在预热期本来就成段是 <c>NaN</c>，那是「这个点没有值」的信号：
    /// 折成正中会把「这里缺个点」画成「一条垂到中间的假线」，读者看到的是错误的价格走势；
    /// 而透传出去，调用方按上面的义务用 <c>!double.IsFinite(y)</c> 一跳就干净了。
    /// 这是本文件对「不返回毒值」的唯一例外，也是它必须写成明文义务的原因。
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
