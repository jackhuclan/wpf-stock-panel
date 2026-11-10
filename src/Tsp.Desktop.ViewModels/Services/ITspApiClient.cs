using Tsp.Contracts.Instruments;
using Tsp.Contracts.Market;
using Tsp.Contracts.Watchlist;

namespace Tsp.Desktop.ViewModels.Services;

/// <summary>
/// 行情数据访问契约。这是**冻结的**接口：P1 由夹具实现 <see cref="FixtureApiClient"/> 驱动界面，
/// P2 换成长驻服务端的 HTTP 实现时签名不变，View 与 ViewModel 一行都不改。
/// 因此这里的每个成员都是 View 真实需要的形状，新增成员需走一次明确裁决，不得顺手扩。
/// </summary>
/// <remarks>
/// <para>
/// **失败约定：** 实现取数不成时一律抛 <see cref="TspApiException"/>，
/// 其 <see cref="Exception.Message"/> 是可直接展示给用户的中文说明。
/// 调用方只负责把这条消息呈现出来，不需要认识失败来自夹具、网络还是服务端——
/// 这正是 P2 替换实现时 View 无需改动的关键所在。
/// </para>
/// <para>
/// 取消是唯一的例外：应照常抛 <see cref="OperationCanceledException"/>，
/// **不包装**成 <see cref="TspApiException"/>，否则调用方无法把"用户主动取消"与"取数失败"分开。
/// </para>
/// </remarks>
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
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指数快照；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 取全部标的（股票 / ETF / 指数）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标的列表；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 取单个标的的日线行情，按 <paramref name="from"/> 与 <paramref name="to"/> 过滤。
    /// 区间为**闭区间**，两端日期都包含在内。
    /// </summary>
    /// <param name="symbol">标的代码，形如 <c>600519.SH</c>。</param>
    /// <param name="from">起始日期（含）。</param>
    /// <param name="to">截止日期（含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>区间内的日线；按日期升序。</returns>
    Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// 取自选列表，条目含标的、备注与最新快照。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>自选条目；顺序即数据源给出的顺序。</returns>
    Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken);
}
