using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Charts;

/// <summary>
/// 雷达图：把若干个已归一到 <c>0..1</c> 的维度画成正多边形上的一个顶点，连成一块面积。
/// </summary>
/// <remarks>
/// <para>
/// <b>分工。</b>外框（底色、边框、空数据短路、绘图区几何）由 <see cref="ChartSurface"/> 独占；
/// 本控件只管框里的三件事：几圈外框刻度、每个维度的辐条、以及数据围出来的那块面积。
/// 序列色取基座的 <see cref="ChartSurface.PrimaryBrush"/>（"一切数据序列的颜色"都取它与
/// <see cref="ChartSurface.SecondaryBrush"/> 之一，<b>不另起画笔名</b>）；
/// 环与辐条是坐标轴一类的东西，取 <see cref="ChartSurface.AxisBrush"/>。
/// </para>
/// <para>
/// <b>它与折线 / 柱的根本差别：不"定框"，只"归一化"。</b>雷达图的每一维都已经在
/// <c>0..1</c> 这个固定标尺上（情绪分、百分位、占比都归一到这个区间），所以它不需要
/// <see cref="ChartFrame"/> 那套"由数据定值轴窗口 + 整齐刻度"的算术，也不画网格线——
/// 它的刻度就是那几圈同心多边形，位置由半径比例定死，与数据无关。这一点与
/// <see cref="DonutChart"/> 同类。
/// </para>
/// <para>
/// <b>角度口径与 <see cref="PolarGeometry"/> 一致</b>：弧度、<c>0</c> 是 12 点方向、顺时针为正。
/// 第一个维度在 12 点，其余按数据顺序依次排下去，一圈分完。
/// </para>
/// <para>
/// <b>少于 3 个维度不画</b>：两个维度只能连出一条线，围不成"面积"，而面积正是雷达图读数的形式
/// （见 <see cref="ComputeVertices"/>）。
/// </para>
/// </remarks>
public sealed class RadarChart : ChartSurface
{
    /// <summary>一整圈。维度角以弧度计，全周就是它。</summary>
    private const double FullTurn = 2d * Math.PI;

    /// <summary>画得成雷达图的最少维度数：三个点才围得出面积。</summary>
    private const int MinAxisCount = 3;

    /// <summary>数据面积的填充不透明度。半透明是为了让内圈的刻度线透出来。</summary>
    private const double AreaFillOpacity = 0.3d;

    /// <summary>数据面积描边宽度。与折线同宽。</summary>
    private const double OutlineStrokeThickness = 1.5d;

    /// <summary>环形刻度的圈数（不含最外圈，最外圈是固定的第 1 圈）。</summary>
    /// <remarks>
    /// 与折线图的 <c>GridLineTargetCount = 4</c> 对齐：网格也好、环也好，四格是在几十到一百多像素的
    /// 绘图区里"读得出比例、又不挤成一团"的密度。
    /// </remarks>
    private const int RingCount = 4;

    /// <summary>各维度的值，<b>每维一个</b>，约定已归一到 <c>0..1</c>。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<RadarChart, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>各维度的名称，按下标与 <see cref="Values"/> 对齐。</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> AxisLabelsProperty =
        AvaloniaProperty.Register<RadarChart, IReadOnlyList<string>?>(nameof(AxisLabels));

    static RadarChart()
    {
        // 数据与维度名都会改变画面，两个都要登记（维度名落地成文字之前登记它是为将来省一次漏登记：
        // 那时候改名的重绘会跟着这条登记一起生效，不必再去补一次）。
        AffectsRender<RadarChart>(ValuesProperty, AxisLabelsProperty);
    }

