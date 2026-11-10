namespace Tsp.Contracts.Instruments;

/// <summary>
/// 交易所。成员数值即线上（JSON 数字）表示，冻结后不得调整。
/// </summary>
public enum Exchange
{
    /// <summary>上海证券交易所。</summary>
    Sh = 0,

    /// <summary>深圳证券交易所。</summary>
    Sz = 1,

    /// <summary>北京证券交易所。</summary>
    Bj = 2,
}
