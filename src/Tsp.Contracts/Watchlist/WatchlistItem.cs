using Tsp.Contracts.Instruments;

namespace Tsp.Contracts.Watchlist;

/// <summary>
/// 自选列表条目：标的信息 + 备注 + 最新快照。
/// </summary>
/// <remarks>
/// <para>
/// 契约规则（见 <see cref="Tsp.Contracts.Market.DailyBar"/>、<see cref="Tsp.Contracts.Market.IndexQuote"/>、
/// <see cref="Instrument"/>）：所有位置参数都声明默认值，使缺字段的 JSON 正常映射而不是抛异常。
/// </para>
/// <para>
/// <b>本类型是这条规则唯一的例外</b>，且四个参数全不给默认值：
/// <c>Instrument</c> 是嵌套引用类型，没有合法的业务默认值——"一条没有标的的自选条目"不是一种数据，
/// 是一个错误。给它写 <c>= null!</c> 只是让编译器闭嘴：缺字段时运行期拿到的仍然是 <c>null</c>，
/// 唯一的差别是 <c>null!</c> 把"这条条目可以没有标的"写进了 API 表面，于是调用方不再被迫传值。
/// 不给默认值，就是在类型上明说 <c>Instrument</c> 必须给出。其余三个参数一并省掉默认值，
/// 使这条例外在整条记录上表述一致——它们本来就无害（值类型与可空串的 <c>default</c>
/// 与显式默认值在运行期等价），代价为零。
/// </para>
/// </remarks>
public sealed record WatchlistItem(
    Instrument Instrument,
    string? Note,
    decimal Close,
    decimal ChangePct);
