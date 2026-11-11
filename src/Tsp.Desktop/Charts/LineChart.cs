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
/// <b>能测的部分全被推进了 <see cref="BuildPoints"/>。</b>仓库的测试栈不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，<c>RenderChart</c> 里画的每一笔在自动化里都断言不了——
/// 于是映射链（下标 → x、值 → y、跳过不可绘制的点）整条放进纯静态函数，由
/// <c>LineChartPathTests</c> 直接调用；<c>RenderChart</c> 只剩「取窗口、把这些点交给几何、
/// 按画笔是否为 <c>null</c> 决定画不画」这几步，没有一处映射算术。
/// Task 7–10 的柱 / 环 / 雷达 / 热力图沿用这个形状：能推进纯函数的全推进去。
/// </para>
/// </remarks>
public sealed class LineChart : ChartSurface
{
    /// <summary>网格线的目标条数。交给 <see cref="AxisTicks.Nice"/> 的"大致想要几条"，实际条数会在它附近浮动。</summary>
    private const int GridLineTargetCount = 4;

    /// <summary>网格线宽度（逻辑像素）。整数宽度才走得出 <see cref="PixelAlign.SnapStroke"/> 的奇偶规则。</summary>
    private const double GridStrokeThickness = 1d;

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
    /// 本控件有没有可画的点：序列非空即可。
    /// </summary>
    /// <remarks>
    /// 空序列时基座会整块收手（底色、边框、网格线一概不画）——没有数据却画出一套网格，
    /// 比什么都不画更容易被误读成"数据全是 0"。
    /// 注意<b>全是非有限值</b>的序列在这里算"有数据"（长度非零），于是会画出一套网格而没有折线：
    /// 这是刻意的，序列在预热期的确存在、只是还没值，画出时间轴比画成空白更如实。
    /// </remarks>
    protected override bool HasData => Values is { Count: > 0 };

    /// <summary>
    /// 画框里的东西：网格线 → 面积填充 → 折线。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>可画窗口由整齐刻度定出</b>，而不是直接拿数据的极值：<see cref="AxisTicks.Nice"/> 的首尾刻度
    /// 覆盖数据极值又落在整齐值上，于是网格线落在 1 / 2 / 5 × 10ⁿ 这类读数舒服的位置，
    /// 曲线也不会紧贴在框边上。窗口同时是折线的值域——两者<b>必须是同一个</b>，
    /// 否则网格线与曲线各说各话。
    /// </para>
    /// <para>
    /// <b>像素对齐只用在网格线上，绝不用在数据点上。</b>网格线的"落哪一行"是纯粹的呈现选择，
    /// 推半个像素能让 1px 的线在 100% 缩放下变锐；而数据点的坐标是<b>它自己的值</b>，
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

        ValueRange dataRange = ValueRange.From(values);
        IReadOnlyList<double> ticks = AxisTicks.Nice(dataRange.Min, dataRange.Max, GridLineTargetCount);
        ValueRange window = new(ticks[0], ticks[^1]);

        DrawGridLines(context, ticks, window, plotArea);

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
    /// 可绘制的点，<b>按原下标顺序</b>排列，个数可以少于 <paramref name="values"/> 的长度
    /// （被跳过的那些不占位），坐标全部有限。任何输入都不抛异常、不返回非有限坐标。
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
    /// 于是被跳过的下标两侧的邻点之间<b>会连出一条直段</b>。选它而不是"断开"的两条理由：
    /// 一是真实场景里被跳过的那一段是<b>前缀</b>（指标预热期），前缀没有可连的左邻点，
    /// 两种画法渲染出来完全一样，差别只在序列中段的空洞上；
    /// 二是"断开"需要把返回值变成"若干连续段"（<c>IReadOnlyList&lt;IReadOnlyList&lt;Point&gt;&gt;</c> 之类）
    /// 才能表达段边界，而本阶段给折线图定的返回类型是一层平铺的点列——为一种病态输入
    /// 把接口升成两层结构，Tasks 7–10 都要跟着抄。
    /// 代价要说清楚：中段有空洞时，被跳过的那段时间里会画出一条连接两侧已知点的直段，
    /// 而那段区间实际上没有数据。若将来指标真的会出现成段中空，这里要改成按段返回。
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
    /// 在每个刻度处画一条横贯绘图区的网格线。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="ticks">刻度值，由 <see cref="AxisTicks.Nice"/> 产出。</param>
    /// <param name="window">这些刻度首尾定出的值域（与折线用的是同一个）。</param>
    /// <param name="plotArea">绘图矩形。</param>
    /// <remarks>
    /// <para>
    /// <b>只有 y 过 <see cref="PixelAlign.SnapStroke"/>。</b>横线"落在哪一行"由 y 决定，
    /// 1px 宽的线以整数 y 为中线时正好骑在相邻两行设备像素的交界上，抗锯齿会把它涂成 2px 的糊边；
    /// 推半格后它铺满整数行，100% 缩放下是锐的（其它缩放比下只能减轻模糊，见该函数的备注）。
    /// 两端<b>不</b>对齐：对齐会把线两端各推出半个像素、越过绘图区边缘，那是另一种错。
    /// </para>
    /// <para>
    /// 网格线首尾两条正好落在绘图区的上下边缘上（窗口就是由首尾刻度定出来的），
    /// 于是绘图区自带一圈淡淡的框，与基座画的控件边框区分得开。
    /// </para>
    /// </remarks>
    private void DrawGridLines(DrawingContext context, IReadOnlyList<double> ticks, ValueRange window, Rect plotArea)
    {
        if (AxisBrush is not { } axisBrush)
        {
            return;
        }

        Pen pen = new(axisBrush, GridStrokeThickness);

        foreach (double tick in ticks)
        {
            double y = ChartHitTest.YFor(tick, window, plotArea.Y, plotArea.Height);

            // 窗口由刻度自己定出来，正常输入下 y 一定有限；非有限只会来自坏掉的绘图区几何，
            // 那种点送进几何会把整帧染成 NaN，所以照 BuildPoints 的同一条纪律跳过。
            if (!double.IsFinite(y))
            {
                continue;
            }

            double snappedY = PixelAlign.SnapStroke(y, GridStrokeThickness);

            context.DrawLine(pen, new Point(plotArea.X, snappedY), new Point(plotArea.Right, snappedY));
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
