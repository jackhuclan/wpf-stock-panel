namespace Tsp.Contracts.Market;

/// <summary>
/// 日线行情。价格与成交额用 decimal，成交量用 long。
/// </summary>
public sealed record DailyBar(
    string Symbol,
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal Amount);
