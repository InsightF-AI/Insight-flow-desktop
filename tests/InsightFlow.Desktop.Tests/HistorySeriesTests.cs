using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Tests;

public sealed class HistorySeriesTests
{
    [Fact]
    public void Orders_by_instant_without_mutating_source_or_losing_intraday_records_and_offsets()
    {
        var later = Point("2026-09-18T11:00:00-03:00", 32, 200);
        var earlier = Point("2026-09-18T13:00:00Z", 30, 100);
        var source = new[] { later, earlier };
        var series = new HistorySeries(source);
        Assert.Equal(new[] { earlier, later }, series.Points);
        Assert.Equal(TimeSpan.FromHours(-3), series.Points[1].Data.Offset);
        Assert.Same(later, source[0]);
        Assert.True(series.PricePosition(32) < series.PricePosition(30));
        Assert.Equal(0.5, series.VolumePosition(100));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("0.00000001")]
    [InlineData("79228162514264337593543950335")]
    public void A_constant_series_has_a_finite_scale_even_at_decimal_extremes(string value)
    {
        var price = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var series = new HistorySeries([Point("2026-09-18T00:00:00Z", price, 0)]);
        Assert.True(series.Maximum > series.Minimum);
        Assert.InRange(series.PricePosition(price), 0.49, 0.51);
        Assert.Equal(0, series.VolumePosition(0));
    }

    [Fact]
    public void Empty_history_is_a_valid_empty_snapshot()
    {
        var series = new HistorySeries([]);
        Assert.Empty(series.Points);
        Assert.Equal(0, series.MaximumVolume);
    }

    private static HistoryPointResponse Point(string time, decimal price, decimal volume) => new()
    {
        Data = DateTimeOffset.Parse(time), Fechamento = price, Volume = volume,
        Abertura = price, Minima = price, Maxima = price
    };
}
