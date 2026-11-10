using System.Text.Json;
using Tsp.Contracts.Market;
using Tsp.Desktop.ViewModels.Services;

namespace Tsp.Desktop.ViewModels.Tests;

public sealed class FixtureApiClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tsp-fixture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task GetIndexQuotes_reads_and_maps_fixture()
    {
        Write("index-quotes.json", """[{"symbol":"000001.SH","name":"上证指数","close":3697.52,"changePct":0.19}]""");
        var sut = new FixtureApiClient(_dir);

        var quotes = await sut.GetIndexQuotesAsync(CancellationToken.None);

        Assert.Single(quotes);
        Assert.Equal(3697.52m, quotes[0].Close);
        Assert.True(sut.IsFixtureData);
    }

    [Fact]
    public async Task GetInstruments_reads_and_maps_fixture()
    {
        Write("instruments.json", """[{"symbol":"600519.SH","name":"贵州茅台","exchange":0,"assetType":0,"listedOn":"2001-08-27"}]""");
        var sut = new FixtureApiClient(_dir);

        var instruments = await sut.GetInstrumentsAsync(CancellationToken.None);

        Assert.Single(instruments);
        Assert.Equal("600519.SH", instruments[0].Symbol);
        Assert.Equal(new DateOnly(2001, 8, 27), instruments[0].ListedOn);
    }

    [Fact]
    public async Task Missing_fixture_directory_reports_readable_error()
    {
        var sut = new FixtureApiClient(Path.Combine(_dir, "not-here"));

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("not-here", ex.Message);
    }

    [Fact]
    public async Task Missing_fixture_directory_message_carries_an_absolute_path()
    {
        // 契约要求消息里是**绝对**路径。上一条用的是绝对临时路径，无论实现有没有规范化都能过，
        // 因此这条特意喂相对路径：只有真做了规范化，消息里才会出现解析后的绝对形式。
        var relative = "tsp-fixture-relative-" + Guid.NewGuid().ToString("N");
        var sut = new FixtureApiClient(relative);

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains(Path.GetFullPath(relative), ex.Message);
    }

    [Fact]
    public async Task Missing_fixture_file_reports_readable_error_naming_the_file()
    {
        Directory.CreateDirectory(_dir);
        var sut = new FixtureApiClient(_dir);

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("index-quotes.json", ex.Message);
    }

    [Fact]
    public async Task Corrupt_json_reports_readable_error_naming_the_file()
    {
        Write("index-quotes.json", "{ this is not json");
        var sut = new FixtureApiClient(_dir);

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("index-quotes.json", ex.Message);
    }

    [Fact]
    public async Task Corrupt_json_message_also_carries_the_original_parser_message()
    {
        // 契约要求损坏消息里同时有文件名与**原始异常消息**（它带行列位置，是修夹具的唯一线索）。
        // 用一个选项相同的反序列化现场取得那条原始消息，再断言它确实被包进了 FixtureDataException。
        const string bad = "{ this is not json";
        Write("index-quotes.json", bad);
        var sut = new FixtureApiClient(_dir);
        var original = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<List<IndexQuote>>(bad, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var ex = await Assert.ThrowsAsync<FixtureDataException>(() => sut.GetIndexQuotesAsync(CancellationToken.None));

        Assert.Contains("index-quotes.json", ex.Message);
        Assert.Contains(original.Message, ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task Fixture_missing_optional_fields_still_maps()
    {
        Write("watchlist.json", """[{"instrument":{"symbol":"600519.SH"},"close":1702.30,"changePct":0.31}]""");
        var sut = new FixtureApiClient(_dir);

        var items = await sut.GetWatchlistAsync(CancellationToken.None);

        Assert.Single(items);
        Assert.Null(items[0].Note);
        Assert.Equal("600519.SH", items[0].Instrument.Symbol);
    }

    [Fact]
    public async Task GetDailyBars_filters_by_date_range()
    {
        Write("daily-bars/600519.SH.json", """
            [{"symbol":"600519.SH","date":"2026-10-22","open":1690,"high":1700,"low":1685,"close":1695,"volume":100,"amount":169500},
             {"symbol":"600519.SH","date":"2026-10-23","open":1695,"high":1710,"low":1692,"close":1705,"volume":110,"amount":187550}]
            """);
        var sut = new FixtureApiClient(_dir);

        var bars = await sut.GetDailyBarsAsync("600519.SH", new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23), CancellationToken.None);

        Assert.Single(bars);
        Assert.Equal(new DateOnly(2026, 10, 23), bars[0].Date);
    }

    [Fact]
    public async Task GetDailyBars_interval_is_inclusive_on_both_ends()
    {
        // 上下边界都必须闭：只测 from == to 的单个交易日无法区分"下界闭"与"下界开"
        // （那天恰好落在区间内，两种实现都会返回它），故这里让两根 bar 分别压在两个边界上。
        Write("daily-bars/600519.SH.json", """
            [{"symbol":"600519.SH","date":"2026-10-21","open":1680,"high":1690,"low":1675,"close":1685,"volume":90,"amount":151650},
             {"symbol":"600519.SH","date":"2026-10-22","open":1690,"high":1700,"low":1685,"close":1695,"volume":100,"amount":169500},
             {"symbol":"600519.SH","date":"2026-10-23","open":1695,"high":1710,"low":1692,"close":1705,"volume":110,"amount":187550}]
            """);
        var sut = new FixtureApiClient(_dir);

        var bars = await sut.GetDailyBarsAsync("600519.SH", new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 23), CancellationToken.None);

        Assert.Equal(2, bars.Count);
        Assert.Equal(new DateOnly(2026, 10, 22), bars[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 23), bars[1].Date);
    }

    [Fact]
    public async Task Cancellation_token_is_honoured()
    {
        // 接口带着 CancellationToken 就必须真的用上：同步读文件的实现不会观察令牌，
        // 这条会以"没有抛出异常"失败，从而钉住"令牌传到了文件读取"这一要求。
        Write("index-quotes.json", """[{"symbol":"000001.SH","close":3697.52}]""");
        var sut = new FixtureApiClient(_dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetIndexQuotesAsync(cts.Token));
    }

    private void Write(string relativePath, string json)
    {
        var full = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }
}
