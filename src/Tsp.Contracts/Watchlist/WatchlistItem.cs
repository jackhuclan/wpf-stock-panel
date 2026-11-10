using Tsp.Contracts.Instruments;

namespace Tsp.Contracts.Watchlist;

/// <summary>
/// 自选列表条目：标的信息 + 备注 + 最新快照。
/// </summary>
public sealed record WatchlistItem(
    Instrument Instrument,
    string? Note,
    decimal Close,
    decimal ChangePct);
