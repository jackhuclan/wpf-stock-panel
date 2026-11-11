using System.Reflection;
using Avalonia;
using Tsp.Desktop.Charts;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="LineChart"/> 的折线与边框算术：三个纯静态函数，外加控件自身几条可离线断言的契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈不含 Avalonia Headless（P0 已裁定不引入），
/// <c>DrawingContext</c> 的构造函数不可访问，所以 <c>RenderChart</c> 里画的每一笔在本工程里
/// <b>都断言不了</b>——「画笔为 <c>null</c> 时不抛异常」「FillArea 真的填了色」只能靠 Task 12 实机验证。
/// 于是 Task 6 把整条链压进三个纯函数：<see cref="LineChart.ComputeWindow"/>（数据 → 可画窗口 + 刻度）、
/// <see cref="LineChart.BuildPoints"/>（下标 → x、值 → y、跳过不可绘制的点）、
/// <see cref="LineChart.GridLineYs"/>（刻度 → 网格行号 + 像素对齐），让<b>能测的部分承担全部验证负担</b>；
/// <c>RenderChart</c> 只剩下「取哪个值、按画笔是否为 <c>null</c> 决定画不画、按什么顺序画」。
/// </para>
/// <para>
/// <b>边界写清楚，免得日后把「这里没测」误读成「这里没问题」</b>：
/// 本文件覆盖上面三个纯函数（含空 / 单点 / 全等值 / 非有限值 / 全负数 / 极端量级 / 零尺寸绘图区）、
/// 「窗口覆盖数据且与网格线共用」「网格行号落在整数 + 0.5 上」这两条不变量、
/// <c>HasData</c> 的口径（非空**且至少一个有限值**）、以及两个 <c>StyledProperty</c> 的注册；
/// <b>不覆盖</b>任何绘制行为（画笔为 null、FillArea 的填充、线真的画在哪、有没有真的变锐）——
/// 那部分只有实机验证一条路。
/// </para>
/// <para>
/// 装饰用的绘图区沿用 <see cref="ChartSurfaceContractTests"/> 里那个 <c>(8, 8, 84, 34)</c>
/// （默认内边距 8 下 100×50 的控件对应的绘图区）：左 8、上 8、右 92、下 42、垂直中心 25。
/// 这里都是精确相等断言——端点之所以能精确，是因为 x 由 <c>i / (count - 1)</c> 算出，
/// <c>i</c> 取 <c>0</c> / <c>count - 1</c> 时 <c>t</c> 恰好是 <c>0</c> / <c>1</c>，而 <c>x = Left + t × Width</c>
/// 在 <c>t = 1</c> 时正好是 <c>Right</c>（与 <c>Rect.Right</c> 的算法同形）。中间那些点能否精确相等，
/// 各用例里单独说明。
/// </para>
/// </remarks>
public sealed class LineChartPathTests
{
    /// <summary>默认内边距下 100×50 控件的绘图区：左 8、上 8、右 92、下 42。</summary>
    private static readonly Rect _plotArea = new(8d, 8d, 84d, 34d);

    /// <summary>绘图区的垂直中心。零跨度的线应当落在这里。</summary>
    private const double PlotCentreY = 25d;

