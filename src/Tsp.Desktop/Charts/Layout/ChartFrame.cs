using Avalonia;

namespace Tsp.Desktop.Charts.Layout;

/// <summary>
/// 图表的<b>框</b>：纵轴的可画窗口、定义这个窗口的刻度值，以及网格线的行号。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不放在某个控件上。</b>它是"数据 → 可画窗口"与"刻度 → 网格行号"这两段纯算术，
/// 不读任何控件状态；而消费者不止一个：折线图（Task 6）要它，柱状图（Task 9）与 K 线图（Task 10）
/// 同样要有值轴窗口与网格线。挂在某一个控件上，后来者就只能二选一——复制一份，
/// 或者让柱状图去调 <c>LineChart.ComputeWindow</c>（一个控件引用另一个控件），
/// 后者正是本阶段一路在避开的耦合。雷达图 / 环形图 / 热力图不需要它：那三个是<b>归一化</b>
/// 而不是<b>定框</b>（各自的值域由数据本身的语义给定），所以这里只服务"用值轴框住数据"的那一类。
/// </para>
/// <para>
/// <b>与同目录其它文件的一致性，以及唯一的一处例外。</b><see cref="ValueRange"/>、<see cref="AxisTicks"/>、
/// <see cref="ChartHitTest"/>、<see cref="PixelAlign"/> 都是"纯静态纯函数、不引用任何 Avalonia 类型"，
/// 因为那个层次的入参出参全是 <see cref="double"/>。本文件是<b>唯一</b>一个收 Avalonia 值结构
/// <see cref="Rect"/> 的：<see cref="GridLineYs"/> 的输入是<b>某一帧的像素几何</b>，
/// 而它本来就以 <see cref="Rect"/> 的形态存在于渲染路径上，为了"不引用 Avalonia"把它拆成四个
/// <see cref="double"/> 只会丢掉"这四个数一定来自同一个矩形"这条不变量。<see cref="Rect"/> 是纯值结构，
/// 不带 UI 状态、不碰绘制栈、不要求 UI 线程——本文件的全部单元测试都在无 UI 线程下直接跑。
/// 若将来真有消费者需要在无 Avalonia 的环境里用它，那时再加一个"四个 double"的重载，
/// 别现在就为想象中的调用方发明它。
/// </para>
/// <para>
/// <b>像素对齐与数据映射是两件事，别混。</b>本文件里的对齐只服务网格线（呈现装饰）；
/// 数据点的坐标是数据本身，绝不许做同样的挪动——那条纪律写在
/// <see cref="GridLineYs"/> 的备注里，也写在 <c>LineChart.RenderChart</c> 里。
/// </para>
/// </remarks>
public static class ChartFrame
{
    /// <summary>
    /// 网格线宽度（逻辑像素）。整数宽度才走得出 <see cref="PixelAlign.SnapStroke"/> 的奇偶规则。
    /// </summary>
    /// <remarks>
    /// 公开是刻意的：<see cref="GridLineYs"/> 按这个宽度的奇偶推了半格，
    /// <b>画线的那支笔必须用同一个值</b>，否则推的半格与笔画的真实覆盖对不上（换成 2px 而这里仍是 1，
    /// 就等于把一条本已铺满整数行的线又推回像素交界上）。两者共用一处常量，是让这条一致性无法被说忘就忘。
    /// </remarks>
    public const double GridStrokeThickness = 1d;

