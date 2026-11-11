using Avalonia;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="ColumnLayout"/> 的三条共用规则：横向槽位、从锚点向外取整的跨度、基线行号。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独有一份用例。</b>这三条规则原本长在 <c>BarChart.LayoutBar</c> 里、由
/// <c>BarLayoutTests</c> 从外面盖住；Task 10b 把它们搬到这里给 K 线共用，于是它们有了<b>两个</b>调用方
/// （柱状图与 K 线的实体 / 成交量柱），该有自己的用例——<c>BarLayoutTests</c> 证明的是"搬家没漂移"，
/// 而<b>规则本身</b>（尤其是槽位的取整与间距夹紧、以及非有限入参那一条）需要直接钉住，
/// 否则将来改 K 线那边时没人拦得住。
/// </para>
/// <para>
/// 绘图区沿用 <see cref="ChartSurfaceContractTests"/> 里那个 <c>(8, 8, 84, 34)</c>：
/// 左 8、上 8、右 92、下 42、垂直中心 25。
/// </para>
/// </remarks>
public sealed class ColumnLayoutTests
{
    /// <summary>默认内边距下 100×50 控件的绘图区：左 8、上 8、右 92、下 42。</summary>
    private static readonly Rect _plotArea = new(8d, 8d, 84d, 34d);

