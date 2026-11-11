using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 折线图：把一条数据序列映射成绘图区里的一条折线（可选地在折线下填一层半透明的面积）。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的三件事：横向网格线、折线、可选的面积填充。
/// 序列色取基座的 <see cref="ChartSurface.PrimaryBrush"/>（"一切数据序列的颜色"都取它与
/// <see cref="ChartSurface.SecondaryBrush"/> 之一，<b>不另起画笔名</b>）。
/// </para>
/// <para>
/// <b>画笔为 <c>null</c> 是合法状态，含义是"这一层不画"</b>：Task 11 落地样式之前三支画笔全是
/// <c>null</c>，控件必须照样正常渲染。本文件<b>不给任何画笔赋字面色值</b>——配色只在样式库里出现。
/// </para>
/// <para>
/// <b>能测的部分全被推进了纯静态函数。</b>仓库的测试栈不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，<c>RenderChart</c> 里画的每一笔在自动化里都断言不了——
/// 于是整条链都搬进了纯函数：<see cref="ChartFrame.ComputeWindow"/>（数据 → 可画窗口 + 刻度）与
/// <see cref="ChartFrame.GridLineYs"/>（刻度 → 网格行号 + 像素对齐）留在
/// <see cref="Tsp.Desktop.Charts.Layout"/>（折线 / 柱 / K 线三个图表共用同一份"框"的算术），
/// <see cref="BuildPoints"/>（下标 → x、值 → y、跳过不可绘制的点）则是本控件自己的映射。
/// 三个都由测试直接调用（<c>LineChartPathTests</c> 与 <c>ChartFrameTests</c>）。
/// <c>RenderChart</c> 只剩「取哪个值、按画笔是否为 <c>null</c> 决定画不画、按什么顺序画」，
/// <b>没有一处映射算术</b>。
/// Task 7–10 的柱 / 环 / 雷达 / 热力图沿用这个形状：能推进纯函数的全推进去——
/// 连"框"（窗口、网格行号）也别留在渲染路径里，那是每个图表都要重写一遍的同一份算术，
/// 已经共享的那一份在 <see cref="ChartFrame"/>，别再各抄一遍。
/// </para>
/// </remarks>
public sealed class LineChart : ChartSurface
{
    /// <summary>
    /// 网格线的目标条数。取 4：<see cref="AxisTicks.Nice"/> 只保证"大致想要几条"（实际条数会在目标值附近浮动，
    /// 向外取整还会两端各多一格），4 落在 3–6 条这个区间里——在几十像素高的绘图区里读得清，也不至于挤成一团。
    /// </summary>
    private const int GridLineTargetCount = 4;

    /// <summary>折线宽度。数据线<b>不做</b>像素对齐——见 <see cref="RenderChart"/> 的备注。</summary>
    private const double LineStrokeThickness = 1.5d;

    /// <summary>面积填充的不透明度。底色画笔往往与控件底色相近，不透明填上去会把网格线整片盖掉。</summary>
    private const double AreaFillOpacity = 0.25d;

    /// <summary>折线的数据点（按时间顺序）与值域的计算依据。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>是否在折线下方填一层半透明的面积，默认 <c>false</c>。</summary>
    public static readonly StyledProperty<bool> FillAreaProperty =
        AvaloniaProperty.Register<LineChart, bool>(nameof(FillArea));

    static LineChart()
    {
        // 数据与开关都会改变画面，两个都要登记：少了它，赋了新数据也不重绘
        // （AffectsRender 的注册状态没有公开查询接口，漏登记在运行期没有任何信号）。
        AffectsRender<LineChart>(ValuesProperty, FillAreaProperty);
    }

