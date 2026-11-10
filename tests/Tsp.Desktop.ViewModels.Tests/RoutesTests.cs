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
}
