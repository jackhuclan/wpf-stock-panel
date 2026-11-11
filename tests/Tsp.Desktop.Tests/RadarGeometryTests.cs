using System.Reflection;
using Avalonia;
using Tsp.Desktop.Charts;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="RadarChart"/> 的顶点换算（<c>ComputeVertices</c>），外加控件自身几条可离线断言的契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，所以 <c>RenderChart</c> 里画的每一笔在本工程里<b>都断言不了</b>
/// ——「刻度圈画没画」「面积真的半透明」只能靠实机验证。于是顶点那段算术压进
/// <see cref="RadarChart.ComputeVertices"/>（本文件），极坐标换算压进 <c>PolarGeometry</c>
/// （<c>PolarGeometryTests</c>），<c>RenderChart</c> 只剩下「取哪个值、按画笔是否为 <c>null</c>
/// 决定画不画、按什么顺序画」。
/// </para>
/// <para>
/// <b>本文件覆盖</b>：三个维度全 1 时落在最外圈、零值收拢到圆心、在值域内的顶点不越出外圈、
/// 空输入与不足三维度、越界值夹紧、非有限值不传染、顶点的顺序与起始方向、
/// <c>HasData</c> 的口径（非空**且至少一个有限值**）、两个 <c>StyledProperty</c> 的注册。
/// <b>不覆盖</b>任何绘制行为（刻度圈、辐条、面积的颜色与不透明度），那部分只有实机验证一条路；
/// 也<b>不覆盖</b> <c>AxisLabels</c> 的文字落点——本阶段没有任何图表画文字，见该属性的文档。
/// </para>
/// <para>
/// 角度一律以<b>弧度</b>计、<c>0</c> 是 12 点方向、顺时针为正（与 <c>PolarGeometry</c> 同一口径）。
/// 装饰用的圆心取 <c>(50, 50)</c>、外圈半径取 <c>20</c>，都是整十的好算数。
/// </para>
/// </remarks>
public sealed class RadarGeometryTests
{
    /// <summary>三角函数结果不可能是精确的 0 / 1，只断言到这个量级以内。</summary>
    private const double Tolerance = 1e-9d;

    private static readonly Point _centre = new(50d, 50d);

    private const double Radius = 20d;

    [Fact]
    public void Three_axes_produce_three_vertices_on_the_outer_ring_when_all_values_are_one()
    {
        // 全 1 = 每一维都到顶：三个顶点必须**全部**落在最外圈上，且构成一个正三角形。
        // 少一个维度、多一个维度、或者角度步长算错，这条都会红。
        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices([1d, 1d, 1d], _centre, Radius);

        Assert.Equal(3, vertices.Count);
        Assert.All(vertices, vertex => Assert.Equal(Radius, DistanceFromCentre(vertex), Tolerance));

        // 第一个顶点固定在 12 点方向，其余依次顺时针排下去（120° 一格）。
        Assert.Equal(_centre.X, vertices[0].X, Tolerance);
        Assert.Equal(_centre.Y - Radius, vertices[0].Y, Tolerance);
        Assert.Equal(_centre.X + (Radius * Math.Sin(2d * Math.PI / 3d)), vertices[1].X, Tolerance);
        Assert.Equal(_centre.Y - (Radius * Math.Cos(2d * Math.PI / 3d)), vertices[1].Y, Tolerance);
        Assert.True(vertices[1].X > _centre.X, "第二个维度应当在右边（顺时针）。");
    }

    [Fact]
    public void Zero_values_collapse_to_the_centre()
    {
        // 全 0 = 每一维都到底：顶点收拢到圆心，面积退化成一点。
        // 关键是它**落到圆心**而不是掉到圆心的另一侧（那是负值才会有的形态）。
        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices([0d, 0d, 0d, 0d], _centre, Radius);

        Assert.Equal(4, vertices.Count);
        Assert.All(vertices, vertex => Assert.Equal(_centre, vertex));
    }

    [Fact]
    public void Vertices_stay_within_the_radius_for_in_range_values()
    {
        // 值域内的顶点绝不越出外圈——越出去就是画到了刻度之外，读数的人无从判断超了多少。
        double[] values = [0d, 0.2d, 0.5d, 0.8d, 1d];

        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices(values, _centre, Radius);

        Assert.Equal(values.Length, vertices.Count);

        for (int i = 0; i < values.Length; i++)
        {
            // 距离与值成正比：0.5 就正好在半圈上。这条把"半径乘的是值、不是别的什么东西"钉死。
            Assert.Equal(values[i] * Radius, DistanceFromCentre(vertices[i]), Tolerance);
        }
    }

