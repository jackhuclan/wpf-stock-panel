using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Tsp.Desktop.Charts;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="DonutChart"/> 的分片角度换算（<c>ComputeSlices</c>），外加控件自身几条可离线断言的契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，所以 <c>RenderChart</c> 里画的每一笔在本工程里<b>都断言不了</b>
/// ——「分片色为 <c>null</c> 时不抛异常」「环真的画出来了」只能靠实机验证。
/// 于是占比那段算术压进 <see cref="DonutChart.ComputeSlices"/>（本文件），
/// 极坐标换算压进 <c>PolarGeometry</c>（<c>PolarGeometryTests</c>），
/// <c>RenderChart</c> 只剩下「取哪一段、按什么颜色画、按什么顺序画」。
/// </para>
/// <para>
/// <b>本文件覆盖</b>：整圈之和、空输入、全 0 的除零、单项占满整圈、负值与非有限值的处置、
/// 下标对齐（颜色不因别项变成 0 而整体错位）、首尾相接、总量溢出的兜底、
/// <c>HasData</c> 的口径（非空**且至少一个有限值**）、三个 <c>StyledProperty</c> 的注册。
/// <b>不覆盖</b>任何绘制行为（环画在哪、内侧半径多大、分片色真取到了哪一支、弧有没有画对方向）
/// ——那部分只有实机验证一条路。
/// </para>
/// <para>
/// 角度一律以<b>弧度</b>计、<c>0</c> 是 12 点方向、顺时针为正（与 <c>PolarGeometry</c> 同一口径）。
/// </para>
/// </remarks>
public sealed class DonutSliceTests
{
    /// <summary>一整圈，弧度。</summary>
    private const double FullTurn = 2d * Math.PI;

    /// <summary>浮点比较的容差：占比是一串 值/总量 的乘法与累加，不可能精确到 0。</summary>
    private const double Tolerance = 1e-12d;

    [Fact]
    public void Slices_sum_to_a_full_circle_within_floating_point_tolerance()
    {
        // 一整圈必须被分完：漏掉一截会在环上留一条缝，超出去则会与下一段叠在一起——
        // 两者都在"占比"这件事上直接撒谎，而看图的人未必看得出是 1% 还是 3% 的错。
        double[] values = [1d, 2d, 3d, 4d];

        IReadOnlyList<(double StartAngle, double SweepAngle)> slices = DonutChart.ComputeSlices(values);

        Assert.Equal(values.Length, slices.Count);
        Assert.Equal(0d, slices[0].StartAngle);
        Assert.Equal(FullTurn, slices.Sum(slice => slice.SweepAngle), Tolerance);

        // 每一段的占比就是它的值除以总量：1/10、2/10、3/10、4/10 圈。
        double[] shares = [0.1d, 0.2d, 0.3d, 0.4d];

        for (int i = 0; i < slices.Count; i++)
        {
            Assert.Equal(shares[i], slices[i].SweepAngle / FullTurn, Tolerance);
        }

        // 首尾相接：后一段的起点**就是**前一段的起点加跨度（同一个累加变量，不是另算一次），
        // 所以这里可以断言精确相等。
        for (int i = 1; i < slices.Count; i++)
        {
            Assert.Equal(slices[i - 1].StartAngle + slices[i - 1].SweepAngle, slices[i].StartAngle);
        }
    }

    [Fact]
    public void Empty_input_returns_no_slices()
    {
        // 空序列没有"总量"可言，占比是 0/0。返回空表让渲染方直接收手，而不是给出一串毒值。
        Assert.Empty(DonutChart.ComputeSlices([]));
        Assert.Empty(DonutChart.ComputeSlices(null!));
    }

    [Fact]
    public void All_zero_values_return_no_slices_rather_than_dividing_by_zero()
    {
        // 总量为 0 时每一段占比都是 0/0 = NaN。NaN 一旦进了角度，整块环图的每一个端点一起变成
        // NaN——什么都画不出来、还不抛异常，是最难查的那一类。这里返回空表：
        // "没有什么可画"与"有一段弧，只是它没有长度"在读图上是一回事，在调用方那里不是。
        Assert.Empty(DonutChart.ComputeSlices([0d, 0d, 0d]));
        Assert.Empty(DonutChart.ComputeSlices([0d]));
    }

    [Fact]
    public void A_single_value_occupies_the_whole_circle()
    {
        // 一项就是全部：跨度必须正好是 2π，而不是"接近 2π"——它同时是**整圆画法**的入口
        // （首尾端点重合，单条 ArcTo 会画成空，得靠拆分，见 BuildSlice）。
        (double start, double sweep) = Assert.Single(DonutChart.ComputeSlices([7d]));

        Assert.Equal(0d, start);
        Assert.Equal(FullTurn, sweep);
    }