    /// <summary>
    /// 各维度的值，约定已归一到 <c>0..1</c>。<c>null</c> 或不足三个表示没有可画的图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>越界的值会被夹到 <c>0..1</c>，非有限值按 0 处理</b>（见 <see cref="ComputeVertices"/>）：
    /// 十进制百分位之类的量偶尔会算出 <c>1.0000001</c>，而这块画布上 <c>1</c> 就是最外圈，
    /// 再往外没有刻度可言；负值更糟——它会落到圆心的<b>另一侧</b>，与"这一维很低"完全相反。
    /// 夹紧是<b>画布这一侧的政策</b>，不是对数据的校验：数据层该修的仍然要修，这里只是不让
    /// 一个越界值画出一张读错的图。
    /// </para>
    /// </remarks>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// 各维度的名称（如"情绪 / 资金 / 量能"），按下标与 <see cref="Values"/> 对齐；
    /// 比数据短或缺省都是合法的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>本阶段不画它。</b>整个 P1-A 没有任何一个图表画过文字：刻度文字、分片标签、维度名
    /// 一律还没有落地（<see cref="ChartSurface.LabelBrush"/> 因此至今没有使用者，
    /// 折线图的文档也把"刻度文字一出现那根轴就是假的"记在同一条账上）。
    /// 文字要带来字体令牌、字号、测量与避让四件事，是一笔独立的、要单独设计的开销，
    /// 不该顺手塞进某一个图表里——所以这里只落下<b>数据契约</b>：
    /// 让它此刻就能被赋值、被绑定、被测试，等文字那一笔落地时不必再改签名。
    /// </para>
    /// <para>
    /// 它已登记进 <c>AffectsRender</c>：改名会重绘这件事，届时跟着这条登记一起生效。
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? AxisLabels
    {
        get => GetValue(AxisLabelsProperty);
        set => SetValue(AxisLabelsProperty, value);
    }

    /// <summary>
    /// 本控件有没有可画的图形：序列里**至少要有一个有限值**（Task 7–10 的统一口径，见计划全局约束）。
    /// </summary>
    /// <remarks>
    /// 与其余图表同一个判据，理由见 <see cref="LineChart.HasData"/>。
    /// <b>这里有一处与折线图不同的后果，明说</b>：维度不足三个时（包括空序列与单项序列）
    /// <see cref="ComputeVertices"/> 返回空表，于是 <see cref="HasData"/> 为 <c>true</c> 却画不出面积，
    /// 只留下几圈空刻度。刻度的存在表示"这里有一个雷达图，只是它还没有够画成一块面积的维度"，
    /// 比整块控件凭空消失要诚实——口径统一成"有没有有限值"，是为了别让四个图表各判一套。
    /// </remarks>
    protected override bool HasData =>
        Values is { Count: > 0 } values && values.Any(double.IsFinite);

