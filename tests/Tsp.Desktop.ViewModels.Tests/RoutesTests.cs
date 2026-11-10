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
}
