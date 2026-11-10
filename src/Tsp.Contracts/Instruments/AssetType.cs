namespace Tsp.Contracts.Instruments;

/// <summary>
/// 标的类型。成员数值即线上（JSON 数字）表示，冻结后不得调整。
/// </summary>
public enum AssetType
{
    /// <summary>股票。</summary>
    Stock = 0,

    /// <summary>交易所交易基金。</summary>
    Etf = 1,

    /// <summary>指数。</summary>
    Index = 2,
}
