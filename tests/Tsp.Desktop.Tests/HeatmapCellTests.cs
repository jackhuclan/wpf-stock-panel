using System.Reflection;
using Avalonia;
using Tsp.Desktop.Charts;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="Heatmap"/> 的铺格子与归一化（<c>LayoutCells</c>），外加控件自身几条可离线断言的契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，所以 <c>RenderChart</c> 里画的每一笔在本工程里<b>都断言不了</b>
/// ——「深浅真的按值排了」「格子之间真的没缝」只能靠实机验证。于是铺格与归一化压进
/// <see cref="Heatmap.LayoutCells"/>（本文件），<c>RenderChart</c> 只剩下
/// 「取哪一格、按什么深浅画、按什么顺序画」。
/// </para>
/// <para>
/// <b>本文件覆盖</b>：无缝无叠地铺满、行数与最后一行的空位、最小 / 最大映到 0 / 1、
/// 全等值的中间值、跨度溢出的退路、越界列数、空输入、非有限值留白且位置不动、
/// 整像素对齐、零尺寸绘图区、<c>HasData</c> 的口径、两个 <c>StyledProperty</c> 的注册。
/// <b>不覆盖</b>任何绘制行为（深浅真的画出来了、不透明度映射对不对）——那部分只有实机验证一条路。
/// </para>
/// <para>
/// 绘图区沿用 <see cref="ChartSurfaceContractTests"/> 里那个 <c>(8, 8, 84, 34)</c>
/// （默认内边距 8 下 100×50 的控件对应的绘图区）：左 8、上 8、右 92、下 42。
/// 三列时格宽恰好 28，三行时格高 34/3 ≈ 11.33（取整后 11 / 12 / 11），都是好核对的数。
/// </para>
/// </remarks>
public sealed class HeatmapCellTests
{
    /// <summary>默认内边距下 100×50 控件的绘图区：左 8、上 8、右 92、下 42。</summary>
    private static readonly Rect _plotArea = new(8d, 8d, 84d, 34d);

    private const double Tolerance = 1e-12d;

