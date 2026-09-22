using InsightFlow.Desktop.Api.Contracts;

namespace InsightFlow.Desktop.Presentation;

// A snapshot for plotting. Preserve every observation and its original timestamp.
internal sealed class HistorySeries
{
    public IReadOnlyList<HistoryPointResponse> Points { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double MaximumVolume { get; }

    public HistorySeries(IEnumerable<HistoryPointResponse> points)
    {
        Points = Array.AsReadOnly(points.OrderBy(point => point.Data).ToArray());
        if (Points.Count == 0) return;
        var minimum = Points.Min(point => (double)point.Fechamento);
        var maximum = Points.Max(point => (double)point.Fechamento);
        // Pad a constant/single-point series as well, avoiding division by zero.
        var padding = maximum == minimum ? Math.Max(Math.Abs(minimum) * 0.02, 0.00000001) : (maximum - minimum) * 0.08;
        Minimum = minimum - padding;
        Maximum = maximum + padding;
        MaximumVolume = Points.Max(point => Math.Max(0, (double)point.Volume));
    }

    public double PricePosition(decimal price) => (Maximum - (double)price) / (Maximum - Minimum);
    public double VolumePosition(decimal volume) => MaximumVolume == 0 ? 0 : Math.Max(0, (double)volume) / MaximumVolume;
}