    [Fact]
    public void BuildPoints_maps_first_and_last_to_the_plot_edges()
    {
        // 首尾两点必须精确落在绘图区的左右边缘上：x 由下标算出，右边缘错半格就是整条曲线平移。
        // 值域取数据的实际值域时，最小值落在底边、最大值落在顶边（屏幕 y 向下、值轴向上）。
        double[] values = [1d, 2d, 3d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(3, points.Count);
        Assert.Equal(_plotArea.Left, points[0].X);
        Assert.Equal(_plotArea.Right, points[^1].X);
        Assert.Equal(_plotArea.Bottom, points[0].Y);
        Assert.Equal(_plotArea.Top, points[^1].Y);

        // 中间点也钉住：t = 1/2 与 0.5 都是二进制精确值，所以这里是精确相等，不是近似。
        Assert.Equal(new Point(50d, PlotCentreY), points[1]);
    }

    [Fact]
    public void BuildPoints_on_empty_input_returns_empty()
    {
        // 空序列取不到可画窗口（ValueRange.From 给的是兜底窗口 (0, 1)），此时不许抛异常，
        // 也不许凭空造点：返回空表，渲染方自然什么都画不出来。
        IReadOnlyList<Point> points = LineChart.BuildPoints([], ValueRange.From([]), _plotArea);

        Assert.Empty(points);
    }

    [Fact]
    public void BuildPoints_on_a_degenerate_range_centres_the_line()
    {
        // 全等值（停牌、单点、平平的一条线）的值域零跨度。横向要铺满绘图区，
        // 纵向必须落在正中——既不是贴顶 / 贴底，也不是越界。这是「零跨度绝不参与除法」那条规则
        // 在线图上的落点（ChartHitTest.YFor 的退化分支）。
        double[] values = [7d, 7d, 7d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(3, points.Count);
        Assert.All(points, point => Assert.Equal(PlotCentreY, point.Y));
        Assert.All(points, point =>
        {
            Assert.True(point.Y > _plotArea.Top && point.Y < _plotArea.Bottom, $"y 落在 {point.Y}，不是线内的正中。");
        });

        Assert.Equal(_plotArea.Left, points[0].X);
        Assert.Equal(_plotArea.Right, points[^1].X);
    }

    [Fact]
    public void BuildPoints_on_a_single_value_centres_it_horizontally()
    {
        // 只有一个点时 x 的公式 i / (count - 1) 会除零。约定：画在水平正中——
        // 与「零跨度落在垂直正中」同一个理由（没有跨度时，正中是唯一说得通的位置），
        // 而不是贴左边缘（那会让人误读成"时间轴上最早的第一个点"）。
        double[] values = [5d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Point point = Assert.Single(points);
        Assert.Equal(50d, point.X);
        Assert.Equal(PlotCentreY, point.Y);
    }

    [Fact]
    public void BuildPoints_skips_values_whose_y_is_not_finite()
    {
        // **本任务的核心义务**（Task 3 的契约）：YFor 对非有限值原样透传非有限结果，
        // 那是"这一点不可绘制"的信号——指标预热期会合法地产生 NaN。
        // 建几何之前必须跳过，判据是 !double.IsFinite(y)，**不是**"是否越出值域"：
        // NaN 与任何数比较都是 false，区间比较拦不住它；±Infinity 虽然确实越界，
        // 但"越界"和"没值"是两回事（尖峰本来就该如实跑到框外）。
        //
        // 造一个能同时打死两种错法的输入：
        //   1) 折成绘图区正中（"往中间掉的假线"）→ 下面那条 DoesNotContain(中心) 会红；
        //   2) 用区间比较代替 IsFinite → NaN 会带着出生坐标混进结果，Count 会变成 3 而不是 2。
        double[] values = [10d, double.NaN, double.PositiveInfinity, 30d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        // 首尾两个有限值各自成点，非有限的两个下标一个点都不产生。
        Assert.Equal(2, points.Count);
        Assert.Equal(new Point(_plotArea.Left, _plotArea.Bottom), points[0]);
        Assert.Equal(new Point(_plotArea.Right, _plotArea.Top), points[1]);

        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y), $"({point.X}, {point.Y}) 里有非有限坐标。");
        });

        // 没有任何点被折到绘图区正中——这正是"假线"的形态，必须能测出来。
        Assert.DoesNotContain(points, point => point.Y == PlotCentreY);
    }

    [Fact]
    public void BuildPoints_on_a_series_of_only_non_finite_values_returns_empty()
    {
        // 整条序列全是坏点（预热期还没结束就被画了一次）：一个点都不画，也不抛异常。
        // ValueRange.From 对全非有限输入给的是兜底窗口 (0, 1)，映射本身是好的——
        // 空表完全来自跳过逻辑，不是来自"值域不可用"。
        double[] values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Empty(points);
    }

