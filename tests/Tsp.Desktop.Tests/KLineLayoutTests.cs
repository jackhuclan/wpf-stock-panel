using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Tsp.Contracts.Market;
using Tsp.Desktop.Charts;
using Tsp.Desktop.Charts.Layout;

namespace Tsp.Desktop.Tests;

/// <summary>
/// <see cref="KLineChart"/> 的四段纯算术：涨跌判据、两个值域、面板切分、蜡烛几何。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么把算术抽成纯静态函数。</b>仓库的测试栈本阶段之前不含 Avalonia Headless，
/// <c>DrawingContext</c> 不可构造，所以 <c>RenderChart</c> 里画的每一笔在本工程里<b>都断言不了</b>
/// ——「涨的画成红的、跌的画成绿的」「影线真的压在实体中间」只能靠实机验证。于是整条链压进纯函数：
/// 涨跌（<see cref="KLineChart.IsBull"/>）、值域（<see cref="KLineChart.PriceRange"/> /
/// <see cref="KLineChart.VolumeRange"/>）、面板切分（<see cref="KLineChart.SplitPanels"/>）、
/// 蜡烛几何（<see cref="KLineChart.LayoutCandle"/>），<c>RenderChart</c> 只剩下
/// 「取哪一根、按哪支画笔、按什么顺序画」。
/// </para>
/// <para>
/// <b>本文件覆盖</b>：按昨收判涨跌（含"收阴但相对昨收上涨"与<b>一字板</b>两种只有这条规则才对的输入）、
/// 第一根的退路、平盘的口径、两个值域（四价全参与、量含 0）、面板切分不产生负高度、
/// 蜡烛与成交量柱共用一个槽、十字星与一字板都至少占一像素、影线的半像素对齐、
/// 零量的柱零高、空输入不抛异常、<c>HasData</c> 的口径、三个 <c>StyledProperty</c> 的注册。
/// <b>不覆盖</b>任何绘制行为（颜色真的取对了吗、画出来的顺序对不对）——那部分只有实机验证一条路。
/// </para>
/// <para>
/// 蜡烛用例用一块<b>手写的价格面板</b> <c>(8, 8, 84, 25)</c> 与值域 <c>(0, 10)</c>：
/// 于是 <c>y = 33 - 2.5 × 价</c>，价 0 在底边 33、价 10 在顶边 8，四根槽的边界是
/// <c>10/31/52/73 → 27/48/69/90</c>，全是好核对的整数。
/// </para>
/// </remarks>
public sealed class KLineLayoutTests
{
    /// <summary>手写的价格面板：左 8、上 8、右 92、下 33。</summary>
    private static readonly Rect _pricePanel = new(8d, 8d, 84d, 25d);

    /// <summary>手写的成交量面板，与价格面板同宽同左边界（真实布局里也是这样切出来的）。</summary>
    private static readonly Rect _volumePanel = new(8d, 34d, 84d, 8d);

    /// <summary>价格用例统一用的值域：价 0 在底边、价 10 在顶边。</summary>
    private static readonly ValueRange _priceWindow = new(0d, 10d);

    private const double DefaultGap = 0.2d;

    [Fact]
    public void A_bullish_day_closes_above_the_previous_close()
    {
        // **按昨收判，不按开盘判**。造一根"收阴线但相对昨收上涨"的 K 线：
        // 开盘 11、收盘 10.5（相对开盘是跌），可昨收是 10 → 相对昨收是涨。
        // 按开盘判会把它画成绿的，而 A 股行情软件都是相对昨收着色的。
        DailyBar[] bars =
        [
            Bar(10d, 12d, 9d, 10d),
            Bar(11d, 12d, 10d, 10.5d),
        ];

        Assert.False(KLineChart.IsBull(bars, 0), "第一根没有昨收，退路是收盘 > 开盘（10 收平 → 平盘取跌色）。");
        Assert.True(KLineChart.IsBull(bars, 1), "收盘 10.5 高于昨收 10，这是涨。");
    }