    /// <summary>
    /// 把一个维度的值换算成它在外圈上的顶点。
    /// </summary>
    /// <param name="values">各维度的值，<b>每维一个</b>，约定已归一到 <c>0..1</c>。</param>
    /// <param name="centre">圆心（控件局部坐标）。</param>
    /// <param name="radius">外圈半径，也就是值 <c>1</c> 所在的那一圈。</param>
    /// <returns>
    /// 与 <paramref name="values"/> <b>长度相同、下标一一对应</b>的顶点表：第 <c>i</c> 个顶点在
    /// <c>i × 2π / count</c> 的角度上（<c>0</c> 是 12 点方向、顺时针为正），
    /// 到圆心的距离是 <c>夹到 0..1 之后的值 × radius</c>——值为 <c>1</c> 落在最外圈、<c>0</c> 落在圆心。
    /// <b>维度少于三个（含空序列与 <c>null</c>）时返回空表</b>。任何输入都不抛异常。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>少于三个维度返回空表，而不是画一条线或一个点。</b>雷达图读的是<b>面积</b>的形状，
    /// 两个点连成的线段与一个点本身都不携带"各维度的相对大小"这个信息；画出来只会让人以为
    /// 那是一张缺了数据的地图。返回空表让渲染方直接收手（见 <see cref="HasData"/> 的备注）。
    /// </para>
    /// <para>
    /// <b>值夹到 <c>0..1</c>，非有限值按 0 处理。</b>这块画布上 <c>1</c> 就是最外圈、<c>0</c> 就是圆心，
    /// 越界与负值在这块画布上没有落点：负值会落到圆心的另一侧（把"很低"画成"很高"），
    /// 大于 1 的值会跑到刻度外面去（读数的人无从判断它到底超出多少）。
    /// 非有限值（<c>NaN</c> / <c>±Infinity</c>）折成 0 而不是透传：透传会让整块面积的<b>每一个</b>
    /// 顶点一起变成 <c>NaN</c>（几何里一个毒值染一帧，与折线图那侧同一条纪律），
    /// 而折到圆心是"这一维没有值"在这块画布上唯一说得通的位置。
    /// </para>
    /// <para>
    /// <b>顺序与角度都按数据来</b>：顶点表与数据下标一一对应，第 <c>0</c> 个固定在 12 点方向——
    /// 这样同一份数据每次重画都落在同一个形状上，也让"哪个角是哪一维"这件事可读、可对照
    /// （维度名与数据按下标对齐）。
    /// </para>
    /// <para>
    /// <b>纯函数</b>：不读控件状态、不抛异常、不碰绘制栈，因此在没有 UI 线程的测试里可以直接调用。
    /// 它<b>不做</b>"要不要画"的政策决定：半径给了 <c>0</c> 就所有顶点落在圆心，那是调用方的事。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Point> ComputeVertices(IReadOnlyList<double> values, Point centre, double radius)
    {
        if (values is null || values.Count < MinAxisCount)
        {
            return [];
        }

        double step = FullTurn / values.Count;
        var vertices = new List<Point>(values.Count);

        for (int i = 0; i < values.Count; i++)
        {
            (double x, double y) = PolarGeometry.OnCircle(i * step, centre.X, centre.Y, radius * LevelOf(values[i]));
            vertices.Add(new Point(x, y));
        }

        return vertices;
    }

    /// <summary>
    /// 画这块雷达：几圈刻度 → 各维度的辐条 → 数据围出来的面积。
    /// </summary>
    /// <param name="context">绘制上下文，由基座传入。</param>
    /// <param name="plotArea">已扣除内边距的绘图矩形，尺寸可能为 0。</param>
    /// <remarks>
    /// <para>
    /// <b>圆心取绘图区正中、外圈半径取长短边的一半</b>：雷达图必须是正圆上的正多边形，
    /// 短边才是限制，长边多出来的部分留白（与 <see cref="DonutChart"/> 同一条理由的同一处落点）。
    /// </para>
    /// <para>
    /// <b>绘制顺序是"刻度圈 → 辐条 → 面积"</b>：面积是半透明的（<see cref="AreaFillOpacity"/>），
    /// 压在刻度上只是把它染色而不是抹掉；描边画在最上层，保证面积的轮廓在任何填充之上都清晰。
    /// </para>
    /// <para>
    /// <b>这里一处都不用 <see cref="PixelAlign"/>。</b>那套半像素偏移是为"水平或垂直的 1px 线"
    /// 定的（轴线、网格线、柱边）：它假定笔画的中线是一条贯穿整个绘图区的直线。
    /// 刻度圈与辐条是斜着的多边形边，逐点吸附只会让多边形的顶点各自挪半格、把正多边形掰歪——
    /// 图形的锐利不是靠挪顶点换来的。数据面积更不用说：顶点就是数据本身，挪一点点就是对数值撒谎。
    /// </para>
    /// </remarks>
    protected override void RenderChart(DrawingContext context, Rect plotArea)
    {
        IReadOnlyList<double>? values = Values;

        // 两条收手理由，性质不同：
        //  - values 为 null 是给类型系统看的（基座已经在 HasData 里短路了空数据，这里跑不到），
        //    但它是 ComputeVertices 的必要条件——它的入参不可空；
        //  - 尺寸为 0 表示"这块地方画不出来"，这是渲染方的政策决定。
        if (values is null || plotArea.Width <= 0d || plotArea.Height <= 0d)
        {
            return;
        }

        Point centre = new(plotArea.X + (plotArea.Width / 2d), plotArea.Y + (plotArea.Height / 2d));
        double radius = Math.Min(plotArea.Width, plotArea.Height) / 2d;

        IReadOnlyList<Point> vertices = ComputeVertices(values, centre, radius);

        if (vertices.Count == 0)
        {
            return;
        }

        DrawScales(context, vertices.Count, centre, radius);

        if (PrimaryBrush is not { } seriesBrush)
        {
            return;
        }

        StreamGeometry area = BuildPolygon(vertices);

        // 用 PushOpacity 而不是另造一支半透明画刷：画笔的身份保持原样（主题令牌换成什么颜色
        // 这里都不用知道），"这一层更淡"是渲染层的状态，不是又一支画笔。
        using (context.PushOpacity(AreaFillOpacity))
        {
            context.DrawGeometry(seriesBrush, null, area);
        }

        context.DrawGeometry(null, new Pen(seriesBrush, OutlineStrokeThickness), area);
    }

