namespace Tsp.Contracts.Market;

/// <summary>
/// 日线行情。价格与成交额用 decimal，成交量用 long。
/// 契约规则：所有位置参数都声明默认值，缺字段的 JSON 正常映射而不抛异常。
/// 具体拿到什么由参数类型决定，而不是由"不吃到 <c>null</c>"一句话概括：
/// <c>Symbol</c> 是引用类型，缺字段取声明的 <c>""</c>；<c>Date</c>、各价格、<c>Volume</c> 与
/// <c>Amount</c> 都是值类型，<c>System.Text.Json</c> 注入的是 <c>default(T)</c>——即
/// <c>0001-01-01</c> 与 <c>0</c>，它们**不可能**是 <c>null</c>，上面的 <c>= default</c> 只是把
/// 这件事写在签名上。
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