    [Fact]
    public void A_limit_up_locked_day_is_bullish_even_though_open_equals_close()
    {
        // 一字板：开盘 = 最高 = 最低 = 收盘。相对开盘永远是"平"，**只有**相对昨收才认得出它是涨停。
        // 按开盘判会把一根一字涨停画成绿的——这是这条判据最要紧的一处。
        DailyBar[] bars =
        [
            Bar(10d, 10.5d, 9.8d, 10d),
            Bar(11d, 11d, 11d, 11d),
        ];

        Assert.True(KLineChart.IsBull(bars, 1), "一字板相对昨收 10 是涨。");

        // 反向：一字跌停同样只有相对昨收才认得出是跌。
        DailyBar[] falling =
        [
            Bar(10d, 10.5d, 9.8d, 10d),
            Bar(9d, 9d, 9d, 9d),
        ];

        Assert.False(KLineChart.IsBull(falling, 1));
    }

    [Fact]
    public void The_first_bar_falls_back_to_close_versus_open()
    {
        // 第一根没有昨收可用，退回"收盘 > 开盘"——这不是偏好，是因为手上只有这两个价，
        // 而它至少回答了"这根是阳线还是阴线"这个更弱的问题。判据取严格大于，与"平盘取跌色"同一口径。
        Assert.True(KLineChart.IsBull([Bar(10d, 12d, 9d, 11d)], 0));
        Assert.False(KLineChart.IsBull([Bar(10d, 12d, 9d, 9.5d)], 0));
        Assert.False(KLineChart.IsBull([Bar(10d, 12d, 9d, 10d)], 0), "第一根收平同样取跌色。");
    }

    [Fact]
    public void A_flat_day_takes_the_bear_colour_because_only_two_brushes_exist()
    {
        // 平盘（收盘 = 昨收）既不是涨也不是跌，而只有两支画笔：归给跌是因为"涨"是需要证据的断言
        // （必须真的高于昨收），其余都算没有涨。这条口径写在 IsBull 的文档里，这里钉住它。
        DailyBar[] bars =
        [
            Bar(10d, 10.5d, 9.8d, 10d),
            Bar(10d, 10.5d, 9.8d, 10d),
        ];

        Assert.False(KLineChart.IsBull(bars, 1));
    }

    [Fact]
    public void Candle_and_volume_column_share_the_same_horizontal_slot()
    {
        // 同一根 K 线的实体与成交量柱必须**上下对齐**：错开半格的话，读者会把放量的那根柱子
        // 认到隔壁 K 线头上。两者共用 ColumnLayout.Slot，所以这里可以断言精确相等。
        DailyBar[] bars = [Bar(10d, 12d, 9d, 11d), Bar(11d, 13d, 10d, 12d), Bar(12d, 14d, 11d, 13d)];

        for (int i = 0; i < bars.Length; i++)
        {
            Rect body = KLineChart.LayoutCandle(i, bars.Length, bars[i], _priceWindow, _pricePanel, DefaultGap).Body;
            Rect column = ColumnLayout
                .Anchored(i, bars.Length, bars[i].Volume, new ValueRange(0d, 300d), _volumePanel, DefaultGap)
                .Column;

            Assert.Equal(body.Left, column.Left);
            Assert.Equal(body.Right, column.Right);
        }
    }

    [Fact]
    public void A_doji_body_is_one_pixel_tall_not_zero()
    {
        // 十字星（开盘 = 收盘）的实体跨度是 0；跨度取整之后可能是 0 像素——这根 K 线就凭空消失了。
        // 价 10 落在**顶边（整数 8）**上，取整前后都是 8，正是会把实体抹掉的那一格。
        DailyBar doji = Bar(10d, 10d, 10d, 10d);

        (Rect body, double wickX, double highY, double lowY) =
            KLineChart.LayoutCandle(0, 1, doji, _priceWindow, _pricePanel, DefaultGap);

        Assert.Equal(8d, body.Top);
        Assert.Equal(1d, body.Height);
        Assert.Equal(50d, wickX - 0.5d);        // 影线落在「整数 + 0.5」上（1px 笔画才铺满一列像素）
        Assert.Equal(1d, lowY - highY);         // 影线的跨度同样至少一像素
    }

