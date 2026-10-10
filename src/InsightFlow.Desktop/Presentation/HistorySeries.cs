using InsightFlow.Desktop.Api.Contracts;

namespace InsightFlow.Desktop.Presentation;

// Retrato do histórico para o gráfico: preserva cada registro com o seu horário original e leva, alinhadas aos
// registros, as séries dos indicadores.
internal sealed class HistorySeries
{
    public IReadOnlyList<HistoryPointResponse> Points { get; }
    public IReadOnlyList<double?> Sma20 { get; }
    public IReadOnlyList<double?> Sma50 { get; }
    public IReadOnlyList<double?> BollingerUpper { get; }
    public IReadOnlyList<double?> BollingerLower { get; }
    public IReadOnlyList<double?> Rsi14 { get; }

    // "lookback" é um histórico mais longo do mesmo ativo: dá aos indicadores os registros anteriores ao período
    // exibido, para que as médias não comecem vazias na primeira data do gráfico.
    public HistorySeries(IEnumerable<HistoryPointResponse> points, IEnumerable<HistoryPointResponse>? lookback = null)
    {
        Points = Array.AsReadOnly(points.OrderBy(point => point.Data).ToArray());
        var (closes, positions) = WithLookback(Points, lookback);
        double?[] Align(double?[] series) => positions.Select(position => series[position]).ToArray();
        Sma20 = Align(Indicators.Sma(closes, 20));
        Sma50 = Align(Indicators.Sma(closes, 50));
        var (upper, lower) = Indicators.Bollinger(closes);
        BollingerUpper = Align(upper);
        BollingerLower = Align(lower);
        Rsi14 = Align(Indicators.Rsi(closes));
    }

    // Intervalo do eixo com folga. Uma série constante ou de um único ponto também ganha altura, sem divisão por zero.
    public static (double Minimum, double Maximum) Scale(IEnumerable<double> values)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var value in values)
        {
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        if (minimum > maximum) return (0, 1);
        var padding = maximum == minimum ? Math.Max(Math.Abs(minimum) * 0.02, 0.00000001) : (maximum - minimum) * 0.08;
        return (minimum - padding, maximum + padding);
    }

    // Fechamentos usados nos cálculos e a posição de cada registro exibido dentro deles.
    private static (double[] Closes, int[] Positions) WithLookback(
        IReadOnlyList<HistoryPointResponse> points, IEnumerable<HistoryPointResponse>? lookback)
    {
        var own = (points.Select(point => (double)point.Fechamento).ToArray(), Enumerable.Range(0, points.Count).ToArray());
        var earlier = lookback?.ToArray() ?? [];
        if (points.Count == 0 || earlier.Length == 0) return own;

        // Só séries diárias, com um registro por data, podem ser emendadas sem ambiguidade.
        static DateTime Day(HistoryPointResponse point) => point.Data.UtcDateTime.Date;
        var byDay = new SortedDictionary<DateTime, double>();
        foreach (var point in earlier) byDay[Day(point)] = (double)point.Fechamento;
        if (byDay.Count != earlier.Length || points.Select(Day).Distinct().Count() != points.Count) return own;

        // Na mesma data, vale o registro do período exibido.
        foreach (var point in points) byDay[Day(point)] = (double)point.Fechamento;
        var days = byDay.Keys.ToList();
        return (byDay.Values.ToArray(), points.Select(point => days.BinarySearch(Day(point))).ToArray());
    }
}