    [Fact]
    public void Negative_values_are_ignored_not_reversed()
    {
        // 负值在环图里没有可表达的形式：反向画一段弧会与正值的弧叠在一起，把总占比读错。
        // 口径是"非正 = 没有量"，占 0 角度——**不是**倒着走一段。
        double[] values = [-5d, 3d, -1d];

        IReadOnlyList<(double StartAngle, double SweepAngle)> slices = DonutChart.ComputeSlices(values);

        Assert.Equal(3, slices.Count);
        Assert.Equal(0d, slices[0].SweepAngle);
        Assert.Equal(FullTurn, slices[1].SweepAngle);
        Assert.Equal(0d, slices[2].SweepAngle);

        // 没有任何一段是负跨度（"反向画"的形态），总和仍是一整圈。
        Assert.All(slices, slice => Assert.True(slice.SweepAngle >= 0d, $"跨度成了 {slice.SweepAngle}。"));
        Assert.Equal(FullTurn, slices.Sum(slice => slice.SweepAngle), Tolerance);
    }

    [Fact]
    public void All_negative_values_return_no_slices()
    {
        // 全负：总量为 0，"总量占比"没有意义，一片都不画（与全 0 同一条退路）。
        Assert.Empty(DonutChart.ComputeSlices([-1d, -2d]));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_are_ignored_like_negatives(double badValue)
    {
        // 非有限值（指标预热期的 NaN、坏点带来的 ±Infinity）与负值同一条口径：没有量，占 0 角度。
        // 但不能让一个坏点毁掉整块环图——若让它参与总量，占比会一起变成 NaN。
        double[] values = [badValue, 2d, 2d];

        IReadOnlyList<(double StartAngle, double SweepAngle)> slices = DonutChart.ComputeSlices(values);

        Assert.Equal(3, slices.Count);
        Assert.Equal(0d, slices[0].SweepAngle);
        Assert.Equal(FullTurn / 2d, slices[1].SweepAngle, Tolerance);
        Assert.Equal(FullTurn / 2d, slices[2].SweepAngle, Tolerance);
        Assert.All(slices, slice => Assert.True(double.IsFinite(slice.StartAngle) && double.IsFinite(slice.SweepAngle)));
    }

    [Fact]
    public void Slices_keep_one_entry_per_value_so_colours_stay_aligned()
    {
        // 分片色是按下标与数据对齐的（SliceBrushes）。非正值若从结果里被剔掉，后面每一项的颜色
        // 都会往前挪一格——同一份数据里某个分项从正变 0，其余分片的颜色会集体换一次。
        // 长度与下标对齐，是让"颜色跟着分项走"成为结构上的必然。
        IReadOnlyList<(double StartAngle, double SweepAngle)> slices = DonutChart.ComputeSlices([0d, 4d, 0d, 4d, -3d]);

        Assert.Equal(5, slices.Count);
        Assert.Equal(0d, slices[0].SweepAngle);
        Assert.Equal(FullTurn / 2d, slices[1].SweepAngle, Tolerance);
        Assert.Equal(0d, slices[2].SweepAngle);
        Assert.Equal(FullTurn / 2d, slices[3].SweepAngle, Tolerance);
        Assert.Equal(0d, slices[4].SweepAngle);

        // 后两项起点相同：中间那两段零跨度的项不占角度，但仍然各占一个下标。
        Assert.Equal(slices[2].StartAngle, slices[3].StartAngle);
        Assert.Equal(slices[4].StartAngle, slices[3].StartAngle + slices[3].SweepAngle);
    }

    [Fact]
    public void A_total_that_overflows_returns_no_slices()
    {
        // 量级逼近 double.MaxValue 的几项相加会把总量溢出成 +Infinity，于是每一段占比都是 0——
        // 画出来是一个空环。返回空表，让"画不出来"这件事在调用方那里是明确的，而不是一片空白。
        Assert.Empty(DonutChart.ComputeSlices([double.MaxValue, double.MaxValue]));
    }

    [Fact]
    public void SlicePalette_prefers_the_page_supplied_brushes()
    {
        // 页面 / 样式给了分片色就用它，一支都不动、也不与基座那三支混在一起——
        // 混起来会让"我给了五个颜色，画出来却有八个"这种事变得没法解释。
        // （下面这几支 Brushes.* 是**测试用的身份标记**，不是配色：本仓库"配色只在样式库里出现"
        // 那条纪律管的是图表源码，而这里只是要三个可区分的对象来验证挑选与顺序。）
        IReadOnlyList<IBrush> supplied = [Brushes.Black, Brushes.White];

        IReadOnlyList<IBrush> palette = DonutChart.SlicePalette(supplied, Brushes.Gray, Brushes.Gray, Brushes.Gray);

        Assert.Equal(supplied, palette);
    }

    [Fact]
    public void SlicePalette_falls_back_to_the_three_base_brushes_in_order()
    {
        // 没给分片色时退回基座那三支（主序列色 / 次序列色 / 中性色），顺序固定——
        // 顺序变了，同一份数据的分片颜色会整体轮换，而画面看着依然"是个环"。
        IReadOnlyList<IBrush> palette = DonutChart.SlicePalette(null, Brushes.Black, Brushes.White, Brushes.Gray);

        Assert.Equal<IReadOnlyList<IBrush>>([Brushes.Black, Brushes.White, Brushes.Gray], palette);
    }

    [Fact]
    public void SlicePalette_skips_missing_base_brushes_and_goes_empty_when_nothing_is_left()
    {
        // 样式没给的那几支是 null，跳过它们、剩下的照样用：只给了一支色的页面得到一块单色环。
        // 三支全空才是"这一层不画"——那时返回空表，环图的渲染方据此收手。
        Assert.Equal<IReadOnlyList<IBrush>>([Brushes.White], DonutChart.SlicePalette(null, null, Brushes.White, null));
        Assert.Empty(DonutChart.SlicePalette(null, null, null, null));
        Assert.Empty(DonutChart.SlicePalette([], null, null, null));
    }

    [Fact]
    public void DonutChart_has_data_only_when_at_least_one_value_is_finite()
    {
        // HasData 是基座用来做"空数据短路"的钩子：为 false 时底色、边框一概不画。
        // 判据与其余图表统一（计划全局约束）：**非空不够，必须至少有一个有限值**。
        //
        // 有一处环图特有的后果已写在 HasData 的文档里：全是 0 或全是负数时它有数据、却一片也画不出来
        // （ComputeSlices 返回空表），画面是一片空白面板——面板本身表示"这里有一个环图"。
        // 这里把那个口径也钉住，免得日后有人"顺手"把它改成"有正数才算有数据"。
        DonutChart chart = new();

        Assert.False(HasDataOf(chart));

        chart.Values = [];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, double.PositiveInfinity];
        Assert.False(HasDataOf(chart));

        chart.Values = [0d];
        Assert.True(HasDataOf(chart));

        chart.Values = [-1d, -2d];
        Assert.True(HasDataOf(chart));

        chart.Values = [double.NaN, 5d];
        Assert.True(HasDataOf(chart));

        chart.Values = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void DonutChart_declares_values_slice_brushes_and_inner_radius_as_styled_properties()
    {
        // 三者都必须是 StyledProperty 且注册在 DonutChart 自己名下：数据、分片色、环宽改了就重绘，
        // 靠的是静态构造里的 AffectsRender<DonutChart>(…)。
        // **分片色这条尤其要紧**：它是"派生控件追加的画笔"里唯一一个集合类型的，
        // ChartsSourceGuardTests 的画笔判据专门为它放宽过（名字以 BrushesProperty 收尾）。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<double>?>>(DonutChart.ValuesProperty);
        Assert.Equal(typeof(IReadOnlyList<double>), DonutChart.ValuesProperty.PropertyType);
        Assert.Equal(nameof(DonutChart.Values), DonutChart.ValuesProperty.Name);
        Assert.Equal(typeof(DonutChart), DonutChart.ValuesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<IBrush>?>>(DonutChart.SliceBrushesProperty);
        Assert.Equal(typeof(IReadOnlyList<IBrush>), DonutChart.SliceBrushesProperty.PropertyType);
        Assert.Equal(nameof(DonutChart.SliceBrushes), DonutChart.SliceBrushesProperty.Name);
        Assert.Equal(typeof(DonutChart), DonutChart.SliceBrushesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<double>>(DonutChart.InnerRadiusRatioProperty);
        Assert.Equal(typeof(double), DonutChart.InnerRadiusRatioProperty.PropertyType);
        Assert.Equal(nameof(DonutChart.InnerRadiusRatio), DonutChart.InnerRadiusRatioProperty.Name);
        Assert.Equal(typeof(DonutChart), DonutChart.InnerRadiusRatioProperty.OwnerType);
    }

    [Fact]
    public void DonutChart_is_a_sealed_chart_surface_with_the_documented_defaults()
    {
        // 无 UI 线程下能被构造出来，说明 DonutChart 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(DonutChart).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(DonutChart).BaseType);

        DonutChart chart = new();

        // 新控件还没数据；分片色未赋值是合法状态，含义是"这一层不画"（配色只在样式库里出现）。
        Assert.Null(chart.Values);
        Assert.Null(chart.SliceBrushes);

        // 默认环宽比 0.6：一个看得出"环"、也留得住中心区域的厚度。
        Assert.Equal(0.6d, chart.InnerRadiusRatio);
    }

    /// <summary>
    /// 读一个 <see cref="DonutChart"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(DonutChart chart)
    {
        PropertyInfo hasData = typeof(DonutChart).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("DonutChart 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