    [Fact]
    public void Empty_input_returns_no_vertices()
    {
        Assert.Empty(RadarChart.ComputeVertices([], _centre, Radius));
        Assert.Empty(RadarChart.ComputeVertices(null!, _centre, Radius));
    }

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new[] { 1d })]
    [InlineData(new[] { 1d, 0.5d })]
    public void Fewer_than_three_axes_returns_no_vertices(double[] values)
    {
        // 雷达图读的是**面积**的形状。两个点连成的线段、一个点本身都不携带"各维度的相对大小"
        // 这个信息，画出来只会让人以为那是一张缺了数据的地图——所以直接不画，而不是画条线凑数。
        Assert.Empty(RadarChart.ComputeVertices(values, _centre, Radius));
    }

    [Fact]
    public void Values_beyond_the_scale_are_clamped_onto_the_ring_or_the_centre()
    {
        // 这块画布上 1 就是最外圈、0 就是圆心，越界值没有落点：负值会跑到圆心的**另一侧**
        // （把"很低"画成"很高"，方向完全相反），大于 1 的值会跑到刻度外面去。
        // 夹紧是画布这一侧的政策，不是对数据的校验。
        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices([2d, -1d, 0.5d], _centre, Radius);

        Assert.Equal(Radius, DistanceFromCentre(vertices[0]), Tolerance);       // 2 → 夹到 1（外圈）
        Assert.Equal(_centre, vertices[1]);                                     // -1 → 夹到 0（圆心）
        Assert.Equal(Radius / 2d, DistanceFromCentre(vertices[2]), Tolerance);  // 值域内的原样
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_do_not_poison_the_other_vertices(double badValue)
    {
        // 非有限值折到圆心，**不是**原样透传：透传会让整块面积的每一个顶点一起变成 NaN
        // （几何里一个毒值染一帧，与折线图那侧同一条纪律）。
        // 这条同时钉住"其余顶点不受影响"——好点必须照常落在自己的位置上。
        double[] values = [badValue, 0.5d, 1d, 0.25d];

        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices(values, _centre, Radius);

        Assert.Equal(values.Length, vertices.Count);
        Assert.Equal(_centre, vertices[0]);
        Assert.All(vertices, vertex => Assert.True(double.IsFinite(vertex.X) && double.IsFinite(vertex.Y)));

        Assert.Equal(0.5d * Radius, DistanceFromCentre(vertices[1]), Tolerance);
        Assert.Equal(Radius, DistanceFromCentre(vertices[2]), Tolerance);
        Assert.Equal(0.25d * Radius, DistanceFromCentre(vertices[3]), Tolerance);
    }

    [Fact]
    public void The_first_vertex_is_at_twelve_oclock_and_the_rest_run_clockwise()
    {
        // 顺序与起始方向是"哪个角是哪一维"的全部依据：维度名与数据按下标对齐，
        // 起始方向错了、或者顺序反了，整张图会与它旁边那排维度名对不上。
        IReadOnlyList<Point> vertices = RadarChart.ComputeVertices([1d, 1d, 1d, 1d], _centre, Radius);

        Assert.Equal(4, vertices.Count);

        // 四维：12 点 → 3 点 → 6 点 → 9 点，顺时针一格 90°。
        Assert.Equal(_centre.X, vertices[0].X, Tolerance);
        Assert.Equal(_centre.Y - Radius, vertices[0].Y, Tolerance);

        Assert.Equal(_centre.X + Radius, vertices[1].X, Tolerance);
        Assert.Equal(_centre.Y, vertices[1].Y, Tolerance);

        Assert.Equal(_centre.X, vertices[2].X, Tolerance);
        Assert.Equal(_centre.Y + Radius, vertices[2].Y, Tolerance);

        Assert.Equal(_centre.X - Radius, vertices[3].X, Tolerance);
        Assert.Equal(_centre.Y, vertices[3].Y, Tolerance);
    }

    [Fact]
    public void RadarChart_has_data_only_when_at_least_one_value_is_finite()
    {
        // HasData 是基座用来做"空数据短路"的钩子：为 false 时底色、边框一概不画。
        // 判据与其余图表统一（计划全局约束）：**非空不够，必须至少有一个有限值**。
        //
        // 另有一处雷达图特有的后果（已写进 HasData 的文档）：维度不足三个时有数据却画不出面积，
        // 留下一圈空刻度——刻度表示"这里有一个雷达图，只是维度还不够"。
        RadarChart chart = new();

        Assert.False(HasDataOf(chart));

        chart.Values = [];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, double.PositiveInfinity];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, 0.5d];
        Assert.True(HasDataOf(chart));

        // 两个有限值：有数据，但围不成面积——两件事分开判，别把后者混进 HasData。
        chart.Values = [0.5d, 0.5d];
        Assert.True(HasDataOf(chart));
        Assert.Empty(RadarChart.ComputeVertices(chart.Values, _centre, Radius));

        chart.Values = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void RadarChart_declares_values_and_axis_labels_as_styled_properties()
    {
        // 两者都必须是 StyledProperty 且注册在 RadarChart 自己名下。
        // AxisLabels 本阶段没有使用者（没有任何图表画文字），这里钉的是**数据契约**：
        // 它此刻就能被赋值、被绑定，等文字那一笔落地时不必再改签名。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<double>?>>(RadarChart.ValuesProperty);
        Assert.Equal(typeof(IReadOnlyList<double>), RadarChart.ValuesProperty.PropertyType);
        Assert.Equal(nameof(RadarChart.Values), RadarChart.ValuesProperty.Name);
        Assert.Equal(typeof(RadarChart), RadarChart.ValuesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<string>?>>(RadarChart.AxisLabelsProperty);
        Assert.Equal(typeof(IReadOnlyList<string>), RadarChart.AxisLabelsProperty.PropertyType);
        Assert.Equal(nameof(RadarChart.AxisLabels), RadarChart.AxisLabelsProperty.Name);
        Assert.Equal(typeof(RadarChart), RadarChart.AxisLabelsProperty.OwnerType);
    }

    [Fact]
    public void RadarChart_is_a_sealed_chart_surface_with_no_data_by_default()
    {
        // 无 UI 线程下能被构造出来，说明 RadarChart 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(RadarChart).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(RadarChart).BaseType);

        RadarChart chart = new();

        Assert.Null(chart.Values);
        Assert.Null(chart.AxisLabels);
    }

    /// <summary>一个顶点到圆心的距离。</summary>
    private static double DistanceFromCentre(Point vertex) =>
        Math.Sqrt(Math.Pow(vertex.X - _centre.X, 2d) + Math.Pow(vertex.Y - _centre.Y, 2d));

    /// <summary>
    /// 读一个 <see cref="RadarChart"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(RadarChart chart)
    {
        PropertyInfo hasData = typeof(RadarChart).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("RadarChart 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