    [Fact]
    public void A_quiet_bar_with_a_real_range_keeps_its_exact_body()
    {
        // 普通的阳线：实体从开盘到收盘、影线从最低到最高，四价各就各位。
        // 取整是"向外"，所以实体的上下边界恰好落在价 5 与价 2 映射到的那两行上（20 与 28）。
        DailyBar bar = Bar(2d, 6d, 1d, 5d);

        (Rect body, double wickX, double highY, double lowY) =
            KLineChart.LayoutCandle(0, 4, bar, _priceWindow, _pricePanel, DefaultGap);

        Assert.Equal(new Rect(10d, 20d, 17d, 8d), body);
        Assert.Equal(18.5d, wickX);
        Assert.Equal(18d, highY);               // 价 6 → y = 18
        Assert.Equal(31d, lowY);                // 价 1 → y = 30.5，向外取整到 31
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(30)]
    public void The_wick_is_half_a_pixel_off_a_whole_pixel(int count)
    {
        // 影线是 1px 竖线，中线必须落在「整数 + 0.5」上才铺满一列设备像素。
        // 槽中心多半是「整数 + 0.5」（左右边界都是整数），所以必须先取整再补半格——
        // 少了取整那一步，得到的会是整数，照样骑在两列像素之间（看着像对齐了，其实没有）。
        DailyBar bar = Bar(2d, 6d, 1d, 5d);

        for (int i = 0; i < count; i++)
        {
            double wickX = KLineChart.LayoutCandle(i, count, bar, _priceWindow, _pricePanel, DefaultGap).WickX;

            Assert.True(double.IsFinite(wickX), $"第 {i} 根的影线横坐标成了 {wickX}。");
            Assert.Equal(0.5d, wickX - Math.Floor(wickX), 1e-12d);
            Assert.InRange(wickX, _pricePanel.Left, _pricePanel.Right);
        }
    }

    [Fact]
    public void LayoutCandle_on_an_empty_series_returns_a_zero_sized_candle_without_throwing()
    {
        // 契约是"调用方先判空"（有没有数据是 HasData 的事），但一个走错路的调用方不该拿到毒值：
        // 槽宽 = 面板宽度 / 0 会得到 Infinity，再往下就是一根无限宽的实体。
        (Rect body, double wickX, double highY, double lowY) =
            KLineChart.LayoutCandle(0, 0, Bar(2d, 6d, 1d, 5d), _priceWindow, _pricePanel, DefaultGap);

        Assert.Equal(0d, body.Width);
        Assert.Equal(0d, body.Height);
        Assert.True(double.IsFinite(wickX) && double.IsFinite(highY) && double.IsFinite(lowY));
    }

    [Fact]
    public void Zero_volume_produces_a_zero_height_column_at_the_baseline()
    {
        // 零量的柱高就是 0 像素——零线就在那里，柱长为零是实话，不是"画不出来"。
        // 更要紧的是方向：零高**不能**画成一条负高度的矩形（那在有的渲染实现里会整根翻过去）。
        ValueRange window = new(0d, 80d);

        (Rect column, double baseline) = ColumnLayout.Anchored(0, 3, 0d, window, _volumePanel, DefaultGap);

        Assert.Equal(_volumePanel.Bottom, baseline);        // 量域含 0 → 零线就是面板底边
        Assert.Equal(0d, column.Height);
        Assert.Equal(baseline, column.Y);
        Assert.True(column.Height >= 0d);
    }