    [Fact]
    public void BuildPoints_keeps_the_original_index_of_every_surviving_point()
    {
        // 跳过之后**剩下的点仍按原始下标定位**，不是被挤到一起：
        // x 的步长恒为 绘图区宽度 / (原序列长度 - 1)。
        //
        // 这条同时钉住"跨过缺口"的段语义（理由与代价见 BuildPoints 的 XML 注释：
        // 我们手上的缺口是**前缀**，两种画法在那里完全一样；而这是对通行折线图做法的一次刻意偏离，
        // 中段空洞时画出来的是连接两侧已知点的一条直段）。
        // 返回的是一个有序点列，渲染方照着连一条折线，于是被跳过的那个下标两侧的邻点之间
        // **会连出一条直段**（跳过去），而不是在那里断开。
        double[] values = [10d, double.NaN, 20d, 30d, 40d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(4, points.Count);

        // 8 = 下标 0（t = 0）、50 = 下标 2（t = 1/2）、71 = 下标 3（t = 3/4）、92 = 下标 4（t = 1）——
        // 全部是二进制精确值。若改按"压缩后的位置"铺开（4 个点均分 84），
        // 下标 2 会落到 8 + 84/3 = 36，这条断言立刻变红。
        Assert.Equal(new[] { 8d, 50d, 71d, 92d }, points.Select(point => point.X));

        // 缺口跨度 = 两个下标步（每步 84 / 4 = 21）：第一段直连 下标 0 与 下标 2，跨过被跳过的 NaN。
        Assert.Equal(42d, points[1].X - points[0].X);
    }

    [Fact]
    public void BuildPoints_maps_a_negative_range_top_down()
    {
        // 全负数（净值回撤、差分、收益率）同样要顶边是最大值：方向只看数值大小，与符号无关。
        // Review Focus 第 3 项「极端值域」里"全负数"的那一格。
        double[] values = [-100d, -75d, -50d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(_plotArea.Bottom, points[0].Y);
        Assert.Equal(PlotCentreY, points[1].Y);
        Assert.Equal(_plotArea.Top, points[^1].Y);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void BuildPoints_never_emits_a_non_finite_coordinate(double badValue)
    {
        // 一个坏点不许污染整条线：结果表里**任何**坐标都必须有限。
        // NaN 与任何数比较都是 false，所以"坏点"不能用区间比较识别；这里两侧的点都是好点，
        // 用来确认坏点被摘下后邻点照常成立（两个好点仍成两点，被跳过的那个不占位）。
        double[] values = [1d, badValue, 2d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(2, points.Count);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X), $"x 成了 {point.X}。");
            Assert.True(double.IsFinite(point.Y), $"y 成了 {point.Y}。");
        });
    }

    [Fact]
    public void BuildPoints_maps_extreme_finite_values_instead_of_dropping_them()
    {
        // 有限值再极端也要照常映射：**有限不等于"画不出来"**，只有非有限才跳过。
        // 量级到 double.MaxValue 时跨度仍然有限（MaxValue - 1 舍入回 MaxValue），
        // 映射照走，最大的一点精确落在顶边。
        double[] huge = [1d, double.MaxValue, 2d];

        IReadOnlyList<Point> points = LineChart.BuildPoints(huge, ValueRange.From(huge), _plotArea);

        Assert.Equal(3, points.Count);
        Assert.Equal(_plotArea.Bottom, points[0].Y);
        Assert.Equal(_plotArea.Top, points[1].Y);
        Assert.Equal(_plotArea.Bottom, points[2].Y);

        // 最负的有限值同理：2 - double.MinValue 也**没有**溢出成 Infinity（它舍入回 MaxValue），
        // 所以 [1, MinValue, 2] 是一段正常的映射，不是"跨度不可映射"的退路——
        // 别把"量级极端"和"跨度非有限"混为一谈，后者才是退路。
        double[] negative = [1d, double.MinValue, 2d];

        points = LineChart.BuildPoints(negative, ValueRange.From(negative), _plotArea);

        Assert.Equal(3, points.Count);
        Assert.Equal(_plotArea.Top, points[0].Y);
        Assert.Equal(_plotArea.Bottom, points[1].Y);
        Assert.Equal(_plotArea.Top, points[2].Y);
    }

