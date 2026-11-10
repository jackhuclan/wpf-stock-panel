namespace Tsp.Contracts.Market;

/// <summary>
/// 日线行情。价格与成交额用 decimal，成交量用 long。
/// 契约规则：所有位置参数都声明默认值，缺字段 JSON 反序列化时不吃到 <c>null</c>。
/// </summary>
public sealed record DailyBar(
    string Symbol = "",
    DateOnly Date = default,
    decimal Open = default,
    decimal High = default,
    decimal Low = default,
    decimal Close = default,
    long Volume = default,
    decimal Amount = default);
