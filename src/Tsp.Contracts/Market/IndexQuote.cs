namespace Tsp.Contracts.Market;

/// <summary>
/// 指数快照。契约规则：所有位置参数都声明默认值，缺字段的 JSON 正常映射而不抛异常。
/// 两个字符串参数是引用类型，缺字段取声明的空串；<c>Close</c> 与 <c>ChangePct</c> 是值类型，
/// <c>System.Text.Json</c> 注入的是 <c>default(T)</c>（<c>0</c>），**不可能**是 <c>null</c>。
/// </summary>
public sealed record IndexQuote(
    string Symbol = "",
    string Name = "",
    decimal Close = default,
    decimal ChangePct = default);