    [Fact]
    public void Adjacent_slots_share_exactly_one_edge()
    {
        // 两条边各自独立取整 ⇒ 相邻两根柱相邻的那条边由同一个 Round 得到同一个整数，
        // "不重叠"是结构上的必然。不留白时前后两根应当正好挨着（8 → 36 → 64 → 92）。
        (double firstLeft, double firstRight) = ColumnLayout.Slot(0, 3, _plotArea, 0d);
        (double secondLeft, double secondRight) = ColumnLayout.Slot(1, 3, _plotArea, 0d);
        (double thirdLeft, double thirdRight) = ColumnLayout.Slot(2, 3, _plotArea, 0d);

        Assert.Equal(8d, firstLeft);
        Assert.Equal(firstRight, secondLeft);
        Assert.Equal(secondRight, thirdLeft);
        Assert.Equal(_plotArea.Right, thirdRight);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.2d)]
    [InlineData(0.9d)]
    [InlineData(-1d)]           // 夹到 0
    [InlineData(5d)]            // 夹到 1（零宽）
    [InlineData(double.NaN)]    // 按 0 处理
    public void Slots_never_overlap_and_never_leave_the_plot_area(double gapRatio)
    {
        // 间距是样式/页面给的，没人保证它在 [0, 1] 里。夹紧之后：不重叠、不越框、宽度不为负。
        const int Count = 6;
        (double left, double right) previous = default;

        for (int i = 0; i < Count; i++)
        {
            (double left, double right) = ColumnLayout.Slot(i, Count, _plotArea, gapRatio);

            Assert.True(right >= left, $"间距 {gapRatio} 下第 {i} 根槽宽度成了负的。");
            Assert.InRange(left, _plotArea.Left, _plotArea.Right);
            Assert.InRange(right, _plotArea.Left, _plotArea.Right);

            if (i > 0)
            {
                Assert.True(left >= previous.right, $"间距 {gapRatio} 下第 {i} 根槽压住了第 {i - 1} 根。");
            }

            previous = (left, right);
        }
    }

    [Fact]
    public void Slots_are_whole_pixels()
    {
        // 槽位是填充的边界，必须落在整像素上——否则相邻两格的半透明覆盖各铺一半，缝就出来了。
        Rect fractional = new(8.3d, 8.3d, 84d, 34d);

        for (int i = 0; i < 5; i++)
        {
            (double left, double right) = ColumnLayout.Slot(i, 5, fractional, 0.2d);

            Assert.True(IsWholePixel(left) && IsWholePixel(right), $"第 {i} 根槽的边界不在整像素上：({left}, {right})。");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_count_yields_a_zero_width_slot_without_dividing_by_zero(int count)
    {
        // 调用方本该先判空（HasData 管这件事），但一个走错路的调用方不该拿到毒值：
        // 槽宽 = 绘图区宽度 / 0 会得到 Infinity，再往下就是一条无限宽的柱。
        (double left, double right) = ColumnLayout.Slot(0, count, _plotArea, 0.2d);

        Assert.Equal(_plotArea.X, left);
        Assert.Equal(left, right);
    }

    [Fact]
    public void A_zero_width_plot_area_yields_a_zero_width_slot()
    {
        (double left, double right) = ColumnLayout.Slot(0, 3, new Rect(8d, 8d, 0d, 0d), 0.2d);

        Assert.Equal(8d, left);
        Assert.Equal(left, right);
    }

    [Theory]
    [InlineData(10d, 10.4d, 10d, 11d)]      // 半个像素的跨度也要占满一整行
    [InlineData(10d, 10d, 10d, 10d)]        // 零跨度就是零高，那是实话
    [InlineData(25d, 8d, 8d, 25d)]          // 参数顺序不影响结果（基线可能在任一端）
    [InlineData(8d, 42d, 8d, 42d)]          // 两端已经是整数时原地不动
    [InlineData(8.2d, 41.7d, 8d, 42d)]      // 向外取整：低端向下、高端向上
    public void SnapSpan_rounds_outward(double firstY, double secondY, double expectedTop, double expectedBottom)
    {
        (double top, double bottom) = ColumnLayout.SnapSpan(firstY, secondY);

        Assert.Equal(expectedTop, top);
        Assert.Equal(expectedBottom, bottom);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SnapSpan_gives_NaN_for_both_ends_when_either_input_is_not_finite(double badY)
    {
        // **两端一起给 NaN，不是"原样透传"**：SnapSpan(25, +Infinity) 若透传就得到 (25, +Infinity)，
        // 上端是有限的——于是"判 Top / 判 Y 就能认出这一段没有落点"这条到处在用的判据在这里失效
        // （只看 Y 的调用方会拿一个 Y = 25 的矩形去画一段高 Infinity 的填充）。
        (double top, double bottom) = ColumnLayout.SnapSpan(25d, badY);

        Assert.False(double.IsFinite(top) && double.IsFinite(bottom), $"({top}, {bottom}) 里出现了有限的落点。");

        (double otherTop, double otherBottom) = ColumnLayout.SnapSpan(badY, 25d);

        Assert.False(double.IsFinite(otherTop) && double.IsFinite(otherBottom));
    }

    [Fact]
    public void BaselineY_sits_where_zero_is_and_snaps_to_a_whole_pixel()
    {
        // 值域含 0 时基线落在 0 的位置（窗口 (-1, 1) → 绘图区正中 25）。
        Assert.Equal(25d, ColumnLayout.BaselineY(new ValueRange(-1d, 1d), _plotArea));

        // 非整格的绘图区：基线仍然要落在整像素上——柱贴基线的那一端不能骑在两个像素之间。
        double fractional = ColumnLayout.BaselineY(new ValueRange(-1d, 1d), new Rect(8.3d, 8.3d, 84d, 34d));

        Assert.True(IsWholePixel(fractional), $"基线成了 {fractional}。");
    }

    [Fact]
    public void BaselineY_clamps_onto_the_near_edge_when_zero_is_outside_the_range()
    {
        // 全负数据：0 在值域上方 → 夹到顶边；全正数据：夹到底边。夹紧之后柱照样从那条边量起，
        // 而不是整块图什么都画不出来。
        Assert.Equal(_plotArea.Top, ColumnLayout.BaselineY(new ValueRange(-3d, -1d), _plotArea));
        Assert.Equal(_plotArea.Bottom, ColumnLayout.BaselineY(new ValueRange(1d, 3d), _plotArea));
    }

    [Fact]
    public void BaselineY_on_a_degenerate_range_lands_in_the_vertical_centre()
    {
        // 零跨度的值域里 YFor 把一切值映到垂直正中，基线也在那里——不抛异常、不做零除。
        Assert.Equal(25d, ColumnLayout.BaselineY(new ValueRange(5d, 5d), _plotArea));
    }

    /// <summary>一个坐标是不是落在整像素上（有限且小数部分为 0）。</summary>
    private static bool IsWholePixel(double coordinate) =>
        double.IsFinite(coordinate) && Math.Floor(coordinate) == coordinate;
}
