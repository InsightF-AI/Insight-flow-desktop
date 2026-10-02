namespace InsightFlow.Desktop.Presentation;

// As células guardam o valor tipado e a formatação pt-BR fica no estilo: ordenar por uma coluna segue o valor, não o texto.
internal static class GridCells
{
    public const string Number = "#,0.########";
    public const string Date = "dd/MM/yyyy";
    public const string DateTime = "dd/MM/yyyy HH:mm";
    // Os valores já chegam multiplicados por 100; o símbolo entre aspas não reaplica a escala.
    public const string Share = "#,0.00'%'";
    public const string SignedPercent = "+#,0.00'%';-#,0.00'%';0.00'%'";
    private const string Amount = "#,0.00######";

    public static string Money(string currency)
    {
        var code = currency.Replace("'", "").Trim();
        // A seção negativa mantém o sinal junto do número: "BRL -2,50", não "-BRL 2,50".
        return code.Length == 0 ? Amount : $"'{code}' {Amount};'{code}' -{Amount}";
    }

    public static DataGridViewTextBoxColumn Column(string name, float weight, Type? valueType = null, string? format = null, bool alignRight = false) => new()
    {
        Name = name,
        HeaderText = name.ToUpperInvariant(),
        FillWeight = weight,
        ValueType = valueType ?? typeof(string),
        DefaultCellStyle =
        {
            Format = format ?? "",
            Alignment = alignRight ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.NotSet
        }
    };

    public static void SetFormat(this DataGridViewRow row, string format, params string[] columns)
    {
        foreach (var column in columns) row.Cells[column].Style.Format = format;
    }
}