    [Fact]
    public void The_price_window_covers_every_open_high_low_close()
    {
        // "窗口盖住画出来的每一样东西"是这里唯一的不变量。所以四价<b>全参与</b>：
        // 只看最高 / 最低的话，一根收盘跑到最高价之上的脏数据会把实体画到框外去。
        DailyBar[] bars =
        [
            Bar(10d, 12d, 9d, 11d),
            Bar(11d, 13d, 10d, 15d),    // 脏数据：收盘 15 高于最高 13
            Bar(9d, 11d, 5d, 10d),
        ];

        ValueRange range = KLineChart.PriceRange(bars);

        Assert.Equal(5d, range.Min);
        Assert.Equal(15d, range.Max);
        Assert.False(range.IsDegenerate);
    }

    [Fact]
    public void The_price_window_of_an_empty_series_is_the_usable_fallback()
    {
        Assert.Equal(ValueRange.From([]), KLineChart.PriceRange([]));
        Assert.Equal(ValueRange.From([]), KLineChart.PriceRange(null!));
    }

    [Theory]
    [InlineData(new long[] { 100, 200, 50 }, 0d, 200d)]
    [InlineData(new long[] { 0, 0, 0 }, 0d, 0d)]        // 全 0：撑无可撑，仍是 (0, 0)，由 Nice 撑开
    [InlineData(new long[] { 300 }, 0d, 300d)]
    public void The_volume_range_always_includes_zero(long[] volumes, double expectedMin, double expectedMax)
    {
        // 成交量柱与柱状图的柱是同一件事：柱长只有相对一条看得见的零线才读得出。
        // 不并 0 的话，量一直很高的区间会把窗口抬到离 0 很远的地方，柱子集体贴着底边、彼此看不出差别。
        DailyBar[] bars = [.. volumes.Select(volume => Bar(10d, 12d, 9d, 11d, volume))];

        ValueRange range = KLineChart.VolumeRange(bars);

        Assert.Equal(expectedMin, range.Min);
        Assert.Equal(expectedMax, range.Max);
    }

    [Fact]
    public void SplitPanels_stacks_the_price_panel_above_the_volume_panel()
    {
        // 上价格、下成交量，中间留一条缝；成交量面板的下边界**就是**绘图区底边。
        Rect plotArea = new(8d, 8d, 84d, 34d);

        (Rect price, Rect volume) = KLineChart.SplitPanels(plotArea);

        Assert.Equal(plotArea.X, price.X);
        Assert.Equal(plotArea.Y, price.Y);
        Assert.Equal(plotArea.Width, price.Width);
        Assert.Equal(plotArea.Width, volume.Width);
        Assert.True(price.Bottom < volume.Top, "两个面板之间没有留缝。");
        Assert.Equal(plotArea.Bottom, volume.Bottom);

        // 价格是主角：它拿到的多数高度要比成交量多。
        Assert.True(price.Height > volume.Height);
    }

    [Theory]
    [InlineData(50d)]
    [InlineData(34d)]
    [InlineData(10d)]
    [InlineData(2d)]
    [InlineData(0d)]
    public void SplitPanels_never_produces_a_negative_height(double height)
    {
        // 控件可以被压得很矮（甚至还没排过版）。负高度的矩形会让后面每一次几何运算都变得反直觉，
        // 而这里跑在 Render() 路径上——所以两块高度一律夹到不小于 0。
        Rect plotArea = new(8d, 8d, 84d, height);

        (Rect price, Rect volume) = KLineChart.SplitPanels(plotArea);

        Assert.True(price.Height >= 0d, $"价格面板高度成了 {price.Height}。");
        Assert.True(volume.Height >= 0d, $"成交量面板高度成了 {volume.Height}。");
        Assert.True(price.Bottom <= volume.Top, "两个面板叠在一起了。");
        Assert.True(volume.Top >= plotArea.Y && volume.Bottom <= plotArea.Bottom + 1e-9d);
    }

