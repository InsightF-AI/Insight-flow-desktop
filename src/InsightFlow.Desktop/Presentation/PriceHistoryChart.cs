using System.Drawing.Drawing2D;
using System.Globalization;
using InsightFlow.Desktop.Api.Contracts;

namespace InsightFlow.Desktop.Presentation;

internal sealed class PriceHistoryChart : Control
{
    private HistorySeries _series = new([]);
    private string _currency = "";
    private string _message = "Selecione um ativo para consultar o histórico.";
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 15000, InitialDelay = 100, ReshowDelay = 100 };
    private int _selected = -1;

    public PriceHistoryChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.InkSecondary;
        Dock = DockStyle.Fill;
        TabStop = true;
        AccessibleName = "Gráfico de fechamento e volume";
        AccessibleRole = AccessibleRole.Chart;
        AccessibleDescription = "Use as setas para consultar os registros. Os mesmos valores estão na tabela OHLCV.";
    }

    public void SetData(IEnumerable<HistoryPointResponse> points, string currency)
    {
        _series = new HistorySeries(points);
        _currency = currency;
        _message = "Sem histórico para este período.";
        _selected = -1;
        _tooltip.SetToolTip(this, "");
        AccessibleDescription = $"{_series.Points.Count} registros em {currency}. Use as setas para consultar os valores ou a aba Tabela OHLCV.";
        Invalidate();
    }

    public void ShowMessage(string message)
    {
        _series = new HistorySeries([]);
        _selected = -1;
        _message = message;
        _tooltip.SetToolTip(this, "");
        AccessibleDescription = message;
        Invalidate();
    }

    private float DpiScale => DeviceDpi / 96f;

    private RectangleF PriceArea => new(96 * DpiScale, 32 * DpiScale,
        Math.Max(1, ClientSize.Width - 112 * DpiScale), Math.Max(1, (ClientSize.Height - 85 * DpiScale) * 0.7f));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_series.Points.Count == 0 || ClientSize.Width < 200 * DpiScale || ClientSize.Height < 160 * DpiScale)
        {
            TextRenderer.DrawText(g, _series.Points.Count == 0 ? _message : "Amplie a janela para visualizar o gráfico.",
                Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var plot = PriceArea;
        var volumeTop = plot.Bottom + 18 * DpiScale;
        var volumeBottom = ClientSize.Height - 28 * DpiScale;
        using var gridPen = new Pen(UiTheme.Border);
        using var linePen = new Pen(UiTheme.Accent, 2 * DpiScale);
        using var volumeBrush = new SolidBrush(Color.FromArgb(100, UiTheme.Accent));
        using var pointBrush = new SolidBrush(UiTheme.Accent);
        using var textBrush = new SolidBrush(ForeColor);
        g.DrawString($"Fechamento · {_currency}", Font, textBrush, new PointF(6 * DpiScale, 4 * DpiScale));

        for (var tick = 0; tick <= 4; tick++)
        {
            var y = plot.Top + plot.Height * tick / 4;
            var value = _series.Maximum - (_series.Maximum - _series.Minimum) * tick / 4;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var label = AxisNumber(value, (_series.Maximum - _series.Minimum) / 4);
            var size = g.MeasureString(label, Font);
            g.DrawString(label, Font, textBrush, plot.Left - size.Width - 6 * DpiScale, y - size.Height / 2);
        }

        var points = new PointF[_series.Points.Count];
        var barWidth = Math.Max(1, Math.Min(12 * DpiScale, plot.Width / points.Length * 0.65f));
        for (var i = 0; i < points.Length; i++)
        {
            var point = _series.Points[i];
            var x = X(i, plot);
            points[i] = new PointF(x, plot.Top + (float)_series.PricePosition(point.Fechamento) * plot.Height);
            var height = (float)_series.VolumePosition(point.Volume) * Math.Max(0, volumeBottom - volumeTop);
            if (height > 0) g.FillRectangle(volumeBrush, x - barWidth / 2, volumeBottom - height, barWidth, height);
        }

        if (points.Length > 1) g.DrawLines(linePen, points);
        else g.FillEllipse(pointBrush, points[0].X - 3 * DpiScale, points[0].Y - 3 * DpiScale, 6 * DpiScale, 6 * DpiScale);

        g.DrawString("Volume", Font, textBrush, 6 * DpiScale, volumeTop);
        g.DrawLine(gridPen, plot.Left, volumeBottom, plot.Right, volumeBottom);
        var first = _series.Points[0].Data;
        var last = _series.Points[^1].Data;
        var format = first.Date == last.Date ? "dd/MM HH:mm" : "dd/MM/yy";
        g.DrawString(first.ToString(format), Font, textBrush, plot.Left, volumeBottom + 4 * DpiScale);
        if (points.Length > 1)
        {
            var endLabel = last.ToString(format);
            g.DrawString(endLabel, Font, textBrush, plot.Right - g.MeasureString(endLabel, Font).Width, volumeBottom + 4 * DpiScale);
        }

        if (_selected >= 0 && _selected < points.Length)
        {
            using var cursorPen = new Pen(UiTheme.InkMuted) { DashStyle = DashStyle.Dash };
            var point = points[_selected];
            g.DrawLine(cursorPen, point.X, plot.Top, point.X, volumeBottom);
            g.FillEllipse(pointBrush, point.X - 4 * DpiScale, point.Y - 4 * DpiScale, 8 * DpiScale, 8 * DpiScale);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle, ForeColor, BackColor);
    }

    private float X(int index, RectangleF plot) => _series.Points.Count == 1
        ? plot.Left + plot.Width / 2
        : plot.Left + plot.Width * index / (_series.Points.Count - 1);

    // As casas decimais acompanham o intervalo entre as marcas: os rótulos ficam uniformes e cabem na margem do eixo.
    internal static string AxisNumber(double value, double step)
    {
        if (value != 0 && (Math.Abs(value) < 0.0001 || Math.Abs(value) >= 1_000_000))
            return value.ToString("0.###E+0", CultureInfo.CurrentCulture);
        var decimals = step > 0 && double.IsFinite(step) ? Math.Clamp((int)Math.Ceiling(-Math.Log10(step)) + 1, 0, 8) : 2;
        return value.ToString("N" + decimals, CultureInfo.CurrentCulture);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_series.Points.Count == 0) return;
        var plot = PriceArea;
        var fraction = Math.Clamp((e.X - plot.Left) / plot.Width, 0, 1);
        SelectPoint((int)Math.Round(fraction * (_series.Points.Count - 1)), false);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Focused) return;
        _selected = -1;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_series.Points.Count == 0) return;
        var index = e.KeyCode switch
        {
            Keys.Left => Math.Max(0, _selected - 1),
            Keys.Right => Math.Min(_series.Points.Count - 1, _selected + 1),
            Keys.Home => 0,
            Keys.End => _series.Points.Count - 1,
            _ => -1
        };
        if (index < 0) return;
        SelectPoint(index, true);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _tooltip.Hide(this); Invalidate(); }

    private void SelectPoint(int index, bool keyboard)
    {
        if (_selected == index && !keyboard) return;
        _selected = index;
        var point = _series.Points[index];
        var detail = $"{point.Data:dd/MM/yyyy HH:mm zzz}\nFechamento: {_currency} {point.Fechamento:#,0.########}\n" +
                     $"Abertura: {point.Abertura:#,0.########} · Mínima: {point.Minima:#,0.########} · Máxima: {point.Maxima:#,0.########}\nVolume: {point.Volume:#,0.########}";
        AccessibleDescription = detail;
        _tooltip.SetToolTip(this, detail);
        if (keyboard) _tooltip.Show(detail, this, new Point((int)PriceArea.Left, 10), 10000);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltip.Dispose();
        base.Dispose(disposing);
    }
}