    [Fact]
    public void BuildPoints_on_an_overflowing_span_falls_back_to_the_vertical_centre()
    {
        // 真正走"跨度不可映射"退路的是 [-MaxValue, +MaxValue] 这种：跨度溢出成 +Infinity，
        // 任何线性映射都是假的，于是全部落到垂直正中（ChartHitTest.YFor 的退化分支）。
        // 这里顺带钉住"退路只影响 y、x 照旧按下标铺开"。
        double[] values = [double.MinValue, double.MaxValue];

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), _plotArea);

        Assert.Equal(2, points.Count);
        Assert.Equal(new Point(_plotArea.Left, PlotCentreY), points[0]);
        Assert.Equal(new Point(_plotArea.Right, PlotCentreY), points[1]);
    }

    [Fact]
    public void BuildPoints_maps_a_zero_sized_plot_area_to_its_origin()
    {
        // 零尺寸绘图区（控件还没排过版，或比两倍内边距还小）含义是"这块地方画不出来"，
        // 但**收手是渲染方的职责**（RenderChart 在尺寸不大于 0 时直接返回）：
        // BuildPoints 保持成一个纯粹、无分支的映射——零宽高把每个点都映到同一点，
        // 不抛异常、不返回空表、也不替调用方做"要不要画"的政策决定。
        double[] values = [1d, 2d, 3d];
        Rect zeroSized = new(8d, 8d, 0d, 0d);

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, ValueRange.From(values), zeroSized);

        Assert.Equal(3, points.Count);
        Assert.All(points, point => Assert.Equal(new Point(8d, 8d), point));
    }

    [Fact]
    public void ComputeWindow_returns_the_ticks_that_define_its_own_window()
    {
        // 网格线与折线**必须共用同一个窗口**，否则网格线说的刻度与曲线落的位置是两回事。
        // 窗口就一条链：数据极值 → ValueRange.From → AxisTicks.Nice → 首尾刻度，
        // 而窗口**就是**那两条首尾刻度本身（这是"共用"这条不变量的落点，别在调用处另算一次）。
        double[] values = [3d, 11d, 7d, 5d, 9d];

        (ValueRange window, IReadOnlyList<double> ticks) = LineChart.ComputeWindow(values, 4);

        Assert.True(ticks.Count >= 2, "刻度至少两条——一条刻度就是零高度窗口。");
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);
        Assert.False(window.IsDegenerate);
        Assert.True(window.Min <= 3d && window.Max >= 11d, $"窗口 [{window.Min}, {window.Max}] 没盖住数据。");
        Assert.All(ticks, tick => Assert.True(double.IsFinite(tick), $"刻度成了 {tick}。"));
    }

    [Theory]
    [InlineData(new double[] { })]                                  // 空序列 → ValueRange.From 的兜底窗口
    [InlineData(new[] { 7d })]                                      // 单点 → 退化值域
    [InlineData(new[] { 7d, 7d, 7d })]                              // 全等值 → 退化值域
    [InlineData(new[] { double.NaN, double.PositiveInfinity })]     // 一个有限值都没有
    [InlineData(new[] { double.MaxValue, double.MinValue })]        // 跨度溢出成 +Infinity
    public void ComputeWindow_always_returns_a_drawable_window(double[] values)
    {
        // 无论数据多糟糕，窗口都必须可画：**有限且非退化**。零跨度窗口会把折线推去 0/0 = NaN，
        // 非有限窗口会让每一行网格线一起变成 NaN——而这里跑在 Render() 路径上，
        // 一个异常或一个毒值就是一整窗白屏。
        (ValueRange window, IReadOnlyList<double> ticks) = LineChart.ComputeWindow(values, 4);

        Assert.True(double.IsFinite(window.Min) && double.IsFinite(window.Max), $"窗口成了 [{window.Min}, {window.Max}]。");
        Assert.True(window.Min < window.Max, $"窗口 [{window.Min}, {window.Max}] 是退化的。");
        Assert.True(ticks.Count >= 2);
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);

        // 盖住每一个有限值（非有限值不参与值域，由 ValueRange.From 自己滤掉）。
        double[] finite = [.. values.Where(double.IsFinite)];
        if (finite.Length > 0)
        {
            Assert.True(
                window.Min <= finite.Min() && window.Max >= finite.Max(),
                $"窗口 [{window.Min}, {window.Max}] 没盖住数据 [{finite.Min()}, {finite.Max()}]。");
        }
    }

    [Fact]
    public void ComputeWindow_on_values_too_large_to_stretch_falls_back_to_the_fixed_window()
    {
        // 唯一一个"可画但不盖住数据"的角落，来自 AxisTicks.Nice 自己的兜底：
        // 全等值且量级逼近 double.MaxValue 时，向外撑开的值域会溢出成 ±Infinity，
        // 它于是退回固定窗口 (0, 1)。窗口本身仍然可画（有限、非退化），但不覆盖数据——
        // 这是 Task 2 已经写明的让步，不在本控件手里；记在这里是因为它是"窗口覆盖数据"
        // 这条不变量**唯一的例外**，别让后来者以为它无条件成立。
        // 现实中到不了：|值| ≈ 1.8e308 的价格或指标没有意义（真出现也是脏数据）。
        (ValueRange window, IReadOnlyList<double> ticks) = LineChart.ComputeWindow([double.MaxValue, double.MaxValue], 4);

        Assert.Equal(new ValueRange(0d, 1d), window);
        Assert.True(ticks.Count >= 2);
        Assert.Equal(ticks[0], window.Min);
        Assert.Equal(ticks[^1], window.Max);
    }

    [Fact]
    public void GridLineYs_snaps_every_tick_row_and_puts_the_ends_on_the_plot_edges()
    {
        // 一个刻度一行，一行不落。对齐分两步：行号先吸到最近的整数（100% 缩放下的设备像素网格），
        // 再按笔画奇偶推半格，于是每个行号都是"整数 + 0.5"，1px 的笔画覆盖 [⌊y⌋, ⌊y⌋ + 1]、
        // 正好铺满一列设备像素。
        // 首尾两行正好落在绘图区的下 / 上边缘上——这只在"窗口 == 首尾刻度"时成立，
        // 所以这条同时钉住了"网格线与折线共用一个窗口"。
        (ValueRange window, IReadOnlyList<double> ticks) = LineChart.ComputeWindow([3d, 11d, 7d, 5d, 9d], 4);

        IReadOnlyList<double> rows = LineChart.GridLineYs(ticks, window, _plotArea);

        Assert.Equal(ticks.Count, rows.Count);
        Assert.Equal(_plotArea.Bottom + 0.5d, rows[0]);     // 窗口下界 → 底边
        Assert.Equal(_plotArea.Top + 0.5d, rows[^1]);       // 窗口上界 → 顶边
        Assert.All(rows, y =>
        {
            Assert.True(double.IsFinite(y), $"网格线落在 {y}。");
            Assert.Equal(0.5d, y - Math.Floor(y));          // 1px 笔画的中线必须落在"整数 + 0.5"上
        });

        // 刻度值递增、屏幕 y 向下，所以行号必须严格递减（值轴朝上）。
        for (int i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i] < rows[i - 1], $"第 {i} 行没有在往上走：{rows[i - 1]} → {rows[i]}。");
        }
    }

    [Fact]
    public void GridLineYs_rounds_a_fractional_row_onto_the_pixel_grid_before_nudging()
    {
        // 两条断言都要在，缺一不可：**SnapStroke 是半像素的偏移，不是吸附**——它假定喂进去的
        // 坐标已经落在像素原点（整数）上，只负责按笔画奇偶补那半格。只做它、不做取整，
        // 得到的是"小数 + 0.5"：看着像对齐，实际照样骑在两行设备像素之间。
        // 这里挑一个**一定**映射出小数行号的窗口（3.5 与 5 都不整齐），钉住取整那一步。
        ValueRange window = new(0d, 7d);
        IReadOnlyList<double> ticks = [0d, 1d, 2d];

        IReadOnlyList<double> rows = LineChart.GridLineYs(ticks, window, _plotArea);

        // 原始行号是 42、37.142857…、32.285714…（8 + (1 - v/7) × 34）：只有取整之后
        // 才落到 42.5 / 37.5 / 32.5；漏掉取整的话这里会是 37.642857… 与 32.785714…。
        Assert.Equal(new[] { 42.5d, 37.5d, 32.5d }, rows);
    }

    [Fact]
    public void GridLineYs_skips_ticks_that_do_not_map_to_a_finite_row()
    {
        // 非有限的 y = 这一行画不出来。让它带着出生坐标进几何，会把同一帧里所有坐标一起染成 NaN
        // （而绘制是整块画布一起重画的），所以与 BuildPoints 同一条纪律：跳过。
        IReadOnlyList<double> rows = LineChart.GridLineYs([0d, double.NaN, 1d], new ValueRange(0d, 1d), _plotArea);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, y => Assert.True(double.IsFinite(y)));
    }

    [Fact]
    public void GridLineYs_on_an_empty_tick_list_returns_empty()
    {
        // 刻度列表为空（调用方直接喂了个空表）：返回空表，不抛异常。
        Assert.Empty(LineChart.GridLineYs([], new ValueRange(0d, 1d), _plotArea));
    }

    [Fact]
    public void GridLineYs_and_BuildPoints_place_the_data_inside_the_same_plot_area()
    {
        // "两者必须是同一个窗口"的可观测后果：数据点全落在绘图区内、
        // 而网格线的首尾两行正好压在绘图区的下 / 上边缘上。窗口若在两处各算一次，
        // 迟早有一处漂移（比如折线用数据极值、网格用整齐刻度），那时曲线会贴着框边而网格不在框上。
        double[] values = [3d, 11d, 7d, 5d, 9d];

        (ValueRange window, IReadOnlyList<double> ticks) = LineChart.ComputeWindow(values, 4);

        IReadOnlyList<Point> points = LineChart.BuildPoints(values, window, _plotArea);
        IReadOnlyList<double> rows = LineChart.GridLineYs(ticks, window, _plotArea);

        Assert.Equal(values.Length, points.Count);
        Assert.All(points, point => Assert.InRange(point.Y, _plotArea.Top, _plotArea.Bottom));
        Assert.Equal(_plotArea.Bottom + 0.5d, rows[0]);
        Assert.Equal(_plotArea.Top + 0.5d, rows[^1]);
    }

    [Fact]
    public void LineChart_has_data_only_when_at_least_one_value_is_finite()
    {
        // HasData 是基座用来做"空数据短路"的钩子：为 false 时底色、边框、坐标轴、网格线一概不画。
        // 没有数据却画出一套网格，比什么都不画更容易被误读成"数据全是 0"
        // （ChartSurface.HasData 的文档原话）。
        //
        // 所以判据不能只看"长度非零"：**整条序列全是非有限值时也必须算没有数据**。
        // 否则 ValueRange.From 会拿它兜底的 (0, 1) 窗口去撑起一套网格线——一个与序列毫无关系的刻度，
        // 今天没露馅只因为本控件还不画刻度文字；一旦有了标签，那根轴就是假的。
        // （HasData 是 protected，只能在派生类里用 nameof 取名字，这里按名字反射读——与
        // ChartSurfaceContractTests 读同一批受保护成员的做法一致。）
        LineChart chart = new();

        Assert.False(HasDataOf(chart));

        chart.Values = [];
        Assert.False(HasDataOf(chart));

        chart.Values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        Assert.False(HasDataOf(chart));

        // 只要有一个有限值就算有数据——一个点也足够画出一根有意义的轴。
        chart.Values = [double.NaN, 5d];
        Assert.True(HasDataOf(chart));

        chart.Values = [1d];
        Assert.True(HasDataOf(chart));

        chart.Values = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void LineChart_declares_values_and_fill_area_as_styled_properties()
    {
        // 两者都必须是 StyledProperty 且注册在 LineChart 自己名下：
        // 数据与开关改了就重绘，靠的是静态构造里的 AffectsRender<LineChart>(…)
        // （登记有没有写由 ChartsSourceGuardTests 静态守卫，登记了会不会真的重绘只能实机验证）。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<double>?>>(LineChart.ValuesProperty);
        Assert.Equal(typeof(IReadOnlyList<double>), LineChart.ValuesProperty.PropertyType);
        Assert.Equal(nameof(LineChart.Values), LineChart.ValuesProperty.Name);
        Assert.Equal(typeof(LineChart), LineChart.ValuesProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<bool>>(LineChart.FillAreaProperty);
        Assert.Equal(typeof(bool), LineChart.FillAreaProperty.PropertyType);
        Assert.Equal(nameof(LineChart.FillArea), LineChart.FillAreaProperty.Name);
        Assert.Equal(typeof(LineChart), LineChart.FillAreaProperty.OwnerType);
    }

    [Fact]
    public void LineChart_is_a_sealed_chart_surface_with_fill_area_off_by_default()
    {
        // 无 UI 线程下能被构造出来，说明 LineChart 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(LineChart).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(LineChart).BaseType);

        LineChart chart = new();

        // 新控件还没数据，FillArea 默认关（面积填充是"要多画一层"的可选装饰，不该默认打开）。
        Assert.Null(chart.Values);
        Assert.False(chart.FillArea);
    }

    /// <summary>
    /// 读一个 <see cref="LineChart"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(LineChart chart)
    {
        PropertyInfo hasData = typeof(LineChart).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("LineChart 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
