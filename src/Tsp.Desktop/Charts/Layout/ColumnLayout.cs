using Avalonia;

namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 竖直<b>柱 / 实体</b>这一类填充图形的排布规则：横向槽位、以及"从某个锚点向外取整"的纵向跨度。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独成篇。</b>这三条规则原本长在 <c>BarChart.LayoutBar</c> 里；K 线图（Task 10b）的
/// 成交量柱与 K 线实体要的是一模一样的三件事，于是把它搬到这里。搬而不是让 K 线去调
/// <c>BarChart.LayoutBar</c>：那是一个控件引用另一个控件，正是 <see cref="ChartFrame"/> 的文档里
/// 写明要避开的那种耦合（"后来者只能二选一——复制一份，或者让柱状图去调 LineChart.ComputeWindow"）。
/// <c>BarChart.LayoutBar</c> 现在只是这里的委托，签名与行为一字不变——它的既有用例
/// （<c>BarLayoutTests</c>）原样通过，就是这次搬家没有漂移的证明。
/// </para>
/// <para>
/// <b>与 <see cref="ChartFrame"/> 的分工。</b>那边算的是"值轴窗口 + 网格行号"（折线 / 柱 / K 线共用的
/// <b>框</b>），这边算的是"一根柱落在哪"（柱 / 实体共用的<b>填充</b>规则）。两者都会用到
/// <see cref="PixelAlign"/>，但用的是它不同的两面：网格线走<b>笔画中线</b>的半像素偏移，
/// 填充走<b>边落在整像素上</b>——见 <see cref="SnapSpan"/> 与 <see cref="Slot"/> 的文档。
/// </para>
/// <para>
/// <b>纯静态纯函数</b>：入参出参只有 <see cref="double"/> 与 Avalonia 的值结构 <see cref="Rect"/>
/// （后者与 <see cref="ChartFrame"/> 同一个理由——像素几何本来就以矩形形态存在于渲染路径上），
/// 不读控件状态、不碰绘制栈，在没有 UI 线程的测试里可以直接调用。
/// </para>
/// </remarks>
public static class ColumnLayout
{
    /// <summary>
    /// 一根柱（或一根 K 线实体）在横向占的那一段：<b>槽位按根数均分</b>，两端各自取整到最近像素。
    /// </summary>
    /// <param name="index">第几根，从 <c>0</c> 起。必须落在 <c>[0, count - 1]</c> 内（越界会画到槽外去，那是调用方的事）。</param>
    /// <param name="count">一共几根。<b>非正</b>时返回一条零宽的槽（不抛异常、不做除法）。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。宽度非正或非有限时同样返回零宽的槽。</param>
    /// <param name="gapRatio">柱在槽里让出的空白比例；非有限值按 0 处理，其余夹到 <c>[0, 1]</c>。</param>
    /// <returns>
    /// 这条槽的左右边界，<b>都落在整像素上</b>，且左右次序不会颠倒（宽度可以为 0，不会为负）。
    /// <paramref name="count"/> 非正或绘图区宽度不可用时，两个值都等于 <paramref name="plotArea"/> 的左边。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>两条边各自独立取整</b>（不是"先取整左边界再加宽度"）。相邻两根柱相邻的那条边于是由
    /// 同一个 <see cref="Math.Round(double)"/> 得到同一个整数，<b>"不重叠"是结构上的必然</b>，
    /// 而不是靠留白算得巧；横向不越框也来自同一件事（槽的左右端点本来就落在框边上，
    /// 取整不会把它推出去）。
    /// </para>
    /// <para>
    /// <b>代价：槽宽不足 1 像素时某些柱会退化成零宽而看不见。</b>宁可不画，也不画一根压住邻居的柱
    /// ——重叠的柱会被读成一根，那是"少画"与"画错"的区别。
    /// </para>
    /// <para>
    /// <b>间距夹到 <c>[0, 1]</c>、非有限按 0.</b>负间距会让柱宽超过槽宽（相邻柱直接叠在一起），
    /// 大于 1 的间距给出负宽度的矩形；两者都是样式/页面能随手填进来的值。
    /// 这与基座给 <c>PlotPadding</c> 的处置同一口径。
    /// </para>
    /// </remarks>
    public static (double Left, double Right) Slot(int index, int count, Rect plotArea, double gapRatio)
    {
        if (count <= 0 || !double.IsFinite(plotArea.Width) || plotArea.Width <= 0d)
        {
            return (plotArea.X, plotArea.X);
        }

        double gap = double.IsFinite(gapRatio) ? Math.Clamp(gapRatio, 0d, 1d) : 0d;

        double slot = plotArea.Width / count;
        double barWidth = slot * (1d - gap);
        double leftEdge = plotArea.X + (index * slot) + ((slot - barWidth) / 2d);

        return (Math.Round(leftEdge), Math.Round(leftEdge + barWidth));
    }

