using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using InsightFlow.Desktop.Api.Contracts;

namespace InsightFlow.Desktop.Presentation;

internal sealed class PriceHistoryChart : Control
{
    // Cores das séries; as caixas de seleção da tela usam as mesmas, servindo de legenda.
    public static readonly Color Sma20Color = UiTheme.Amber;
    public static readonly Color Sma50Color = UiTheme.InkSecondary;
    public static readonly Color BollingerColor = UiTheme.InkMuted;
    public static readonly Color RsiColor = UiTheme.Amber;
    // Com menos registros que isto na tela o zoom deixa de ajudar a leitura.
    private const int MinimumVisible = 10;
    private HistorySeries _series = new([]);
    private string _currency = "";
    private string _message = "Selecione um ativo para consultar o histórico.";
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 15000, InitialDelay = 100, ReshowDelay = 100 };
    private int _selected = -1;
    // Janela visível sobre a série; zero em "_count" significa a série inteira.
    private int _first;
    private int _count;
    private bool _candles;
    private bool _sma20 = true;
    private bool _sma50 = true;
    private bool _bollinger;
    private bool _rsi;

    public PriceHistoryChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.InkSecondary;
        Dock = DockStyle.Fill;
        TabStop = true;
        AccessibleName = "Gráfico de preço, indicadores e volume";
        AccessibleRole = AccessibleRole.Chart;
        AccessibleDescription = "Use as setas para consultar os registros. Os mesmos valores estão na tabela OHLCV.";
    }

    // Opções ligadas pela tela em tempo de execução; não são gravadas pelo designer.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Candles { get => _candles; set => Toggle(ref _candles, value); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSma20 { get => _sma20; set => Toggle(ref _sma20, value); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSma50 { get => _sma50; set => Toggle(ref _sma50, value); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowBollinger { get => _bollinger; set => Toggle(ref _bollinger, value); }
    // O RSI ocupa o painel inferior no lugar do volume.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowRsi { get => _rsi; set => Toggle(ref _rsi, value); }

    private void Toggle(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Invalidate();
    }

    public void SetData(IEnumerable<HistoryPointResponse> points, string currency, IEnumerable<HistoryPointResponse>? lookback = null)
    {
        _series = new HistorySeries(points, lookback);
        _currency = currency;
        _message = "Sem histórico para este período.";
        _selected = -1;
        _first = _count = 0;
        _tooltip.SetToolTip(this, "");
        AccessibleDescription = $"{_series.Points.Count} registros em {currency}. Use as setas para consultar os valores, " +
                                "mais e menos ou a roda do mouse para o zoom, ou a aba Tabela OHLCV.";
        Invalidate();
    }

    public void ShowMessage(string message)
    {
        _series = new HistorySeries([]);
        _selected = -1;
        _first = _count = 0;
        _message = message;
        _tooltip.SetToolTip(this, "");
        AccessibleDescription = message;
        Invalidate();
    }

    private float DpiScale => DeviceDpi / 96f;
    private int VisibleCount => _count == 0 ? _series.Points.Count : Math.Min(_count, _series.Points.Count);

    // Sem altura para os dois painéis, o gráfico mostra só o de preço; abaixo de MinimumHeight, pede uma janela maior.
    private const int TwoPanelHeight = 160;
    private const int MinimumHeight = 110;
    private bool PriceOnly => ClientSize.Height < TwoPanelHeight * DpiScale;

    private RectangleF PriceArea => new(96 * DpiScale, 32 * DpiScale,
        Math.Max(1, ClientSize.Width - 112 * DpiScale),
        Math.Max(1, PriceOnly ? ClientSize.Height - 62 * DpiScale : (ClientSize.Height - 85 * DpiScale) * 0.7f));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_series.Points.Count == 0 || ClientSize.Width < 200 * DpiScale || ClientSize.Height < MinimumHeight * DpiScale)
        {
            TextRenderer.DrawText(g, _series.Points.Count == 0 ? _message : "Amplie a janela para visualizar o gráfico.",
                Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var plot = PriceArea;
        var count = VisibleCount;
        var last = _first + count - 1;
        var lowerTop = plot.Bottom + 18 * DpiScale;
        var lowerBottom = ClientSize.Height - 28 * DpiScale;
        var (minimum, maximum) = HistorySeries.Scale(VisiblePrices());
        float Y(double value) => plot.Top + (float)((maximum - value) / (maximum - minimum)) * plot.Height;

        using var gridPen = new Pen(UiTheme.Border);
        using var textBrush = new SolidBrush(ForeColor);
        using var mutedBrush = new SolidBrush(UiTheme.InkMuted);
        g.DrawString($"{(_candles ? "Preço" : "Fechamento")} · {_currency}", Font, textBrush, new PointF(6 * DpiScale, 4 * DpiScale));
        if (count < _series.Points.Count)
        {
            var zoom = $"{count} de {_series.Points.Count} registros · duplo clique mostra todos";
            g.DrawString(zoom, Font, mutedBrush, plot.Right - g.MeasureString(zoom, Font).Width, 4 * DpiScale);
        }

        // Num painel baixo, cinco rótulos de preço se sobreporiam.
        var ticks = plot.Height < 70 * DpiScale ? 2 : 4;
        for (var tick = 0; tick <= ticks; tick++)
        {
            var y = plot.Top + plot.Height * tick / ticks;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var label = AxisNumber(maximum - (maximum - minimum) * tick / ticks, (maximum - minimum) / ticks);
            var size = g.MeasureString(label, Font);
            g.DrawString(label, Font, textBrush, plot.Left - size.Width - 6 * DpiScale, y - size.Height / 2);
        }

        var barWidth = Math.Max(1, Math.Min(12 * DpiScale, plot.Width / count * 0.65f));
        if (_bollinger)
        {
            using var bandPen = new Pen(BollingerColor, DpiScale) { DashStyle = DashStyle.Dash };
            DrawSeries(g, bandPen, _series.BollingerUpper, plot, Y);
            DrawSeries(g, bandPen, _series.BollingerLower, plot, Y);
        }
        if (_candles) DrawCandles(g, plot, barWidth, Y);
        else DrawCloses(g, plot, Y);
        if (_sma50)
        {
            using var pen = new Pen(Sma50Color, 1.5f * DpiScale);
            DrawSeries(g, pen, _series.Sma50, plot, Y);
        }
        if (_sma20)
        {
            using var pen = new Pen(Sma20Color, 1.5f * DpiScale);
            DrawSeries(g, pen, _series.Sma20, plot, Y);
        }

        if (!PriceOnly)
        {
            if (_rsi) DrawRsi(g, plot, lowerTop, lowerBottom, gridPen, textBrush);
            else DrawVolume(g, plot, lowerTop, lowerBottom, barWidth, textBrush);
            g.DrawLine(gridPen, plot.Left, lowerBottom, plot.Right, lowerBottom);
        }

        var start = _series.Points[_first].Data;
        var end = _series.Points[last].Data;
        var format = start.Date == end.Date ? "dd/MM HH:mm" : "dd/MM/yy";
        g.DrawString(start.ToString(format), Font, textBrush, plot.Left, lowerBottom + 4 * DpiScale);
        if (count > 1)
        {
            var endLabel = end.ToString(format);
            g.DrawString(endLabel, Font, textBrush, plot.Right - g.MeasureString(endLabel, Font).Width, lowerBottom + 4 * DpiScale);
        }

        if (_selected >= _first && _selected <= last)
        {
            using var cursorPen = new Pen(UiTheme.InkMuted) { DashStyle = DashStyle.Dash };
            using var pointBrush = new SolidBrush(UiTheme.Accent);
            var x = X(_selected, plot);
            var y = Y((double)_series.Points[_selected].Fechamento);
            g.DrawLine(cursorPen, x, plot.Top, x, lowerBottom);
            g.FillEllipse(pointBrush, x - 4 * DpiScale, y - 4 * DpiScale, 8 * DpiScale, 8 * DpiScale);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle, ForeColor, BackColor);
    }

    // O eixo de preços acomoda tudo o que está desenhado na janela visível.
    private IEnumerable<double> VisiblePrices()
    {
        for (var i = _first; i < _first + VisibleCount; i++)
        {
            var point = _series.Points[i];
            if (_candles)
            {
                yield return (double)point.Minima;
                yield return (double)point.Maxima;
            }
            // O fechamento entra sempre: é ele que o cursor marca.
            yield return (double)point.Fechamento;
            if (_sma20 && _series.Sma20[i] is { } sma20) yield return sma20;
            if (_sma50 && _series.Sma50[i] is { } sma50) yield return sma50;
            if (_bollinger && _series.BollingerUpper[i] is { } upper) yield return upper;
            if (_bollinger && _series.BollingerLower[i] is { } lower) yield return lower;
        }
    }

    private void DrawCloses(Graphics g, RectangleF plot, Func<double, float> y)
    {
        using var linePen = new Pen(UiTheme.Accent, 2 * DpiScale);
        var count = VisibleCount;
        if (count == 1)
        {
            using var pointBrush = new SolidBrush(UiTheme.Accent);
            g.FillEllipse(pointBrush, X(_first, plot) - 3 * DpiScale, y((double)_series.Points[_first].Fechamento) - 3 * DpiScale, 6 * DpiScale, 6 * DpiScale);
            return;
        }
        var points = new PointF[count];
        for (var i = 0; i < count; i++) points[i] = new PointF(X(_first + i, plot), y((double)_series.Points[_first + i].Fechamento));
        g.DrawLines(linePen, points);
    }

    // Verde e vermelho indicam apenas se o registro fechou acima ou abaixo da abertura.
    private void DrawCandles(Graphics g, RectangleF plot, float barWidth, Func<double, float> y)
    {
        using var upPen = new Pen(UiTheme.Up, DpiScale);
        using var downPen = new Pen(UiTheme.Down, DpiScale);
        using var upBrush = new SolidBrush(UiTheme.Up);
        using var downBrush = new SolidBrush(UiTheme.Down);
        for (var i = _first; i < _first + VisibleCount; i++)
        {
            var point = _series.Points[i];
            var rising = point.Fechamento >= point.Abertura;
            var x = X(i, plot);
            g.DrawLine(rising ? upPen : downPen, x, y((double)point.Maxima), x, y((double)point.Minima));
            var top = y((double)Math.Max(point.Abertura, point.Fechamento));
            var bottom = y((double)Math.Min(point.Abertura, point.Fechamento));
            g.FillRectangle(rising ? upBrush : downBrush, x - barWidth / 2, top, barWidth, Math.Max(DpiScale, bottom - top));
        }
    }

    // Trechos sem valor (o começo das médias) interrompem a linha em vez de ligá-la ao zero.
    private void DrawSeries(Graphics g, Pen pen, IReadOnlyList<double?> values, RectangleF plot, Func<double, float> y)
    {
        var segment = new List<PointF>();
        for (var i = _first; i <= _first + VisibleCount; i++)
        {
            if (i < _first + VisibleCount && values[i] is { } value)
            {
                segment.Add(new PointF(X(i, plot), y(value)));
                continue;
            }
            if (segment.Count > 1) g.DrawLines(pen, segment.ToArray());
            segment.Clear();
        }
    }

    private void DrawVolume(Graphics g, RectangleF plot, float top, float bottom, float barWidth, Brush textBrush)
    {
        using var volumeBrush = new SolidBrush(Color.FromArgb(100, UiTheme.Accent));
        g.DrawString("Volume", Font, textBrush, 6 * DpiScale, top);
        double maximum = 0;
        for (var i = _first; i < _first + VisibleCount; i++) maximum = Math.Max(maximum, (double)_series.Points[i].Volume);
        if (maximum <= 0) return;
        for (var i = _first; i < _first + VisibleCount; i++)
        {
            var height = (float)(Math.Max(0, (double)_series.Points[i].Volume) / maximum) * Math.Max(0, bottom - top);
            if (height > 0) g.FillRectangle(volumeBrush, X(i, plot) - barWidth / 2, bottom - height, barWidth, height);
        }
    }

    // Escala fixa de 0 a 100, com as faixas de 30 e 70 usadas pelas regras de sinal do backend.
    private void DrawRsi(Graphics g, RectangleF plot, float top, float bottom, Pen gridPen, Brush textBrush)
    {
        using var guidePen = new Pen(UiTheme.StrongBorder) { DashStyle = DashStyle.Dash };
        using var pen = new Pen(RsiColor, 1.5f * DpiScale);
        g.DrawString("RSI (14)", Font, textBrush, 6 * DpiScale, top);
        var area = new RectangleF(plot.Left, top, plot.Width, Math.Max(1, bottom - top));
        float Y(double value) => area.Top + (float)((100 - value) / 100) * area.Height;
        g.DrawLine(gridPen, plot.Left, top, plot.Right, top);
        foreach (var level in new[] { 70, 30 })
        {
            g.DrawLine(guidePen, plot.Left, Y(level), plot.Right, Y(level));
            var label = level.ToString(CultureInfo.CurrentCulture);
            var size = g.MeasureString(label, Font);
            g.DrawString(label, Font, textBrush, plot.Left - size.Width - 6 * DpiScale, Y(level) - size.Height / 2);
        }
        DrawSeries(g, pen, _series.Rsi14, area, Y);
    }

    // Cada registro ocupa uma faixa de mesma largura; um registro único fica no centro.
    private float X(int index, RectangleF plot) => plot.Left + plot.Width * (index - _first + 0.5f) / VisibleCount;

    // As casas decimais acompanham o intervalo entre as marcas: os rótulos ficam uniformes e cabem na margem do eixo.
    internal static string AxisNumber(double value, double step)
    {
        if (value != 0 && (Math.Abs(value) < 0.0001 || Math.Abs(value) >= 1_000_000))
            return value.ToString("0.###E+0", CultureInfo.CurrentCulture);
        var decimals = step > 0 && double.IsFinite(step) ? Math.Clamp((int)Math.Ceiling(-Math.Log10(step)) + 1, 0, 8) : 2;
        return value.ToString("N" + decimals, CultureInfo.CurrentCulture);
    }

    private int IndexAt(int x)
    {
        var plot = PriceArea;
        var fraction = Math.Clamp((x - plot.Left) / plot.Width, 0, 1);
        return Math.Min(_first + VisibleCount - 1, _first + (int)(fraction * VisibleCount));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_series.Points.Count == 0) return;
        SelectPoint(IndexAt(e.X), false);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Focused) return;
        _selected = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // A roda do mouse e o teclado chegam ao controle que tem o foco.
        Focus();
    }

    // A roda aproxima ou afasta em torno do registro sob o cursor; com Shift, desloca a janela.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_series.Points.Count == 0 || e.Delta == 0) return;
        if ((ModifierKeys & Keys.Shift) != 0) Pan(e.Delta > 0 ? -Math.Max(1, VisibleCount / 10) : Math.Max(1, VisibleCount / 10));
        else Zoom(e.Delta > 0, IndexAt(e.X));
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ResetZoom();
    }

    internal void Zoom(bool closer, int anchor)
    {
        var total = _series.Points.Count;
        if (total <= MinimumVisible) return;
        var count = VisibleCount;
        var target = Math.Clamp(closer ? (int)Math.Floor(count * 0.75) : (int)Math.Ceiling(count / 0.75), MinimumVisible, total);
        if (target == count) return;
        // O registro de referência continua na mesma posição relativa da tela.
        anchor = Math.Clamp(anchor, _first, _first + count - 1);
        var ratio = (anchor - _first + 0.5) / count;
        _first = Math.Clamp((int)Math.Round(anchor + 0.5 - ratio * target), 0, total - target);
        _count = target == total ? 0 : target;
        Invalidate();
    }

    private void Pan(int records)
    {
        if (_count == 0) return;
        _first = Math.Clamp(_first + records, 0, _series.Points.Count - VisibleCount);
        Invalidate();
    }

    private void ResetZoom()
    {
        _first = _count = 0;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_series.Points.Count == 0) return;
        var anchor = _selected >= 0 ? _selected : _first + VisibleCount / 2;
        switch (e.KeyCode)
        {
            case Keys.Add or Keys.Oemplus: Zoom(true, anchor); break;
            case Keys.Subtract or Keys.OemMinus: Zoom(false, anchor); break;
            case Keys.Escape: ResetZoom(); break;
            default:
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
                break;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _tooltip.Hide(this); Invalidate(); }

    private void SelectPoint(int index, bool keyboard)
    {
        if (_selected == index && !keyboard) return;
        _selected = index;
        // Pelo teclado, o registro escolhido pode estar fora da janela ampliada: ela o acompanha.
        if (index < _first) _first = index;
        else if (index >= _first + VisibleCount) _first = index - VisibleCount + 1;
        var point = _series.Points[index];
        var detail = $"{point.Data:dd/MM/yyyy HH:mm zzz}\nFechamento: {_currency} {point.Fechamento:#,0.########}\n" +
                     $"Abertura: {point.Abertura:#,0.########} · Mínima: {point.Minima:#,0.########} · Máxima: {point.Maxima:#,0.########}\nVolume: {point.Volume:#,0.########}" +
                     IndicatorDetail(index);
        AccessibleDescription = detail;
        _tooltip.SetToolTip(this, detail);
        if (keyboard) _tooltip.Show(detail, this, new Point((int)PriceArea.Left, 10), 10000);
        Invalidate();
    }

    // Só os indicadores ligados e já disponíveis naquele registro entram no detalhe.
    private string IndicatorDetail(int index)
    {
        static string Value(double value) => value.ToString("#,0.00##", CultureInfo.CurrentCulture);
        var lines = new List<string>();
        if (_sma20 && _series.Sma20[index] is { } sma20) lines.Add($"SMA 20: {Value(sma20)}");
        if (_sma50 && _series.Sma50[index] is { } sma50) lines.Add($"SMA 50: {Value(sma50)}");
        if (_bollinger && _series.BollingerLower[index] is { } lower && _series.BollingerUpper[index] is { } upper)
            lines.Add($"Bollinger: {Value(lower)} a {Value(upper)}");
        if (_rsi && _series.Rsi14[index] is { } rsi) lines.Add($"RSI (14): {Value(rsi)}");
        return lines.Count == 0 ? "" : "\n" + string.Join(" · ", lines);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltip.Dispose();
        base.Dispose(disposing);
    }
}
