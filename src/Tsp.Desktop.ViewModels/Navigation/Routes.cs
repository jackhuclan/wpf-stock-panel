namespace Tsp.Desktop.ViewModels.Navigation;

/// <summary>
/// 页面路由注册表：全应用页面集合的唯一事实来源。
/// <see cref="All"/> 按 <see cref="PageRoute.Order"/> 升序排列，顺序与设计文档 §5.6 的表格一致：
/// 核心 8 页在前，扩展 11 页随后。不含设置页（设置页由主窗口壳单独承载）。
/// </summary>
public static class Routes
{
    // 核心 8 页
    public const string Dashboard = "/dashboard";
    public const string Watchlist = "/watchlist";
    public const string Screener = "/screener";
    public const string Factors = "/factors";
    public const string Backtest = "/backtest";
    public const string Regime = "/regime";
    public const string Indices = "/indices";
    public const string Data = "/data";

    // 扩展 11 页
    public const string StockAnalysis = "/stock-analysis";
    public const string LimitLadder = "/limit-ladder";
    public const string ConceptAnalysis = "/concept-analysis";
    public const string IndustryAnalysis = "/industry-analysis";
    public const string Financials = "/financials";
    public const string Monitor = "/monitor";
    public const string Abnormal = "/abnormal";
    public const string Lots = "/lots";
    public const string Paper = "/paper";
    public const string Signals = "/signals";
    public const string Review = "/review";

    /// <summary>
    /// 全部 19 页，按 <see cref="PageRoute.Order"/> 升序。
    /// </summary>
    public static IReadOnlyList<PageRoute> All { get; } = Array.AsReadOnly(new PageRoute[]
    {
        new PageRoute(Dashboard, "看板", PageDomain.Core, 1),
        new PageRoute(Watchlist, "自选", PageDomain.Core, 2),
        new PageRoute(Screener, "策略", PageDomain.Core, 3),
        new PageRoute(Factors, "因子", PageDomain.Core, 4),
        new PageRoute(Backtest, "回测", PageDomain.Core, 5),
        new PageRoute(Regime, "市场环境", PageDomain.Core, 6),
        new PageRoute(Indices, "指数", PageDomain.Core, 7),
        new PageRoute(Data, "数据", PageDomain.Core, 8),
        new PageRoute(StockAnalysis, "个股分析", PageDomain.Ext, 9),
        new PageRoute(LimitLadder, "连板梯队", PageDomain.Ext, 10),
        new PageRoute(ConceptAnalysis, "概念分析", PageDomain.Ext, 11),
        new PageRoute(IndustryAnalysis, "行业分析", PageDomain.Ext, 12),
        new PageRoute(Financials, "财务分析", PageDomain.Ext, 13),
        new PageRoute(Monitor, "监控中心", PageDomain.Ext, 14),
        new PageRoute(Abnormal, "异动监控", PageDomain.Ext, 15),
        new PageRoute(Lots, "持仓提醒", PageDomain.Ext, 16),
        new PageRoute(Paper, "模拟盘", PageDomain.Ext, 17),
        new PageRoute(Signals, "信号库", PageDomain.Ext, 18),
        new PageRoute(Review, "复盘", PageDomain.Ext, 19),
    });

    /// <summary>
    /// 按键精确查找（<see cref="StringComparer.Ordinal"/>，区分大小写）。未命中返回 <c>null</c>——未知路由是正常情况
    /// （手输错、深链失效、页面被裁掉），由界面显示空状态，不抛异常。
    /// </summary>
    public static PageRoute? Find(string key)
    {
        foreach (PageRoute route in All)
        {
            if (StringComparer.Ordinal.Equals(route.Key, key))
            {
                return route;
            }
        }

        return null;
    }
}
