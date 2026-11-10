using System.Text.Json;
using Tsp.Contracts.Instruments;
using Tsp.Contracts.Market;

namespace Tsp.Contracts.Tests;

public sealed class SerializationRoundTripTests
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DailyBar_round_trips_with_decimal_precision()
    {
        var bar = new DailyBar("600519.SH", new DateOnly(2026, 10, 26), 1697.00m, 1705.50m, 1690.10m, 1702.30m, 1234567L, 2100000000.55m);
        var json = JsonSerializer.Serialize(bar, _options);
        Assert.Equal(bar, JsonSerializer.Deserialize<DailyBar>(json, _options));
    }

    [Fact]
    public void Instrument_missing_fields_takes_defaults()
    {
        var json = """{"symbol":"600519.SH"}""";
        var instrument = JsonSerializer.Deserialize<Instrument>(json, _options);
        Assert.NotNull(instrument);
        Assert.Equal("600519.SH", instrument.Symbol);
        Assert.Equal(string.Empty, instrument.Name);
        Assert.Equal(AssetType.Stock, instrument.AssetType);
    }

    [Theory]
    [InlineData(AssetType.Stock, 0)]
    [InlineData(AssetType.Etf, 1)]
    [InlineData(AssetType.Index, 2)]
    public void AssetType_wire_numbers_are_pinned(AssetType value, int expected)
    {
        Assert.Equal(expected, (int)value);
        Assert.Equal(value, JsonSerializer.Deserialize<AssetType>(expected.ToString(), _options));
    }
}
