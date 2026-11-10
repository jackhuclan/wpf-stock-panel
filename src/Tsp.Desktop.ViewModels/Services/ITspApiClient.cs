using Tsp.Contracts.Instruments;
using Tsp.Contracts.Market;
using Tsp.Contracts.Watchlist;

namespace Tsp.Desktop.ViewModels.Services;

/// <summary>
/// 行情数据访问契约。这是**冻结的**接口：P1 由夹具实现 <see cref="FixtureApiClient"/> 驱动界面，
/// P2 换成长驻服务端的 HTTP 实现时签名不变，View 与 ViewModel 一行都不改。
/// 因此这里的每个成员都是 View 真实需要的形状，新增成员需走一次明确裁决，不得顺手扩。
/// </summary>
public interface ITspApiClient
{
    /// <summary>
    /// 当前数据是否来自夹具（离线样例数据）。
    /// 界面据此向用户明示"你看到的不是真实行情"——这是规格要求，不是装饰。
    /// </summary>
    bool IsFixtureData { get; }

    /// <summary>
    /// 取指数快照列表（大盘指数）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>指数快照；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken ct);

    /// <summary>
    /// 取全部标的（股票 / ETF / 指数）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>标的列表；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct);

    /// <summary>
    /// 取单个标的的日线行情，按 <paramref name="from"/> 与 <paramref name="to"/> 过滤。
    /// 区间为**闭区间**，两端日期都包含在内。
    /// </summary>
    /// <param name="symbol">标的代码，形如 <c>600519.SH</c>。</param>
    /// <param name="from">起始日期（含）。</param>
    /// <param name="to">截止日期（含）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>区间内的日线；按日期升序。</returns>
    Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>
    /// 取自选列表，条目含标的、备注与最新快照。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>自选条目；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken ct);
}