    /// <summary>
    /// 由数据定出<b>可画的纵轴窗口</b>，以及定义这个窗口的刻度值。
    /// </summary>
    /// <param name="values">数据序列；非有限值不参与值域（<see cref="ValueRange.From"/> 自己滤掉）。</param>
    /// <param name="targetCount">期望的刻度条数，会被 <see cref="AxisTicks.Nice"/> 收敛；实际条数可以与之不同。</param>
    /// <returns>
    /// 窗口是<b>首尾刻度本身</b>，刻度至少两条、严格递增、全部有限，窗口有限且非退化，并覆盖全部有限数据值
    /// （唯一的例外见备注：量级逼近 <c>double.MaxValue</c> 的退化序列会退回固定窗口，那时不覆盖数据）。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>窗口与刻度要一起产出，不能各算一次。</b>网格线按刻度落行、折线按窗口落点，
    /// 两处若各算一遍，迟早会漂移（折线用数据极值、网格用整齐刻度），那时曲线贴着框边而网格不在框上，
    /// 且没有任何报错。这里把"窗口就是首尾刻度"做成一个不变量：调用方拿到的两样东西天然自洽。
    /// </para>
    /// <para>
    /// <b>任何输入都不抛异常、不返回毒值</b>：空序列、单点、全等值、全是非有限值、量级极端到撑不开，
    /// 一律由 <see cref="ValueRange.From"/> 与 <see cref="AxisTicks.Nice"/> 兜成一个可画窗口
    /// （空序列与全非有限值得到的是它们各自的兜底窗口 <c>(0, 1)</c>——一个确定的、可画的窗口，
    /// 不是毒值）。它跑在 <c>Render()</c> 路径上，那里抛异常或给出零跨度窗口都是一整窗白屏。
    /// </para>
    /// <para>
    /// <b>"覆盖数据"有一个例外，明说</b>：值全是同一个数、且量级逼近 <c>double.MaxValue</c> 时，
    /// 向外撑开的窗口会溢出成 <c>±Infinity</c>，<see cref="AxisTicks.Nice"/> 于是退回固定的
    /// <c>(0, 1)</c>——窗口仍然可画（有限、非退化），但<b>不覆盖数据</b>。这是 Task 2 已经写明的让步，
    /// 不在调用方手里；现实中到不了（价格或指标 <c>|值| ≈ 1.8e308</c> 是无意义的脏数据），
    /// 但"窗口覆盖数据"这条不变量别当成无条件成立。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不碰绘制栈，在没有 UI 线程的测试里可直接调用。
    /// </para>
    /// </remarks>
    public static (ValueRange Window, IReadOnlyList<double> Ticks) ComputeWindow(IReadOnlyList<double> values, int targetCount)
        => ComputeWindow(ValueRange.From(values), targetCount);

    /// <summary>
    /// 由<b>值域</b>（而不是数据序列）定出可画窗口与刻度，其余契约与上面那个重载完全相同。
    /// </summary>
    /// <param name="dataRange">数据值域，通常来自 <see cref="ValueRange.From"/>；也可以是**调整过**的值域。</param>
    /// <param name="targetCount">期望的刻度条数，会被 <see cref="AxisTicks.Nice"/> 收敛。</param>
    /// <returns>窗口是首尾刻度本身，契约同 <see cref="ComputeWindow(IReadOnlyList{double}, int)"/>。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么需要它。</b>柱状图的窗口不能直接用数据的值域：柱长只有相对一条<b>看得见的零线</b>
    /// 才读得出来，所以它必须先把 <c>0</c> 并进值域（<c>BarChart.ZeroAnchored</c>），
    /// 而那一刻手上只有一条 <see cref="ValueRange"/>，没有"数据序列"可传。
    /// 用这个重载而不是在这里复制「窗口 = 首尾刻度」那句，是因为那句<b>就是</b>这条不变量的全部内容
    /// ——写两遍迟早会漂移，而漂移的表现是网格线与数据各说各话、且没有任何报错。
    /// </para>
    /// <para>
    /// <b>本重载不做任何"要不要含 0"的政策决定</b>：给什么值域就按什么值域定窗口。
    /// 那个政策属于柱状图（<c>BarChart.ZeroAnchored</c>），不属于"框"的算术。
    /// </para>
    /// </remarks>
    public static (ValueRange Window, IReadOnlyList<double> Ticks) ComputeWindow(ValueRange dataRange, int targetCount)
    {
        IReadOnlyList<double> ticks = AxisTicks.Nice(dataRange.Min, dataRange.Max, targetCount);

        return (new ValueRange(ticks[0], ticks[^1]), ticks);
    }

