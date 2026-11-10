using Tsp.Desktop.ViewModels.Navigation;

namespace Tsp.Desktop.ViewModels.Tests;

public sealed class RoutesTests
{
    [Fact]
    public void All_contains_exactly_the_nineteen_spec_pages()
    {
        var expected = new[]
        {
            "/dashboard", "/watchlist", "/screener", "/factors", "/backtest", "/regime", "/indices", "/data",
            "/stock-analysis", "/limit-ladder", "/concept-analysis", "/industry-analysis", "/financials",
            "/monitor", "/abnormal", "/lots", "/paper", "/signals", "/review",
        };
        Assert.Equal(expected.OrderBy(k => k), Routes.All.Select(r => r.Key).OrderBy(k => k));
    }

    [Fact]
    public void All_has_unique_keys_and_unique_order()
    {
        Assert.Equal(Routes.All.Count, Routes.All.Select(r => r.Key).Distinct().Count());
        Assert.Equal(Routes.All.Count, Routes.All.Select(r => r.Order).Distinct().Count());
    }

    [Fact]
    public void Core_pages_match_spec_domains()
    {
        var core = Routes.All.Where(r => r.Domain == PageDomain.Core).Select(r => r.Key).OrderBy(k => k);
        Assert.Equal(
            new[] { "/backtest", "/dashboard", "/data", "/factors", "/indices", "/regime", "/screener", "/watchlist" },
            core);
    }

    [Fact]
    public void Find_returns_null_for_unknown_key()
    {
        Assert.Null(Routes.Find("/does-not-exist"));
    }

    [Fact]
    public void Titles_match_spec_page_names()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/dashboard"] = "看板",
            ["/watchlist"] = "自选",
            ["/screener"] = "策略",
            ["/factors"] = "因子",
            ["/backtest"] = "回测",
            ["/regime"] = "市场环境",
            ["/indices"] = "指数",
            ["/data"] = "数据",
            ["/stock-analysis"] = "个股分析",
            ["/limit-ladder"] = "连板梯队",
            ["/concept-analysis"] = "概念分析",
            ["/industry-analysis"] = "行业分析",
            ["/financials"] = "财务分析",
            ["/monitor"] = "监控中心",
            ["/abnormal"] = "异动监控",
            ["/lots"] = "持仓提醒",
            ["/paper"] = "模拟盘",
            ["/signals"] = "信号库",
            ["/review"] = "复盘",
        };

        var actual = Routes.All.ToDictionary(r => r.Key, r => r.Title, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void All_is_ordered_by_ascending_order_matching_spec_table()
    {
        // spec §5.6 表格的行序，逐行对应 Order（1 起、连续升序）与路由键。
        // 同时钉住三件事：Order 的取值、其升序序列、以及"哪个页面拿哪个 Order"——
        // 第三条是单看 Enumerable.Range(1, 19) 序列断言漏掉的那一维。
        var expected = new (int Order, string Key)[]
        {
            (1, "/dashboard"), (2, "/watchlist"), (3, "/screener"), (4, "/factors"), (5, "/backtest"),
            (6, "/regime"), (7, "/indices"), (8, "/data"), (9, "/stock-analysis"), (10, "/limit-ladder"),
            (11, "/concept-analysis"), (12, "/industry-analysis"), (13, "/financials"), (14, "/monitor"),
            (15, "/abnormal"), (16, "/lots"), (17, "/paper"), (18, "/signals"), (19, "/review"),
        };

        var actual = Routes.All.Select(r => (r.Order, r.Key)).ToArray();

        Assert.Equal(expected, actual);
    }
}
