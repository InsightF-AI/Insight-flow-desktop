namespace InsightFlow.Desktop.Presentation;

internal sealed class ThemedButton : Button
{
    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (Enabled)
        {
            base.OnPaint(pevent);
            return;
        }

        pevent.Graphics.Clear(BackColor);
        if (FlatAppearance.BorderSize > 0)
        {
            using var border = new Pen(UiTheme.StrongBorder);
            pevent.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft => TextFormatFlags.Left,
            ContentAlignment.MiddleRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        var textBounds = Rectangle.Inflate(ClientRectangle, -Padding.Horizontal / 2, -Padding.Vertical / 2);
        TextRenderer.DrawText(pevent.Graphics, Text, Font, textBounds, ForeColor, flags);
    }
}