    /// <summary>
    /// 把刻度值换算成网格线的<b>行号</b>（屏幕 y），并做像素对齐。
    /// </summary>
    /// <param name="ticks">刻度值，来自 <see cref="ComputeWindow"/>。</param>
    /// <param name="window">与数据映射共用的值域，<b>必须是同一份</b>（<see cref="ComputeWindow"/> 给的那个）。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。</param>
    /// <returns>
    /// 每个刻度一行，顺序与 <paramref name="ticks"/> 一致（刻度递增 ⇒ 行号递减，屏幕 y 向下）；
    /// 行号是<b>整数 + 0.5</b>（<see cref="GridStrokeThickness"/> = 1px 时），
    /// 即在 100% 缩放下正好铺满一列设备像素的位置；
    /// 映射不出有限行号的刻度<b>被跳过</b>（于是条数可以少于 <paramref name="ticks"/> 的长度）。不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>为什么行号也算"能推进纯函数的算术"。</b>它是"刻度值 → 屏幕坐标 → 像素对齐"三件纯算术的复合，
    /// 原本长在渲染路径里，只能用眼睛看；搬到这里之后，首尾两行是否压在绘图区边缘、
    /// 每行是否真落在"整数 + 0.5"上（1px 笔画），都能在测试里断言。
    /// </para>
    /// <para>
    /// <b>对齐要两步：先取整，再推半格。</b><see cref="PixelAlign.SnapStroke"/> 是半像素的<b>偏移</b>，
    /// 不是吸附——它假定传进去的坐标已经落在像素原点（整数）上，只负责按笔画奇偶补那半格。
    /// <see cref="ChartHitTest.YFor"/> 给出的却是任意小数行号（值域到像素的映射与窗口量级有关），
    /// 直接递给它只会得到"小数 + 0.5"：看着像对齐，实际照样骑在两行设备像素之间。
    /// 漏掉取整这一步在测试里是可见的（<c>ChartFrameTests</c> 里那条小数行号的用例会红）。
    /// </para>
    /// <para>
    /// <b>这套对齐只属于网格线，绝不许推广到数据点上</b>：网格线落在哪一行是呈现选择，
    /// 挪半格不改变它代表什么；数据点的坐标是数据本身，挪一点点就是对数值撒谎。
    /// </para>
    /// <para>
    /// <b>这个"锐"只在 100% 缩放下成立</b>（<c>整数 + 0.5</c> 铺满一列设备像素的前提是 1 逻辑像素
    /// 恰好等于 1 设备像素）。缩放比非整数时（125% / 150% / 175%…）真正的锐利中线落在设备坐标
    /// <c>ℤ + 0.25</c> / <c>ℤ + 0.75</c> 这类位置上，任何"整数 + 0.5"的逻辑偏移都够不到：
    /// 那时这里至多把骑跨减轻、不会更糟，但<b>不承诺锐利</b>。按当前缩放比做真正的对齐需要缩放比本身，
    /// 那是调用方（页面 / 宿主）的事，见 <see cref="PixelAlign"/> 的备注。
    /// </para>
    /// <para>
    /// <b>行号非有限就跳过</b>（坏掉的绘图区几何、或调用方直接喂了坏刻度）：让它进几何会把同一帧里
    /// 所有坐标一起染成 <c>NaN</c>，与数据映射那侧的跳过是同一条纪律。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不碰绘制栈，可在没有 UI 线程的测试里直接调用。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<double> GridLineYs(IReadOnlyList<double> ticks, ValueRange window, Rect plotArea)
    {
        List<double> rows = new(ticks.Count);

        foreach (double tick in ticks)
        {
            double y = ChartHitTest.YFor(tick, window, plotArea.Y, plotArea.Height);

            if (!double.IsFinite(y))
            {
                continue;
            }

            // 两步，缺一不可：先把小数行号吸到最近的整数（100% 缩放下的设备像素网格），
            // 再按笔画奇偶推半格。**SnapStroke 是"半像素的偏移"，不是"吸附到网格"**：
            // 它只保证奇数宽 +0.5，前提是喂进去的坐标本来就在像素原点（整数）上。
            // 直接把小数行号递进去得到的是"小数 + 0.5"，照样骑在两行设备像素之间——
            // 看着像对齐了，其实一点没变锐。网格线是呈现装饰，挪半格不改变它代表什么；
            // 数据点则绝不许这么挪。
            rows.Add(PixelAlign.SnapStroke(Math.Round(y), GridStrokeThickness));
        }

        return rows;
    }
}