    /// <summary>
    /// 一根柱的矩形与它量起的<b>基线</b>：横向取 <see cref="Slot"/>，纵向从基线向外取整。
    /// </summary>
    /// <param name="index">第几根，从 <c>0</c> 起。</param>
    /// <param name="count">一共几根。<b>非正</b>时返回一根零尺寸的柱（不抛异常、不做除法）。</param>
    /// <param name="value">这根柱的数据值。可以是非有限值，含义是"这一根没有值"。</param>
    /// <param name="range">纵轴的值域，<b>含 0 时基线才落在框里</b>（见备注）。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。宽度为 0 时所有柱退化成零宽，本函数照算不误。</param>
    /// <param name="gapRatio">柱在槽里让出的空白比例，见 <see cref="Slot"/>。</param>
    /// <returns>
    /// 柱的矩形（横向已对齐到整像素、纵向已从基线向外取整）与基线的行号（已对齐到整像素）。
    /// <b><paramref name="value"/> 非有限时返回的矩形 <c>Y</c> 与 <c>Height</c> 都是非有限值</b>
    /// （与 <see cref="ChartHitTest.YFor"/> 原样透传非有限的纪律一致）——调用方据此跳过，
    /// 判据是 <c>!double.IsFinite(bar.Y)</c>。<b>任何输入都不抛异常。</b>
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>基线取"值 <c>0</c> 在值域里的位置"，夹进绘图区</b>：值域含 0 时它就在框里；
    /// 0 落在值域之外时夹到<b>靠近 0 的那条边</b>——<see cref="ChartHitTest.YFor"/> 把值域上界映到顶边、
    /// 下界映到底边，所以 0 大于上界时夹到顶边、小于下界时夹到底边，正是"取靠近 0 的边界"。
    /// 夹紧之后柱照样从那条边量起，而不是整块图什么都画不出来。
    /// （上下界用 <see cref="Math.Min(double, double)"/> / <see cref="Math.Max(double, double)"/> 排序而不是
    /// 直接 <c>Math.Clamp(值, Top, Bottom)</c>：后者在 <c>min &gt; max</c> 时<b>抛异常</b>，
    /// 而高为负的矩形（坏掉的绘图区几何）就会走到那一步。）
    /// </para>
    /// <para>
    /// <b>基线取整到整像素</b>，与柱的纵向端点同一格网格：基线若不是整数，柱贴基线的那一端就会
    /// 骑在两个设备像素之间、糊出一条边——而那条边是每根柱都要读的地方。画<b>基线本身</b>那条线
    /// 的笔画再补半格（<see cref="PixelAlign.SnapStroke"/>），两者是"填充对齐"与"笔画对齐"的区别。
    /// </para>
    /// </remarks>
    public static (Rect Column, double Baseline) Anchored(
        int index,
        int count,
        double value,
        ValueRange range,
        Rect plotArea,
        double gapRatio)
    {
        double baseline = BaselineY(range, plotArea);

        if (count <= 0 || !double.IsFinite(plotArea.Width) || plotArea.Width <= 0d)
        {
            return (new Rect(plotArea.X, baseline, 0d, 0d), baseline);
        }

        (double left, double right) = Slot(index, count, plotArea, gapRatio);

        double outer = ChartHitTest.YFor(value, range, plotArea.Y, plotArea.Height);
        (double top, double bottom) = SnapSpan(baseline, outer);

        return (new Rect(left, top, right - left, bottom - top), baseline);
    }

