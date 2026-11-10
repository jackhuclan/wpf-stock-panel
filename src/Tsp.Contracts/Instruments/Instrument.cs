namespace Tsp.Contracts.Instruments;

/// <summary>
/// 标的。这些默认值只在 JSON 缺字段反序列化时生效：
/// <c>Name</c> 为空字符串，<c>Exchange</c> 为 <see cref="Tsp.Contracts.Instruments.Exchange.Sh"/>，
/// <c>AssetType</c> 为 <see cref="Tsp.Contracts.Instruments.AssetType.Stock"/>，<c>ListedOn</c> 为 <c>default</c>。
/// </summary>
public sealed record Instrument(
    string Symbol,
    string Name = "",
    Exchange Exchange = default,
    AssetType AssetType = default,
    DateOnly ListedOn = default);
