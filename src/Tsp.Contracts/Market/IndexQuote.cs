namespace Tsp.Contracts.Market;

/// <summary>
/// 指数快照。契约规则：所有位置参数都声明默认值，缺字段 JSON 反序列化时不吃到 <c>null</c>。
/// </summary>
public sealed record IndexQuote(
    string Symbol = "",
    string Name = "",
    decimal Close = default,
    decimal ChangePct = default);
