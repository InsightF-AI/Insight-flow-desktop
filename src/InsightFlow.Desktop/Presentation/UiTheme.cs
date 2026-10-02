namespace InsightFlow.Desktop.Presentation;

internal static class UiTheme
{
    // Tokens adaptados do design system do cliente web InsightFlow.
    public static readonly Color Background = Color.FromArgb(7, 11, 20);
    public static readonly Color Surface = Color.FromArgb(15, 22, 38);
    public static readonly Color Elevated = Color.FromArgb(20, 29, 51);
    public static readonly Color MutedSurface = Color.FromArgb(27, 37, 64);
    public static readonly Color Border = Color.FromArgb(30, 41, 63);
    public static readonly Color StrongBorder = Color.FromArgb(44, 59, 94);
    public static readonly Color Ink = Color.FromArgb(238, 242, 249);
    public static readonly Color InkSecondary = Color.FromArgb(167, 179, 201);
    public static readonly Color InkMuted = Color.FromArgb(118, 131, 160);
    public static readonly Color Accent = Color.FromArgb(58, 160, 255);
    public static readonly Color AccentHover = Color.FromArgb(92, 179, 255);
    public static readonly Color Amber = Color.FromArgb(245, 166, 35);
    public static readonly Color Violet = Color.FromArgb(177, 140, 255);
    public static readonly Color Up = Color.FromArgb(34, 197, 138);
    public static readonly Color Down = Color.FromArgb(251, 84, 104);

    // Aliases mantidos para a lógica de apresentação existente.
    public static readonly Color Navy = Ink;
    public static readonly Color Blue = Accent;
    public static readonly Color Muted = InkMuted;
    public static readonly Color Error = Down;

    public static Font Font(float size = 10, bool bold = false) =>
        new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);

    public static Label Label(string text, float size = 10, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Ink,
        Font = Font(size, bold),
        BackColor = Color.Transparent,
        Margin = new Padding(0, 4, 0, 6),
        Anchor = AnchorStyles.Left
    };

    public static Button Button(string text, bool primary = false) => new ThemedButton
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(92, 36),
        Padding = new Padding(10, 3, 10, 3),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance =
        {
            BorderSize = 1,
            BorderColor = primary ? Accent : StrongBorder,
            MouseOverBackColor = primary ? AccentHover : MutedSurface,
            MouseDownBackColor = primary ? Accent : Elevated
        },
        BackColor = primary ? Accent : Elevated,
        ForeColor = primary ? Color.White : InkSecondary,
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 3, 8, 3)
    };

    public static Button NavigationButton(string text, bool active = false) => new ThemedButton
    {
        Text = text,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Height = 43,
        Padding = new Padding(14, 0, 8, 0),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance =
        {
            BorderSize = active ? 1 : 0,
            BorderColor = active ? Color.FromArgb(80, Accent) : Surface,
            MouseOverBackColor = Elevated,
            MouseDownBackColor = MutedSurface
        },
        BackColor = active ? Color.FromArgb(27, 55, 82) : Color.Transparent,
        ForeColor = active ? Ink : InkMuted,
        Font = Font(9.5f, active),
        UseVisualStyleBackColor = false,
        Cursor = active ? Cursors.Default : Cursors.Hand,
        Margin = new Padding(8, 2, 8, 2)
    };

    public static void StyleInput(TextBox input)
    {
        input.BackColor = Elevated;
        input.ForeColor = Ink;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = Font(10);
    }

    public static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = Elevated;
        combo.ForeColor = Ink;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = Font(9.5f);
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += (_, e) =>
        {
            // Na lista aberta, o item sob o cursor ou o teclado precisa se distinguir dos demais.
            var highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var background = new SolidBrush(highlighted ? StrongBorder : Elevated);
            using var foreground = new SolidBrush(Ink);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index >= 0)
            {
                e.Graphics.DrawString(combo.GetItemText(combo.Items[e.Index]), combo.Font, foreground,
                    e.Bounds.Left + 4, e.Bounds.Top + 2);
            }
            e.DrawFocusRectangle();
        };
    }

    public static void SetButtonEnabled(Button button, bool enabled, bool primary = false)
    {
        button.BackColor = enabled ? (primary ? Accent : Elevated) : MutedSurface;
        button.ForeColor = enabled ? (primary ? Color.White : InkSecondary) : InkMuted;
        button.Enabled = enabled;
    }

    public static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        BackgroundColor = Surface,
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Border,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Elevated,
            ForeColor = InkMuted,
            SelectionBackColor = Elevated,
            SelectionForeColor = InkMuted,
            Padding = new Padding(6),
            Font = Font(8.5f, true),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            // Títulos longos quebram em duas linhas em vez de terminar em reticências.
            WrapMode = DataGridViewTriState.True
        },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = InkSecondary,
            SelectionBackColor = MutedSurface,
            SelectionForeColor = Ink,
            Padding = new Padding(6),
            NullValue = "—",
            Font = Font(9.5f)
        },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(17, 25, 43),
            ForeColor = InkSecondary,
            SelectionBackColor = MutedSurface,
            SelectionForeColor = Ink
        },
        RowTemplate = { Height = 40 }
    };
}
