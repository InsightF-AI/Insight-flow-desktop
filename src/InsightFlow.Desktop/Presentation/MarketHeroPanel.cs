using System.Drawing.Drawing2D;

namespace InsightFlow.Desktop.Presentation;

internal sealed class MarketHeroPanel : Panel
{
    public MarketHeroPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = UiTheme.Background;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(12, 25, 46), UiTheme.Background, LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillRectangle(background, ClientRectangle);

        using var gridPen = new Pen(Color.FromArgb(24, 58, 83, 120), 1);
        for (var x = 28; x < Width; x += 48) e.Graphics.DrawLine(gridPen, x, 0, x, Height);
        for (var y = 28; y < Height; y += 48) e.Graphics.DrawLine(gridPen, 0, y, Width, y);

        if (Width < 180 || Height < 180) return;
        var chartBounds = new Rectangle(38, Height / 2, Math.Max(80, Width - 76), Math.Max(80, Height / 3));
        var points = new[]
        {
            new PointF(chartBounds.Left, chartBounds.Bottom - 20),
            new PointF(chartBounds.Left + chartBounds.Width * .15f, chartBounds.Bottom - 38),
            new PointF(chartBounds.Left + chartBounds.Width * .30f, chartBounds.Bottom - 30),
            new PointF(chartBounds.Left + chartBounds.Width * .46f, chartBounds.Bottom - 70),
            new PointF(chartBounds.Left + chartBounds.Width * .62f, chartBounds.Bottom - 54),
            new PointF(chartBounds.Left + chartBounds.Width * .78f, chartBounds.Top + 35),
            new PointF(chartBounds.Right, chartBounds.Top + 12)
        };

        using var glowPen = new Pen(Color.FromArgb(48, UiTheme.Accent), 8) { LineJoin = LineJoin.Round };
        using var linePen = new Pen(UiTheme.Accent, 2.5f) { LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(glowPen, points);
        e.Graphics.DrawLines(linePen, points);

        using var accentBrush = new SolidBrush(UiTheme.Up);
        e.Graphics.FillEllipse(accentBrush, points[^1].X - 4, points[^1].Y - 4, 8, 8);
    }
}