    /// <summary>
    /// 画刻度：<see cref="RingCount"/> 圈同心正多边形，加上从圆心到最外圈每个顶点的辐条。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="axisCount">维度数（顶点的个数）。</param>
    /// <param name="centre">圆心。</param>
    /// <param name="radius">外圈半径。</param>
    /// <remarks>
    /// 顶点的角度不由数据决定，只由"第几维"决定（<c>i × 2π / count</c>），
    /// 所以它一份数据与一份纯比例就能算出来；形状取自 <see cref="PolarGeometry"/>，
    /// 与数据面积用的是同一个换算。
    /// 画笔为 <c>null</c> 时整层不画（"<c>null</c> 画笔 = 这一层不画"是基座定下的口径）。
    /// </remarks>
    private void DrawScales(DrawingContext context, int axisCount, Point centre, double radius)
    {
        if (AxisBrush is not { } axisBrush)
        {
            return;
        }

        Pen pen = new(axisBrush, 1d);
        double step = FullTurn / axisCount;

        for (int ring = 1; ring <= RingCount; ring++)
        {
            double ringRadius = radius * ring / RingCount;
            var ringPoints = new List<Point>(axisCount);

            for (int i = 0; i < axisCount; i++)
            {
                (double x, double y) = PolarGeometry.OnCircle(i * step, centre.X, centre.Y, ringRadius);
                ringPoints.Add(new Point(x, y));
            }

            context.DrawGeometry(null, pen, BuildPolygon(ringPoints));
        }

        for (int i = 0; i < axisCount; i++)
        {
            (double x, double y) = PolarGeometry.OnCircle(i * step, centre.X, centre.Y, radius);
            context.DrawLine(pen, centre, new Point(x, y));
        }
    }

    /// <summary>
    /// 把一串顶点连成一个闭合多边形。
    /// </summary>
    /// <param name="points">顶点，<b>至少一个</b>（少于三个的调用方已经在 <see cref="ComputeVertices"/> 处收手）。</param>
    /// <returns>只填充不描边的闭合几何，描边由调用方自己给。</returns>
    private static StreamGeometry BuildPolygon(IReadOnlyList<Point> points)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(points[0], isFilled: true);

            for (int i = 1; i < points.Count; i++)
            {
                path.LineTo(points[i]);
            }

            path.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// 一维的值落在外圈的哪个比例上：有限值夹到 <c>0..1</c>，非有限值按 0（也就是圆心）。
    /// </summary>
    /// <remarks>
    /// 判据先看有限性再看夹紧：<c>NaN</c> 与任何数比较都为 <c>false</c>，直接 <c>Math.Clamp</c> 会把它
    /// 原样放出去，于是"这一维没有值"会带着出生坐标潜进几何，把同一帧里所有顶点一起染成 <c>NaN</c>。
    /// </remarks>
    private static double LevelOf(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
}
