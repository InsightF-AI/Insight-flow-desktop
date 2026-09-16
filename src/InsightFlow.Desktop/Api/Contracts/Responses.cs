using System.Text.Json.Serialization;

namespace InsightFlow.Desktop.Api.Contracts;

public sealed record TokenResponse
{
    [JsonPropertyName("access_token"), JsonRequired]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("token_type"), JsonRequired]
    public string TokenType { get; init; } = string.Empty;
}

public sealed record SearchAssetResponse
{
    [JsonPropertyName("ticker"), JsonRequired]
    public string Ticker { get; init; } = string.Empty;

    [JsonPropertyName("nome"), JsonRequired]
    public string Nome { get; init; } = string.Empty;

    [JsonPropertyName("tipo"), JsonRequired]
    public string Tipo { get; init; } = string.Empty;

    [JsonPropertyName("moeda"), JsonRequired]
    public string Moeda { get; init; } = string.Empty;

    [JsonPropertyName("setor")]
    public string? Setor { get; init; }
}

public sealed record AssetResponse
{
    [JsonPropertyName("id"), JsonRequired]
    public Guid Id { get; init; }

    [JsonPropertyName("ticker"), JsonRequired]
    public string Ticker { get; init; } = string.Empty;

    [JsonPropertyName("nome"), JsonRequired]
    public string Nome { get; init; } = string.Empty;

    [JsonPropertyName("tipo"), JsonRequired]
    public string Tipo { get; init; } = string.Empty;

    [JsonPropertyName("moeda"), JsonRequired]
    public string Moeda { get; init; } = string.Empty;

    [JsonPropertyName("setor")]
    public string? Setor { get; init; }
}

public sealed record WatchlistItemResponse
{
    [JsonPropertyName("ativo"), JsonRequired]
    public AssetResponse Ativo { get; init; } = new();

    [JsonPropertyName("notificar"), JsonRequired]
    public bool Notificar { get; init; }

    [JsonPropertyName("adicionado_em"), JsonRequired]
    public DateTimeOffset AdicionadoEm { get; init; }
}

public sealed record QuoteResponse
{
    [JsonPropertyName("ticker"), JsonRequired]
    public string Ticker { get; init; } = string.Empty;

    [JsonPropertyName("preco"), JsonRequired]
    public decimal Preco { get; init; }

    [JsonPropertyName("variacao"), JsonRequired]
    public decimal Variacao { get; init; }

    [JsonPropertyName("variacao_percentual"), JsonRequired]
    public decimal VariacaoPercentual { get; init; }

    [JsonPropertyName("maxima_dia"), JsonRequired]
    public decimal MaximaDia { get; init; }

    [JsonPropertyName("minima_dia"), JsonRequired]
    public decimal MinimaDia { get; init; }

    [JsonPropertyName("volume"), JsonRequired]
    public decimal Volume { get; init; }
}

public sealed record HistoryPointResponse
{
    [JsonPropertyName("data"), JsonRequired]
    public DateTimeOffset Data { get; init; }

    [JsonPropertyName("abertura"), JsonRequired]
    public decimal Abertura { get; init; }

    [JsonPropertyName("maxima"), JsonRequired]
    public decimal Maxima { get; init; }

    [JsonPropertyName("minima"), JsonRequired]
    public decimal Minima { get; init; }

    [JsonPropertyName("fechamento"), JsonRequired]
    public decimal Fechamento { get; init; }

    [JsonPropertyName("volume"), JsonRequired]
    public decimal Volume { get; init; }
}