    /// <summary>折线的数据点，按时间顺序排列。为 <c>null</c> 或空表示没有数据。赋值只发生在页面或样式里。</summary>
    /// <remarks>
    /// 序列里允许出现非有限值（<c>NaN</c> / <c>±Infinity</c>）：指标预热期本来就成段是 <c>NaN</c>，
    /// 那是"这个点没有值"的合法表示。它们<b>不会</b>被画出来——见 <see cref="BuildPoints"/>。
    /// </remarks>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>是否在折线下方填一层面积，默认 <c>false</c>。填充用 <see cref="ChartSurface.SurfaceBrush"/>；它为 <c>null</c> 时整层不画。</summary>
    public bool FillArea
    {
        get => GetValue(FillAreaProperty);
        set => SetValue(FillAreaProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的点：序列里**至少要有一个有限值**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 空序列时基座会整块收手（底色、边框、网格线一概不画）——没有数据却画出一套网格，
    /// 比什么都不画更容易被误读成"数据全是 0"（<see cref="ChartSurface.HasData"/> 的文档原话）。
    /// <b>判据不能只看"长度非零"</b>：整条序列全是非有限值（指标预热期就是一个自成段的 <c>NaN</c> 前缀）
    /// 时，<see cref="ValueRange.From"/> 会拿它兜底的 <c>(0, 1)</c> 窗口去撑起一套网格线——
    /// 一个与序列毫无关系的刻度。今天没露馅只因为本控件还不画刻度文字；一旦有了标签，那根轴就是假的。
    /// </para>
    /// <para>
    /// 所以这里与 <see cref="BuildPoints"/> 的跳过判据保持一致：**非有限 = 没有值**。
    /// 一个有限值就足够画出一根有意义的轴（其余点跳过即可），这也是 Tasks 7–10 的统一答案
    /// （计划已把它写进全局约束）。代价是每帧一次 O(n) 扫描，相对于每帧重建刻度列表的开销可以忽略。
    /// </para>
    /// </remarks>
    protected override bool HasData =>
        Values is { Count: > 0 } values && values.Any(double.IsFinite);

    /// <summary>
    /// 画框里的东西：网格线 → 面积填充 → 折线。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>可画窗口由整齐刻度定出</b>（<see cref="ChartFrame.ComputeWindow"/>），而不是直接拿数据的极值：
    /// <see cref="AxisTicks.Nice"/> 的首尾刻度覆盖数据极值又落在整齐值上，于是网格线落在
    /// 1 / 2 / 5 × 10ⁿ 这类读数舒服的位置，曲线也不会紧贴在框边上。
    /// 窗口同时是折线的值域——两者<b>必须是同一个</b>，否则网格线与曲线各说各话：
    /// 这里把同一个 <c>window</c> 交给 <see cref="ChartFrame.GridLineYs"/> 与 <see cref="BuildPoints"/>。
    /// <b>本方法里没有任何映射算术</b>（窗口、网格行号来自 <see cref="ChartFrame"/>，折线点来自
    /// <see cref="BuildPoints"/>），留在这里的只有"取哪个值、画不画、按什么顺序画"。
    /// </para>
    /// <para>
    /// <b>像素对齐只用在网格线上，绝不用在数据点上。</b>网格线"落在哪一行"是纯粹的呈现选择，
    /// 把它对齐到像素网格能让 1px 的线在 100% 缩放下变锐（两步：行号先取整、再按笔画奇偶推半格，
    /// 见 <see cref="ChartFrame.GridLineYs"/>）；而数据点的坐标是<b>它自己的值</b>，
    /// 挪半个像素就是对数值撒谎。两处目标不同，别把这条规则推广到折线上。
    /// </para>
    /// <para>
    /// <b>绘制顺序是"网格 → 面积 → 折线"</b>：面积是半透明的（<see cref="AreaFillOpacity"/>），
    /// 压在网格上只是把它染色而不是抹掉；折线画在最上层，保证在任何填充之上都清晰。
    /// </para>
    /// <para>
    /// 少于两个点时直接收手：一个点连不出线段，面积更无从谈起。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<double>? values = Values;

        // 两条收手理由，性质不同：
        //  - values 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是调用 BuildPoints 的必要条件——它的入参不可空；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定（BuildPoints 自己没有政策，
        //    零尺寸下它照算，只是所有点都退化成同一个坐标）。
        if (values is null || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        (ValueRange window, IReadOnlyList<double> ticks) = ChartFrame.ComputeWindow(values, GridLineTargetCount);

        DrawGridLines(context, ChartFrame.GridLineYs(ticks, window, plotArea), plotArea);

        IReadOnlyList<Point> points = BuildPoints(values, window, plotArea);

        if (points.Count < 2)
        {
            return;
        }

        if (FillArea && SurfaceBrush is { } fillBrush)
        {
            // 用 PushOpacity 而不是另造一支半透明画刷：画笔的身份保持原样（主题令牌换成什么颜色
            // 这里都不用知道），"这一层更淡"是渲染层的状态，不是又一支画笔。
            using (context.PushOpacity(AreaFillOpacity))
            {
                context.DrawGeometry(fillBrush, null, BuildAreaGeometry(points, plotArea));
            }
        }

        if (PrimaryBrush is { } lineBrush)
        {
            context.DrawGeometry(null, new Pen(lineBrush, LineStrokeThickness), BuildPolylineGeometry(points));
        }
    }

    /// <summary>
    /// 把一条序列按<b>下标 → 横坐标</b>、<b>值 → 纵坐标</b>映射成绘图区里的点，跳过画不出来的点。
    /// </summary>
    /// <param name="values">数据序列，按时间顺序；元素可以是非有限值。空序列返回空表。</param>
    /// <param name="range">纵坐标对应的值域，通常是网格线首尾刻度定出的可画窗口。</param>
    /// <param name="plotArea">绘图矩形（控件局部坐标）。宽度为 0 时所有点落在同一条竖线上，本函数照算不误。</param>
    /// <returns>
    /// 可绘制的点，<b>按原下标顺序</b>排列，个数可以少于 <paramref name="values"/> 的长度（被跳过的那些不占位）。
    /// <b>只要绘图区坐标有限</b>（<see cref="ChartSurface.PlotArea"/> 保证如此），返回的坐标就全部有限：
    /// y 由非有限判定筛过，x 由有限的绘图区与下标算出。
    /// <b>本函数不校验 <paramref name="plotArea"/> 本身</b>——它的 <c>X</c> / <c>Width</c> 是坏值时，
    /// 坏值会原样进到 x 里；给一个可画的矩形是调用方的事（基座的 <c>PlotArea</c> 已经把内边距与尺寸夹干净）。
    /// 任何输入都不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>调用方义务（Task 3 对 <see cref="ChartHitTest.YFor"/> 的契约，本函数是它的落实处）：
    /// 建几何之前必须跳过非有限结果，判据是 <c>!double.IsFinite(y)</c>，不是"是否越出值域"。</b>
    /// <see cref="ChartHitTest.YFor"/> 对非有限的 <c>value</c> <b>原样透传</b>非有限结果，
    /// 那正是"这一点没有值"的信号——指标（均线之类）预热期<b>合法地</b>产生 <c>NaN</c>。
    /// 两件事不能混：
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>折成绘图区正中是最糟的错法</b>：那会把"这里缺个点"画成一条往中间掉的
    /// <b>假线</b>，读者看到的是错误的价格走势——比缺点更难查。</description></item>
    /// <item><description><b>用区间比较代替非有限判定同样拦不住</b>：<c>NaN</c> 与任何数比较都是 <c>false</c>，
    /// 它会带着出生坐标一路潜进几何，把同一帧里所有坐标一起染成 <c>NaN</c>，于是整块图什么都画不出来，
    /// 还不抛异常。<c>±Infinity</c> 虽然确实越界，但"越界"与"没值"是两回事——
    /// 尖峰本来就该如实跑到框外，不该被这条规则顺手吃掉。</description></item>
    /// </list>
    /// <para>
    /// <b>跳过之后剩下的点仍按原下标定位</b>（步长恒为 <c>绘图区宽度 / (原序列长度 - 1)</c>），
    /// 不是被挤到一起——否则一个有预热期的指标会把整条曲线整体压缩、与网格线对不上。
    /// </para>
    /// <para>
    /// <b>段语义：跨过缺口，不断开（jump，not break）。</b>返回值是一个有序点列，渲染方照着连一条折线，
    /// 于是被跳过的下标两侧的邻点之间<b>会连出一条直段</b>。选 jump 的两条理由，都是关于这份返回值的实话：
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>我们手上的缺口本来就是前缀。</b>指标预热期的 <c>NaN</c> 自成一段、落在序列开头，
    /// 它没有可连的左邻点——jump 与 break 在这种数据上渲染结果完全一样。两者的差别只在序列中段的空洞上。</description></item>
    /// <item><description><b>这一层平铺的点列不自述"洞"。</b>存活点不带自己的下标，返回值里也没有原序列长度，
    /// 调用方光看点列分不出"这里原来缺一个点"还是"这里本来就两点相邻"（x 是按原下标算的，
    /// 拿得到原长度的人<b>能</b>反推出来，但那是他自己补的推断，不是这份契约给的信息）。
    /// 想让"断开"成为契约的一部分，要改的就是这个签名——所以这不是个以后加个分支就能补上的实现细节，
    /// 而是本决定的<b>真正分量</b>所在。</description></item>
    /// </list>
    /// <para>
    /// <b>代价要说清楚：这是对通行做法的一次刻意偏离。</b>序列中段出现空洞时，那段时间里会画出一条
    /// 连接两侧已知点的直段，而该区间实际上<b>没有数据</b>——而中段空洞在 A 股数据里并不病态
    /// （停牌、缺一根 bar、滚动窗口有一段还没预热完，都是家常便饭）。
    /// 读折线图的人默认那里应当<b>断开</b>：matplotlib 遇到 <c>NaN</c> 就断、ECharts 默认
    /// <c>connectNulls: false</c>、TradingView 也是断的。本控件跳过去，是因为今天手上的缺口只以前缀形式出现；
    /// <b>K 线控件（唯一同样有"一段没值"的图表）开工前必须把这个决定重新过一遍</b>——
    /// 要断就得连签名一起改，不能等画出来才发现多了一条假线。
    /// </para>
    /// <para>
    /// <b>单点画在水平正中。</b><c>count == 1</c> 时 <c>i / (count - 1)</c> 是 <c>0 / 0 = NaN</c>，
    /// 约定落在正中——与"零跨度落在垂直正中"同一个理由（没有跨度时正中是唯一说得通的位置），
    /// 而不是贴左边缘（那会被读成"时间轴上最早的点"）。空序列返回空表；
    /// 退化值域（全等值）由 <see cref="ChartHitTest.YFor"/> 落到垂直正中，不是贴边、不是越界。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// 本函数<b>不做</b>"要不要画"的政策决定（零尺寸绘图区照算，收手是 <see cref="RenderChart"/> 的事）。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Point> BuildPoints(IReadOnlyList<double> values, ValueRange range, Rect plotArea)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        int count = values.Count;
        double lastIndex = count - 1d;
        List<Point> points = new(count);

        for (int i = 0; i < count; i++)
        {
            // i / (count - 1)：下标 0 / count - 1 分别给出精确的 0 与 1，
            // 于是首尾两点精确落在绘图区的左右边缘上（x = Left + t × Width 在 t = 1 时就是 Right）。
            // 单点没有下标跨度（0 / 0 = NaN），约定画在水平正中。
            double t = count > 1 ? i / lastIndex : 0.5d;
            double x = plotArea.X + (t * plotArea.Width);

            // YFor 的调用方义务：非有限结果 = "这一点没有值"，不进几何。判据必须是非有限判定。
            double y = ChartHitTest.YFor(values[i], range, plotArea.Y, plotArea.Height);

            if (!double.IsFinite(y))
            {
                continue;
            }

            points.Add(new Point(x, y));
        }

        return points;
    }

    /// <summary>
    /// 在每个行号处画一条横贯绘图区的网格线。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="rows">行号（屏幕 y），由 <see cref="ChartFrame.GridLineYs"/> 给出——已对齐、已筛过非有限值。</param>
    /// <param name="plotArea">绘图矩形。</param>
    /// <remarks>
    /// 笔的宽度取 <see cref="ChartFrame.GridStrokeThickness"/>，与行号的对齐<b>共用同一个值</b>：
    /// 那半格是按笔画奇偶推的，两者用了不同的宽度，推出来的半格就与笔画的真实覆盖对不上。
    /// </remarks>
    private void DrawGridLines(DrawingContext context, IReadOnlyList<double> rows, Rect plotArea)
    {
        if (AxisBrush is not { } axisBrush)
        {
            return;
        }

        Pen pen = new(axisBrush, ChartFrame.GridStrokeThickness);

        foreach (double row in rows)
        {
            context.DrawLine(pen, new Point(plotArea.X, row), new Point(plotArea.Right, row));
        }
    }

    /// <summary>
    /// 把点列连成一条开放的折线。
    /// </summary>
    /// <param name="points">由 <see cref="BuildPoints"/> 产出的点，至少两个。</param>
    /// <returns>只描边不填充的几何。</returns>
    private static StreamGeometry BuildPolylineGeometry(IReadOnlyList<Point> points)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(points[0], isFilled: false);

            for (int i = 1; i < points.Count; i++)
            {
                path.LineTo(points[i]);
            }

            path.EndFigure(isClosed: false);
        }

        return geometry;
    }

    /// <summary>
    /// 把折线与绘图区底边围成一个闭合区域，用来填色。
    /// </summary>
    /// <param name="points">由 <see cref="BuildPoints"/> 产出的点，至少两个。</param>
    /// <param name="plotArea">绘图矩形，下边缘就是填充的基线。</param>
    /// <returns>沿折线走、再沿底边走回来的闭合几何。</returns>
    /// <remarks>
    /// 基线取<b>绘图区底边</b>而不是零轴：本控件不画零轴（值域由刻度窗口定出，可能整段在零轴同一侧），
    /// 填到零轴会让面积跑出绘图区、把"这一层的范围"画错。
    /// </remarks>
    private static StreamGeometry BuildAreaGeometry(IReadOnlyList<Point> points, Rect plotArea)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(points[0].X, plotArea.Bottom), isFilled: true);

            for (int i = 0; i < points.Count; i++)
            {
                path.LineTo(points[i]);
            }

            path.LineTo(new Point(points[^1].X, plotArea.Bottom));
            path.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
