namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 一条数据系列的<b>值域</b>（最小值与最大值），是自绘图表把数值映射到像素前的第一步。
/// </summary>
/// <remarks>
/// <para>
/// 纯值类型，<b>不引用任何 Avalonia 类型</b>：入参出参都是 <see cref="double"/>，
/// 因此可以在没有 UI 线程的单元测试里直接构造与断言。
/// </para>
/// <para>
/// <see cref="From"/> <b>不抛异常、不返回 NaN</b>：它跑在 <c>Render()</c> 里，
/// 数据可能是空的、只有一个点的、全等的，或混着非有限值，而那里的一个异常就是一整窗白屏。
/// 拿不到数据时返回的是「没有跨度的可用窗口」，不是毒值。
/// </para>
/// </remarks>
/// <param name="Min">下界。</param>
/// <param name="Max">上界。</param>
public readonly record struct ValueRange(double Min, double Max)
{
    /// <summary>取不到数据时的兜底窗口；总是有限且非退化。</summary>
    private static readonly ValueRange _emptyWindow = new(0d, 1d);

    /// <summary>
    /// 退化值域：<see cref="Min"/> 与 <see cref="Max"/> 完全相等，也就是数据没有任何跨度。
    /// </summary>
    /// <remarks>
    /// 调用方据此决定「把线画在正中 / 取中性色」，而不是拿一个零跨度去做除法。
    /// 判定是<b>精确相等</b>、<b>不带 epsilon</b>：真正平坦的序列（停牌、单点、全等值）必须被认出来，
    /// 加一点容差就会把它们悄悄当成有跨度的序列，反而错。
    /// <b>空集不算退化</b>——<see cref="From"/> 对空集返回的是兜底窗口 <c>(0, 1)</c>，其
    /// <see cref="IsDegenerate"/> 为 <c>false</c>。
    /// </remarks>
    public bool IsDegenerate => Min == Max;

    /// <summary>
    /// 扫描 <paramref name="values"/> 求值域，<b>单遍</b>完成。
    /// </summary>
    /// <param name="values">
    /// 待求值域的数据。非有限值（<c>NaN</c> / <c>±Infinity</c>）被忽略，不参与求值域；
    /// <c>null</c> 与空序列一视同仁。
    /// </param>
    /// <returns>
    /// 有限值中的最小 / 最大者；<b>若一个有限值都没有</b>（空序列、<c>null</c>、
    /// 或整条序列全是非有限值），返回 <c>(0, 1)</c> 这个可用窗口而不是 <c>(NaN, NaN)</c>。
    /// </returns>
    public static ValueRange From(IEnumerable<double> values)
    {
        if (values is null)
        {
            return _emptyWindow;
        }

        double min = 0d;
        double max = 0d;
        bool seen = false;

        foreach (double value in values)
        {
            // 一个坏点不该毁掉整条线：丢掉它，而不是让 NaN 顺着比较运算污染整个值域。
            if (!double.IsFinite(value))
            {
                continue;
            }

            if (!seen)
            {
                min = value;
                max = value;
                seen = true;
                continue;
            }

            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        return seen ? new ValueRange(min, max) : _emptyWindow;
    }
}