    [Fact]
    public void Cells_tile_the_plot_area_without_gaps_or_overlap()
    {
        // 相邻两格共用一条边时，那条边若落在小数坐标上，两格的半透明覆盖各自只铺一半，
        // 合成之后会留下一道比两侧都暗的缝——一张满屏都是缝的热力图。所以两条边从**同一个表达式**
        // 出发各自取整，右边界与下一格的左边界算出来必然是同一个整数。
        double[] values = [1d, 2d, 3d, 4d, 5d, 6d, 7d];
        const int Columns = 3;

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells(values, Columns, _plotArea);

        Assert.Equal(values.Length, cells.Count);

        // 每一格都在框里，且四条边都落在整像素上。
        foreach ((Rect cell, _) in cells)
        {
            Assert.InRange(cell.Left, _plotArea.Left, _plotArea.Right);
            Assert.InRange(cell.Right, _plotArea.Left, _plotArea.Right);
            Assert.InRange(cell.Top, _plotArea.Top, _plotArea.Bottom);
            Assert.InRange(cell.Bottom, _plotArea.Top, _plotArea.Bottom);

            Assert.True(IsWholePixel(cell.Left) && IsWholePixel(cell.Right), $"横边没落在整像素上：{cell}。");
            Assert.True(IsWholePixel(cell.Top) && IsWholePixel(cell.Bottom), $"纵边没落在整像素上：{cell}。");
        }

        // 两两不重叠（"相接"不算重叠——它们共用一条边，那是本用例要的形状）。
        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                Assert.False(cells[i].Cell.Intersects(cells[j].Cell), $"第 {i} 格与第 {j} 格重叠了。");
            }
        }

        // 同一行里严丝合缝，且铺满整行：8 → 36 → 64 → 92。
        Assert.Equal(8d, cells[0].Cell.Left);
        Assert.Equal(cells[0].Cell.Right, cells[1].Cell.Left);
        Assert.Equal(cells[1].Cell.Right, cells[2].Cell.Left);
        Assert.Equal(_plotArea.Right, cells[2].Cell.Right);

        // 行与行同样严丝合缝，纵向铺满：8 → 19 → 31 → 42。
        Assert.Equal(19d, cells[0].Cell.Bottom);
        Assert.Equal(cells[0].Cell.Bottom, cells[3].Cell.Top);
        Assert.Equal(cells[3].Cell.Bottom, cells[6].Cell.Top);
        Assert.Equal(_plotArea.Bottom, cells[6].Cell.Bottom);

        // 最后一行只有一个元素（第 7 个在下标 6），它右边的空位**不产出格子**——
        // 空位不是"一个值为 0 的格子"，它根本不存在。
        Assert.Equal(new Rect(8d, 31d, 28d, 11d), cells[6].Cell);
        Assert.DoesNotContain(cells, cell => cell.Cell == new Rect(36d, 31d, 28d, 11d));
    }

    [Fact]
    public void Normalized_value_is_zero_for_the_minimum_and_one_for_the_maximum()
    {
        // 归一化的尺子就是这批数据自己：最小 0、最大 1。热力图回答的是"在这批里谁高谁低"。
        double[] values = [5d, 7d, 9d, 1d];

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells(values, 4, _plotArea);

        Assert.Equal(4, cells.Count);
        Assert.Equal(0.5d, cells[0].Normalized, Tolerance);
        Assert.Equal(0.75d, cells[1].Normalized, Tolerance);
        Assert.Equal(1d, cells[2].Normalized, Tolerance);
        Assert.Equal(0d, cells[3].Normalized, Tolerance);
    }

    [Fact]
    public void Degenerate_range_normalizes_every_cell_to_one_half()
    {
        // 全等值（停牌、单因子横截面全同分）没有高低可言，一律给中间值：
        // 那是"看不出差别"在这块画布上唯一说得通的位置，也避开了 0/0 = NaN。
        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells([3d, 3d, 3d, 3d], 2, _plotArea);

        Assert.Equal(4, cells.Count);
        Assert.All(cells, cell => Assert.Equal(0.5d, cell.Normalized, Tolerance));
    }

    [Fact]
    public void An_overflowing_span_normalizes_every_cell_to_one_half_too()
    {
        // 跨度本身溢出成 +Infinity 时（[-double.MaxValue, double.MaxValue]），
        // (值 - 最小) / 跨度 算出来是 Infinity / Infinity = NaN，而负载 NaN 的不透明度会让整格消失。
        // 折成中间值，与 ChartHitTest.YFor 对非有限跨度的处置是同一条退路。
        double[] values = [double.MinValue, 0d, double.MaxValue];

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells(values, 3, _plotArea);

        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell => Assert.Equal(0.5d, cell.Normalized, Tolerance));
        Assert.All(cells, cell => Assert.True(double.IsFinite(cell.Normalized)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Columns_below_one_are_rejected(int columns)
    {
        // 夹成 1 列会把"列数忘设了"这个笔误静默成一张一列的长条——那形状看着像一个正经结果。
        // 拒绝则让它在画面上明确地空着（HasData 的备注写了这一条）。它同时是这里的除零防线：
        // 下标 / 0 会得到 Infinity 行号。
        Assert.Empty(Heatmap.LayoutCells([1d, 2d, 3d], columns, _plotArea));
    }

    [Fact]
    public void Empty_input_returns_no_cells()
    {
        Assert.Empty(Heatmap.LayoutCells([], 3, _plotArea));
        Assert.Empty(Heatmap.LayoutCells(null!, 3, _plotArea));
    }

    [Fact]
    public void Non_finite_values_leave_their_slot_empty_without_shifting_the_rest()
    {
        // 非有限值是"这一格没有值"：不画，但**位置留着**。每个格子的行列只由它自己的下标算出，
        // 与别人无关，所以跳过一格不会让后面整体前移——一条中间缺数的序列，热的形状仍然落在对的位置上。
        // 把它画成最小色或中间色，都是在替数据编一个它没有的读数。
        double[] values = [1d, double.NaN, 3d, 4d];

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells(values, 2, _plotArea);

        Assert.Equal(3, cells.Count);

        // 下标 0、2、3 留下了；下标 1（NaN）没留下，但下标 2 仍在第二行第一格。
        Assert.Equal(new Rect(8d, 8d, 42d, 17d), cells[0].Cell);
        Assert.Equal(new Rect(8d, 25d, 42d, 17d), cells[1].Cell);
        Assert.Equal(new Rect(50d, 25d, 42d, 17d), cells[2].Cell);
        Assert.DoesNotContain(cells, cell => cell.Cell == new Rect(50d, 8d, 42d, 17d));
    }

    [Fact]
    public void Cells_snap_onto_whole_pixels_even_when_the_plot_area_does_not()
    {
        // 整格绘图区上取整是空操作，看不出取整有没有发生——所以用一块**非整格**的绘图区来验。
        // 这也是"格子之间不留缝"的前提：两条边都落在整像素上，相邻两格才会严丝合缝。
        Rect fractional = new(8.3d, 8.3d, 84d, 34d);

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells([1d, 2d, 3d, 4d], 2, fractional);

        Assert.Equal(4, cells.Count);

        foreach ((Rect cell, _) in cells)
        {
            Assert.True(IsWholePixel(cell.Left) && IsWholePixel(cell.Right), $"横边没落在整像素上：{cell}。");
            Assert.True(IsWholePixel(cell.Top) && IsWholePixel(cell.Bottom), $"纵边没落在整像素上：{cell}。");
        }

        // 相邻仍然严丝合缝：取整没有把两格推开，也没有让它们叠上。
        Assert.Equal(cells[0].Cell.Right, cells[1].Cell.Left);
        Assert.Equal(cells[0].Cell.Bottom, cells[2].Cell.Top);
    }

    [Fact]
    public void A_zero_sized_plot_area_yields_zero_sized_cells_without_throwing()
    {
        // 控件还没排过版（或比两倍内边距还小）时绘图区尺寸为 0，含义是"这块地方画不出来"。
        // **收手是渲染方的职责**（RenderChart 在尺寸不大于 0 时直接返回）：LayoutCells 保持成一个
        // 纯粹、无分支的映射——零宽高把格子压成零尺寸，不抛异常，也不替调用方做政策决定。
        Rect zeroSized = new(8d, 8d, 0d, 0d);

        IReadOnlyList<(Rect Cell, double Normalized)> cells = Heatmap.LayoutCells([1d, 2d, 3d], 3, zeroSized);

        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Equal(0d, cell.Cell.Width);
            Assert.Equal(0d, cell.Cell.Height);
            Assert.True(double.IsFinite(cell.Normalized));
        });
    }

    [Fact]
    public void Heatmap_has_data_only_when_at_least_one_value_is_finite()
    {
        // HasData 是基座用来做"空数据短路"的钩子：为 false 时底色、边框一概不画。
        // 判据与其余图表统一（计划全局约束）：**非空不够，必须至少有一个有限值**。
        //
        // 另有一处热力图特有的后果（已写进 HasData 的文档）：列数没设（默认 0）时有数据却排不出格子，
        // 画面是一块空面板——面板本身表示"这里有一张热力图，只是它排不出来"。
        Heatmap chart = new();

        Assert.False(HasDataOf(chart));

        chart.Values = [];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, double.PositiveInfinity];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, 1d];
        Assert.True(HasDataOf(chart));

        // 有数据、但列数没设：HasData 为 true（有有限值），却一格都排不出来——两件事分开判。
        chart.Values = [1d, 2d];
        Assert.True(HasDataOf(chart));
        Assert.Equal(0, chart.Columns);
        Assert.Empty(Heatmap.LayoutCells(chart.Values, chart.Columns, _plotArea));

        chart.Values = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void Heatmap_declares_values_and_columns_as_styled_properties()
    {
        // 两者都必须是 StyledProperty 且注册在 Heatmap 自己名下：数据与列数改了就重绘，
        // 靠的是静态构造里的 AffectsRender<Heatmap>(…)。列数是几何参数，同样必须登记——
        // 它不像画笔，漏登记在运行期的表现是"改了列数图不动"。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<double>?>>(Heatmap.ValuesProperty);
        Assert.Equal(typeof(IReadOnlyList<double>), Heatmap.ValuesProperty.PropertyType);
        Assert.Equal(nameof(Heatmap.Values), Heatmap.ValuesProperty.Name);
        Assert.Equal(typeof(Heatmap), Heatmap.ValuesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<int>>(Heatmap.ColumnsProperty);
        Assert.Equal(typeof(int), Heatmap.ColumnsProperty.PropertyType);
        Assert.Equal(nameof(Heatmap.Columns), Heatmap.ColumnsProperty.Name);
        Assert.Equal(typeof(Heatmap), Heatmap.ColumnsProperty.OwnerType);
    }

    [Fact]
    public void Heatmap_is_a_sealed_chart_surface_with_no_columns_by_default()
    {
        // 无 UI 线程下能被构造出来，说明 Heatmap 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(Heatmap).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(Heatmap).BaseType);

        Heatmap chart = new();

        Assert.Null(chart.Values);

        // "每行几格"是页面才知道的事（几个概念、几个行业），没有哪个默认值能替它猜对。
        // 所以默认是 0（排不出格子），让"忘了设"在画面上看得见，而不是悄悄排成某个别扭的形状。
        Assert.Equal(0, chart.Columns);
    }

    /// <summary>一个坐标是不是落在整像素上（有限且小数部分为 0）。</summary>
    private static bool IsWholePixel(double coordinate) =>
        double.IsFinite(coordinate) && Math.Floor(coordinate) == coordinate;

    /// <summary>
    /// 读一个 <see cref="Heatmap"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(Heatmap chart)
    {
        PropertyInfo hasData = typeof(Heatmap).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("Heatmap 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