    /// <summary>
    /// 把两个"映射出来的"纵坐标铺成一段<b>边落在整像素上</b>的跨度：低的一端向下取整、
    /// 高的一端向上取整。
    /// </summary>
    /// <param name="firstY">一个纵坐标（屏幕 y，向下增大）。</param>
    /// <param name="secondY">另一个纵坐标。</param>
    /// <returns>
    /// <c>(Top, Bottom)</c>，两者都是整数且 <c>Top &lt;= Bottom</c>；
    /// <b>任一入参非有限时两端都是 <c>NaN</c></b>——见备注。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>向外取整，而不是就近取整。</b>一举两得：跨度恒为整数像素（填充锐利），且<b>任何非零的跨度
    /// 至少占 1 像素</b>——一根量级只有半个像素的柱、一根开盘等于收盘的十字星，若按就近取整就会
    /// 塌成零高、凭空消失，而"很短"与"没有"在读图上完全是两件事。跨度为 0（两个 y 相等且都是整数）
    /// 时高恒为 0，那是实话，不受这条规则影响。
    /// </para>
    /// <para>
    /// <b>前提：至少要有一个入参已经落在整像素上</b>（柱的基线就是，它由调用方先取整）。
    /// 此时"向外取整"与"把落在整像素上的那一端原地不动、另一端向外"完全一致，于是
    /// <see cref="Anchored"/> 与旧实现逐位相同。两个入参都不在整像素上时，这里会把两端各推出
    /// 最多一个像素——调用方要的是"这一段落在哪几行像素上"，那就是它给出的答案。
    /// </para>
    /// <para>
    /// <b>纵向因此可能越出绘图区不到 1 像素</b>（边缘不是整数时）：这是"填充锐利"与"严格不越框"
    /// 之间的取舍，取前者——越出去的那点通常落在基座的内边距里，而糊掉的边是看得见的。
    /// </para>
    /// <para>
    /// <b>非有限入参给的是 <c>(NaN, NaN)</c>，而不是"原样透传"。</b>直接做取整的话，
    /// <c>SnapSpan(25, +Infinity)</c> 会得到 <c>(25, +Infinity)</c>——上端是有限的，
    /// 于是"判 Top / 判 Y 就能认出这一段没有落点"这条<b>到处都在用的判据</b>在这里失效
    /// （调用方只看 Y 的话，会拿一个 <c>Y = 25</c> 的矩形去画一段高度为 <c>Infinity</c> 的填充）。
    /// 两端一起给 <c>NaN</c>，让判据在任何非有限入参下都成立。
    /// </para>
    /// </remarks>
    public static (double Top, double Bottom) SnapSpan(double firstY, double secondY)
    {
        if (!double.IsFinite(firstY) || !double.IsFinite(secondY))
        {
            return (double.NaN, double.NaN);
        }

        double low = Math.Min(firstY, secondY);
        double high = Math.Max(firstY, secondY);

        return (Math.Floor(low), Math.Ceiling(high));
    }

    /// <summary>
    /// 基线的行号：值 <c>0</c> 在 <paramref name="range"/> 里的位置，夹进绘图区、对齐到整像素。
    /// </summary>
    /// <param name="range">纵轴值域。含 0 时基线落在框里，不含时夹到靠近 0 的那条边。</param>
    /// <param name="plotArea">绘图矩形。</param>
    /// <returns>基线所在的行号，<b>整数</b>（柱与实体都要把一端对齐到它）。</returns>
    /// <remarks>
    /// <para>
    /// 调用方拿它画零线那条<b>笔画</b>时还要补半格（<see cref="PixelAlign.SnapStroke"/>）：
    /// 这里给的是填充要对齐的整数行，笔画的中线落在 <c>整数 + 0.5</c> 上才铺满一列设备像素。
    /// 两者是同一处几何的两种对齐口径，见 <see cref="SnapSpan"/>。
    /// </para>
    /// <para>
    /// 上下界用 <see cref="Math.Min(double, double)"/> / <see cref="Math.Max(double, double)"/> 排序而不是
    /// 直接 <c>Math.Clamp(值, Top, Bottom)</c>：后者在 <c>min &gt; max</c> 时<b>抛异常</b>，
    /// 而高为负的矩形（坏掉的绘图区几何）就会走到那一步。本函数跑在 <c>Render()</c> 路径上，
    /// 那里一个异常就是一整窗白屏。
    /// </para>
    /// </remarks>
    public static double BaselineY(ValueRange range, Rect plotArea)
    {
        double low = Math.Min(plotArea.Top, plotArea.Bottom);
        double high = Math.Max(plotArea.Top, plotArea.Bottom);
        double y = ChartHitTest.YFor(0d, range, plotArea.Y, plotArea.Height);

        return Math.Round(Math.Clamp(y, low, high));
    }
}