    [Fact]
    public void KLineChart_has_data_whenever_the_series_is_not_empty()
    {
        // **这里不需要其余图表那条"至少一个有限值"的判据**：那条防的是"整条序列都是 NaN 却撑起
        // 一套假刻度"，而 DailyBar 的价格是 decimal——不可能是 NaN 或 ±Infinity，那条失效模式
        // 在这条数据上不存在。挡 null 与空表就够（空表同样让基座整块收手）。
        KLineChart chart = new();

        Assert.False(HasDataOf(chart));

        chart.Bars = [];
        Assert.False(HasDataOf(chart));

        chart.Bars = [Bar(10d, 12d, 9d, 11d)];
        Assert.True(HasDataOf(chart));

        // 全 0 的一根（契约说缺字段取默认值）也算有数据：它照画不误，画面上看得见一根塌到底的 K 线。
        chart.Bars = [new DailyBar()];
        Assert.True(HasDataOf(chart));

        chart.Bars = null;
        Assert.False(HasDataOf(chart));
    }

    [Fact]
    public void KLineChart_declares_bars_and_the_two_colour_brushes_as_styled_properties()
    {
        // 三者都必须是 StyledProperty 且注册在 KLineChart 自己名下：数据与涨跌色改了就重绘，
        // 靠的是静态构造里的 AffectsRender<KLineChart>(…)。
        // **两支涨跌画笔尤其要紧**：它们是派生控件追加的画笔里语义最重的一对（红涨绿跌，
        // 只用于价格与 K 线），漏登记就是"切主题后 K 线还留着旧颜色"这条静默失效。
        Assert.IsAssignableFrom<StyledProperty<IReadOnlyList<DailyBar>?>>(KLineChart.BarsProperty);
        Assert.Equal(typeof(IReadOnlyList<DailyBar>), KLineChart.BarsProperty.PropertyType);
        Assert.Equal(nameof(KLineChart.Bars), KLineChart.BarsProperty.Name);
        Assert.Equal(typeof(KLineChart), KLineChart.BarsProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<IBrush?>>(KLineChart.BullBrushProperty);
        Assert.Equal(nameof(KLineChart.BullBrush), KLineChart.BullBrushProperty.Name);
        Assert.Equal(typeof(KLineChart), KLineChart.BullBrushProperty.OwnerType);

        Assert.IsAssignableFrom<StyledProperty<IBrush?>>(KLineChart.BearBrushProperty);
        Assert.Equal(nameof(KLineChart.BearBrush), KLineChart.BearBrushProperty.Name);
        Assert.Equal(typeof(KLineChart), KLineChart.BearBrushProperty.OwnerType);
    }

    [Fact]
    public void KLineChart_is_a_sealed_chart_surface_with_no_brushes_assigned()
    {
        // 无 UI 线程下能被构造出来，说明 KLineChart 的静态构造（属性注册 + AffectsRender）没有偷偷依赖平台。
        Assert.True(typeof(KLineChart).IsSealed);
        Assert.Equal(typeof(ChartSurface), typeof(KLineChart).BaseType);

        KLineChart chart = new();

        Assert.Null(chart.Bars);

        // 画笔未赋值是合法状态，含义是"这一层不画"（配色只在样式库里出现）。
        Assert.Null(chart.BullBrush);
        Assert.Null(chart.BearBrush);

        // PrimaryBrush 留给均线：本阶段没有均线（指标在 P3），它此刻没有使用者是刻意的。
        Assert.Null(chart.PrimaryBrush);
    }

    /// <summary>造一根 K 线（成交额留默认值，本控件的几何不看它）。</summary>
    private static DailyBar Bar(double open, double high, double low, double close, long volume = 100) =>
        new("600000", new DateOnly(2024, 1, 2), (decimal)open, (decimal)high, (decimal)low, (decimal)close, volume);

    /// <summary>
    /// 读一个 <see cref="KLineChart"/> 实例的受保护 <c>HasData</c>。
    /// </summary>
    private static bool HasDataOf(KLineChart chart)
    {
        PropertyInfo hasData = typeof(KLineChart).GetProperty(
            "HasData",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("KLineChart 上没有覆盖 HasData。");

        return (bool)hasData.GetValue(chart)!;
    }
}
