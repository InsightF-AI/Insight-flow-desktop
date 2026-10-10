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
        var (minimum, maximum) = HistorySeries.Scale(series.Points.Select(point => (double)point.Fechamento));
        Assert.True(minimum < 30 && maximum > 32);
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
        var (minimum, maximum) = HistorySeries.Scale(series.Points.Select(point => (double)point.Fechamento));
        Assert.True(maximum > minimum);
        Assert.InRange((maximum - (double)price) / (maximum - minimum), 0.49, 0.51);
        Assert.Null(series.Sma20[0]);
    }

    [Fact]
    public void Empty_history_is_a_valid_empty_snapshot()
    {
        var series = new HistorySeries([]);
        Assert.Empty(series.Points);
        Assert.Empty(series.Rsi14);
        Assert.Equal((0, 1), HistorySeries.Scale([]));
    }

    [Fact]
    public void Indicators_follow_the_backend_formulas()
    {
        // Fechamentos 1..25: médias, bandas e RSI têm valores conhecidos.
        var closes = Enumerable.Range(1, 25).Select(value => (double)value).ToArray();

        var sma = Indicators.Sma(closes, 20);
        Assert.Null(sma[18]);
        Assert.Equal(10.5, sma[19]!.Value, 10);
        Assert.Equal(15.5, sma[24]!.Value, 10);

        // Desvio-padrão amostral de 20 inteiros consecutivos: raiz de 35.
        var (upper, lower) = Indicators.Bollinger(closes);
        Assert.Null(upper[18]);
        Assert.Equal(15.5 + 2 * Math.Sqrt(35), upper[24]!.Value, 10);
        Assert.Equal(15.5 - 2 * Math.Sqrt(35), lower[24]!.Value, 10);

        // Só altas: RSI 100 a partir do 15º registro. Uma queda depois disso o reduz.
        var rsi = Indicators.Rsi(closes);
        Assert.Null(rsi[13]);
        Assert.Equal(100, rsi[14]!.Value, 10);
        var withDrop = Indicators.Rsi([.. closes, 20]);
        Assert.InRange(withDrop[25]!.Value, 1, 99);
        // Série parada não tem ganho nem perda: o RSI fica sem valor, como no pandas-ta.
        Assert.All(Indicators.Rsi(Enumerable.Repeat(7.0, 30).ToArray()), value => Assert.Null(value));
    }

    [Fact]
    public void A_longer_daily_history_gives_the_averages_their_earlier_records()
    {
        // 30 dias exibidos: sozinhos, a SMA 20 só existe a partir do 20º registro.
        var shown = Enumerable.Range(0, 30).Select(day => Point(Day(60 + day), 100 + day, 1)).ToArray();
        Assert.Null(new HistorySeries(shown).Sma20[0]);
        Assert.Null(new HistorySeries(shown).Sma50[29]);

        // Com os 60 dias anteriores (e os mesmos 30, em outro horário do dia), as médias cobrem todo o período.
        var longer = Enumerable.Range(0, 90).Select(day => Point(Day(day).Replace("T03:", "T00:"), 40 + day, 1)).ToArray();
        var series = new HistorySeries(shown, longer);
        Assert.Equal(30, series.Points.Count);
        // Média dos fechamentos de 40+41 a 40+59 e do primeiro exibido (100).
        Assert.Equal((Enumerable.Range(41, 19).Sum(day => 40.0 + day) + 100) / 20, series.Sma20[0]!.Value, 10);
        Assert.NotNull(series.Sma50[0]);
        Assert.NotNull(series.Rsi14[0]);

        // Um histórico com vários registros no mesmo dia não pode ser emendado: vale só o período exibido.
        var intraday = new[] { Point("2026-01-01T10:00:00Z", 1, 1), Point("2026-01-01T11:00:00Z", 2, 1) };
        Assert.Null(new HistorySeries(shown, intraday).Sma20[0]);
    }

    private static string Day(int offset) => new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero).AddDays(offset).ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static HistoryPointResponse Point(string time, decimal price, decimal volume) => new()
    {
        Data = DateTimeOffset.Parse(time), Fechamento = price, Volume = volume,
        Abertura = price, Minima = price, Maxima = price
    };
}
