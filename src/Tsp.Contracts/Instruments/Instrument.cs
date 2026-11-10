namespace Tsp.Contracts.Instruments;

/// <summary>
/// 标的。契约规则：所有位置参数都声明默认值，缺字段 JSON 反序列化时不吃到 <c>null</c>。
/// <c>Name</c> 为空字符串、<c>Exchange</c> 为 <see cref="Tsp.Contracts.Instruments.Exchange.Sh"/>、
/// <c>AssetType</c> 为 <see cref="Tsp.Contracts.Instruments.AssetType.Stock"/>、<c>ListedOn</c> 为 <c>default</c>。
/// </summary>
public sealed record Instrument(
    string Symbol = "",
    string Name = "",
    Exchange Exchange = default,
    AssetType AssetType = default,
    DateOnly ListedOn = default);
