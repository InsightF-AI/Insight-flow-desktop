using System.Globalization;
using System.Text.Json;

namespace InsightFlow.Desktop.Presentation;

// Objetos auxiliares da API (parâmetros, valores auxiliares e contexto) não têm schema fixo; a tela mostra "chave: valor".
internal static class JsonText
{
    public static string Summary(JsonElement? value, int maximumLength = 120)
    {
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "—";
        var text = element.ValueKind == JsonValueKind.Object
            ? string.Join(" · ", element.EnumerateObject().Select(property => $"{property.Name.Replace('_', ' ')}: {Scalar(property.Value)}"))
            : Scalar(element);
        if (text.Length == 0) return "—";
        return text.Length <= maximumLength ? text : text[..(maximumLength - 1)] + "…";
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var number) => Number(number),
        // O backend serializa decimais como texto.
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number) => Number(number),
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "sim",
        JsonValueKind.False => "não",
        JsonValueKind.Null => "—",
        _ => value.GetRawText()
    };

    private static string Number(decimal value) => value.ToString("#,0.####", CultureInfo.CurrentCulture);
}
