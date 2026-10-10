namespace InsightFlow.Desktop.Presentation;

// Séries dos indicadores desenhados no gráfico, com as mesmas fórmulas do backend (pandas-ta). A API devolve só o
// valor mais recente de cada indicador; estes cálculos mostram a evolução e não substituem aqueles valores.
internal static class Indicators
{
    public static double?[] Sma(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        double sum = 0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            if (i >= period - 1) result[i] = sum / period;
        }
        return result;
    }

    // Média de "period" registros mais ou menos "deviations" desvios-padrão amostrais (divisor n − 1), como no backend.
    public static (double?[] Upper, double?[] Lower) Bollinger(IReadOnlyList<double> values, int period = 20, double deviations = 2)
    {
        var upper = new double?[values.Count];
        var lower = new double?[values.Count];
        for (var i = period - 1; i < values.Count; i++)
        {
            double sum = 0;
            for (var j = i - period + 1; j <= i; j++) sum += values[j];
            var mean = sum / period;
            double squares = 0;
            for (var j = i - period + 1; j <= i; j++) squares += (values[j] - mean) * (values[j] - mean);
            var band = deviations * Math.Sqrt(squares / (period - 1));
            upper[i] = mean + band;
            lower[i] = mean - band;
        }
        return (upper, lower);
    }

    // Médias de ganhos e perdas com suavização de Wilder, iniciadas na primeira variação.
    public static double?[] Rsi(IReadOnlyList<double> values, int period = 14)
    {
        var result = new double?[values.Count];
        double gain = 0, loss = 0;
        for (var i = 1; i < values.Count; i++)
        {
            var change = values[i] - values[i - 1];
            var up = Math.Max(change, 0);
            var down = Math.Max(-change, 0);
            if (i == 1)
            {
                gain = up;
                loss = down;
            }
            else
            {
                gain += (up - gain) / period;
                loss += (down - loss) / period;
            }
            if (i >= period && gain + loss > 0) result[i] = 100 * gain / (gain + loss);
        }
        return result;
    }
}
