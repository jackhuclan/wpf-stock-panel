namespace Tsp.Contracts.Market;

/// <summary>
/// 指数快照。
/// </summary>
public sealed record IndexQuote(
    string Symbol,
    string Name,
    decimal Close,
    decimal ChangePct);
