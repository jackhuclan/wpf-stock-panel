namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 坐标轴刻度：把 <c>[min, max]</c> 切成一串「整齐」的刻度值，步长取 1 / 2 / 5 × 10ⁿ。
/// </summary>
/// <remarks>
/// <para>
/// 纯静态纯函数，<b>不引用任何 Avalonia 类型</b>：入参出参都是 <see cref="double"/>，
/// 可以在没有 UI 线程的单元测试里直接跑。
/// </para>
/// <para><b>契约</b>（调用方可以依赖这四条）：</para>
/// <list type="number">
/// <item><description>返回列表<b>恒非空</b>（至少两条），严格递增，且元素全部有限；</description></item>
/// <item><description>相邻刻度之差恒为<b>正的有限值</b>；</description></item>
/// <item><description>刻度区间<b>覆盖</b> <c>[min, max]</c> —— 首尾各自向外取整到整齐值，
/// 因此 <c>ticks[0]</c> / <c>ticks[^1]</c> 可以直接当作可画窗口，数据点不会落到轴外；</description></item>
/// <item><description>任何输入都<b>不抛异常、不返回 NaN</b>：<c>NaN</c> / <c>±Infinity</c>、
/// 上下界写反、退化区间、极端到溢出的量级，全部落成可画的窗口。</description></item>
/// </list>
/// <para>
/// <b>刻度条数不保证等于 <paramref name="targetCount"/></b>。这是 nice-number 类算法的固有性质：
/// 步长只能取 1 / 2 / 5 × 10ⁿ，实际条数会在目标值附近浮动，向外取整还会两端各多一格。
/// <paramref name="targetCount"/> 是「大致想要几条」的期望，<b>不是精确条数契约</b>，
/// 后来者不要把它「修」成精确条数。它还会被收敛到 <c>[2, 1000]</c>，
/// 免得病态入参生成海量刻度把渲染拖死。
/// </para>
/// </remarks>
public static class AxisTicks
{
    /// <summary>刻度条数的下限：至少要能切出一段区间。</summary>
    private const int MinTargetCount = 2;

    /// <summary>刻度条数的上限：病态入参不至于生成海量刻度拖垮渲染。</summary>
    private const int MaxTargetCount = 1000;

    /// <summary>刻度条数的兜底上限，防浮点意外造出巨量列表。</summary>
    private const int MaxTickCount = 4096;

    /// <summary>退化区间向外撑开的比例（相对于该值自身的量级）。</summary>
    private const double DegeneratePadRatio = 0.5d;

    /// <summary>
    /// 在 <c>[min, max]</c> 上取一串整齐刻度。
    /// </summary>
    /// <param name="min">区间下界；非有限值时与 <paramref name="max"/> 一起退回 <c>(0, 1)</c>。</param>
    /// <param name="max">区间上界；写反了会自动纠正。</param>
    /// <param name="targetCount">期望的刻度条数，会被收敛到 <c>[2, 1000]</c>；实际条数可以与之不同。</param>
    /// <returns>非空、严格递增、全部有限的刻度列表，区间覆盖 <paramref name="min"/> 与 <paramref name="max"/>。</returns>
    public static IReadOnlyList<double> Nice(double min, double max, int targetCount)
    {
        double low = min;
        double high = max;

        if (!double.IsFinite(low) || !double.IsFinite(high))
        {
            // 任一端不是有限数 → 整个区间不可信，退回一个确定的可用窗口，
            // 绝不让 NaN / Infinity 渗进步长。
            low = 0d;
            high = 1d;
        }

        if (low > high)
        {
            // 上下界写反是调用方笔误，纠正即可，不必抛异常打断整帧绘制。
            (low, high) = (high, low);
        }

        if (low == high)
        {
            // 退化区间（空集 / 单点 / 全等值）没有跨度，以该值为中心撑开一段，
            // 让平坦的线仍然落在可画窗口的正中，而不是拿零跨度去做除法。
            (low, high) = ExpandDegenerate(low);
        }

        int count = Math.Clamp(targetCount, MinTargetCount, MaxTargetCount);
        double step = SelectStep(high - low, count);
        double firstIndex = Math.Floor(low / step);
        double lastIndex = Math.Ceiling(high / step);
        double tickCount = lastIndex - firstIndex + 1d;

        if (!double.IsFinite(step) || step <= 0d
            || !double.IsFinite(firstIndex * step)
            || !double.IsFinite(lastIndex * step)
            || tickCount > MaxTickCount)
        {
            // 量级极端到浮点求不出整齐步长：例如 [1e307, 1.7e308] 会让整齐步长 2 × 10³⁰⁸ 溢出，
            // [-1e308, 1e308] 的区间本身就溢出成 +Infinity。
            // 退回「正好两端」的窗口，硬契约（非空、递增、有限、覆盖）依然成立。
            return new[] { low, high };
        }

        // 用「首项 + 序号 × 步长」而不是累加，避免浮点误差随刻度条数累积。
        int total = (int)tickCount;
        long first = (long)firstIndex;
        var ticks = new List<double>(total);
        for (int i = 0; i < total; i++)
        {
            ticks.Add((first + i) * step);
        }

        return ticks;
    }

    /// <summary>
    /// 把一个没有跨度的值撑成一段以它为中心的窗口。
    /// </summary>
    private static (double Low, double High) ExpandDegenerate(double value)
    {
        double pad = value == 0d ? 1d : Math.Abs(value) * DegeneratePadRatio;
        double low = value - pad;
        double high = value + pad;

        if (double.IsFinite(low) && double.IsFinite(high) && low < high)
        {
            return (low, high);
        }

        // 量级大到一撑开就溢出（|value| 逼近 double.MaxValue）：退回固定窗口。
        return (0d, 1d);
    }

    /// <summary>
    /// 经典 nice number：把 <c>区间 / (条数 - 1)</c> 归一化后吸附到 1 / 2 / 5 / 10，
    /// 再乘回量级，得到 1 / 2 / 5 × 10ⁿ 形式的步长。
    /// </summary>
    private static double SelectStep(double range, int count)
    {
        double raw = range / (count - 1);
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;

        double niceness = normalized switch
        {
            < 1.5d => 1d,
            < 3d => 2d,
            < 7d => 5d,
            _ => 10d,
        };

        return niceness * magnitude;
    }
}
