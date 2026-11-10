using System.Text.Json;
using Tsp.Contracts.Instruments;
using Tsp.Contracts.Market;
using Tsp.Contracts.Watchlist;

namespace Tsp.Desktop.ViewModels.Services;

/// <summary>
/// <see cref="ITspApiClient"/> 的夹具实现：从本地 JSON 文件读离线样例数据。
/// 夹具目录由构造函数注入——生产由 <c>Tsp.Desktop</c> 传 <c>AppContext.BaseDirectory/Fixtures</c>，
/// 测试传临时目录。本类型只依赖 BCL 的文件与 JSON API，不含任何 UI 依赖，
/// 因而能脱离 Avalonia 直接受测（任务 6 建生产夹具文件，本类型不关心其内容）。
/// </summary>
public sealed class FixtureApiClient : ITspApiClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _fixtureDirectory;

    /// <summary>
    /// 用夹具目录构造。目录不存在不在此时报错——检查推迟到取数时，
    /// 这样"目录没准备好"是一条可呈现的数据错误，而不是构造期崩溃。
    /// </summary>
    /// <param name="fixtureDirectory">夹具 JSON 所在目录，可为相对路径，内部规范化为绝对路径。</param>
    public FixtureApiClient(string fixtureDirectory)
    {
        _fixtureDirectory = Path.GetFullPath(fixtureDirectory);
    }

    /// <inheritdoc />
    public bool IsFixtureData => true;

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexQuote>> GetIndexQuotesAsync(CancellationToken cancellationToken)
    {
        return ReadListAsync<IndexQuote>("index-quotes.json", cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken cancellationToken)
    {
        return ReadListAsync<Instrument>("instruments.json", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyBar>> GetDailyBarsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        IReadOnlyList<DailyBar> bars = await ReadListAsync<DailyBar>(Path.Combine("daily-bars", $"{symbol}.json"), cancellationToken).ConfigureAwait(false);

        // 闭区间，两端都取。过滤在读之后于进程内完成：夹具按标的一个文件存全量日线，切区间是客户端的事。
        // 再按日期升序——契约承诺升序，而夹具文件是手写落盘的，顺序不可依赖。
        // 其余三个成员的"顺序即数据源给出的顺序"不适用于 K 线：画图必须从左到右。
        return bars
            .Where(bar => bar.Date >= from && bar.Date <= to)
            .OrderBy(bar => bar.Date)
            .ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WatchlistItem>> GetWatchlistAsync(CancellationToken cancellationToken)
    {
        return ReadListAsync<WatchlistItem>("watchlist.json", cancellationToken);
    }

    /// <summary>
    /// 读一个 JSON 数组文件并反序列化成 <typeparamref name="T"/> 列表。
    /// 三种失败各有各的可读消息：目录缺失报绝对路径，文件缺失与 JSON 损坏报文件名，
    /// 字段缺失则交给 DTO 自身的默认值兜住、正常映射。
    /// </summary>
    private async Task<IReadOnlyList<T>> ReadListAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(relativePath);

        if (!Directory.Exists(_fixtureDirectory))
        {
            throw new TspApiException($"夹具目录不存在：{_fixtureDirectory}");
        }

        string fullPath = Path.Combine(_fixtureDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new TspApiException($"夹具文件缺失：{fileName}（应位于 {_fixtureDirectory}）");
        }

        string json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // 原始消息折进文案给用户看；异常对象本身也经 InnerException 保留，
            // 因为 JsonException 的结构化 Path / LineNumber / BytePositionInLine
            // 比消息文本更能直接定位到出错的成员。
            throw new TspApiException($"夹具文件 {fileName} 不是合法 JSON：{ex.Message}", ex);
        }
    }
}
