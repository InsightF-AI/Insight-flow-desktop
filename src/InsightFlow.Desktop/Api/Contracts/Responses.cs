using System.Text.Json;
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

public sealed record IndicatorResponse
{
    [JsonPropertyName("tipo"), JsonRequired]
    public string Tipo { get; init; } = string.Empty;

    [JsonPropertyName("parametros"), JsonRequired]
    public JsonElement Parametros { get; init; }

    [JsonPropertyName("data_calculo"), JsonRequired]
    public DateTimeOffset DataCalculo { get; init; }

    [JsonPropertyName("valor"), JsonRequired]
    public decimal Valor { get; init; }

    [JsonPropertyName("valores_auxiliares"), JsonRequired]
    public JsonElement? ValoresAuxiliares { get; init; }
}

public sealed record SignalResponse
{
    [JsonPropertyName("id"), JsonRequired]
    public Guid Id { get; init; }

    [JsonPropertyName("ativo_id"), JsonRequired]
    public Guid AtivoId { get; init; }

    [JsonPropertyName("regra_id"), JsonRequired]
    public Guid RegraId { get; init; }

    [JsonPropertyName("regra_nome"), JsonRequired]
    public string RegraNome { get; init; } = string.Empty;

    [JsonPropertyName("regra_descricao"), JsonRequired]
    public string RegraDescricao { get; init; } = string.Empty;

    [JsonPropertyName("data_ativacao"), JsonRequired]
    public DateTimeOffset DataAtivacao { get; init; }

    [JsonPropertyName("contexto"), JsonRequired]
    public JsonElement Contexto { get; init; }

    [JsonPropertyName("data_desativacao"), JsonRequired]
    public DateTimeOffset? DataDesativacao { get; init; }
}

public sealed record BacktestResponse
{
    [JsonPropertyName("regra_id"), JsonRequired]
    public Guid RegraId { get; init; }

    [JsonPropertyName("ativo_id"), JsonRequired]
    public Guid AtivoId { get; init; }

    [JsonPropertyName("total_ocorrencias"), JsonRequired]
    public int TotalOcorrencias { get; init; }

    [JsonPropertyName("retorno_medio_5_pregoes"), JsonRequired]
    public decimal? RetornoMedio5Pregoes { get; init; }

    [JsonPropertyName("retorno_medio_20_pregoes"), JsonRequired]
    public decimal? RetornoMedio20Pregoes { get; init; }

    [JsonPropertyName("retorno_medio_60_pregoes"), JsonRequired]
    public decimal? RetornoMedio60Pregoes { get; init; }
}

public sealed record AlertResponse
{
    [JsonPropertyName("id"), JsonRequired]
    public Guid Id { get; init; }

    [JsonPropertyName("ativo"), JsonRequired]
    public AssetResponse Ativo { get; init; } = new();

    [JsonPropertyName("tipo_condicao"), JsonRequired]
    public string TipoCondicao { get; init; } = string.Empty;

    [JsonPropertyName("valor_alvo"), JsonRequired]
    public decimal ValorAlvo { get; init; }

    [JsonPropertyName("moeda_alvo"), JsonRequired]
    public string MoedaAlvo { get; init; } = string.Empty;

    [JsonPropertyName("habilitado"), JsonRequired]
    public bool Habilitado { get; init; }

    // Verdadeiro quando a condição estava satisfeita na última avaliação do backend.
    [JsonPropertyName("ultimo_estado"), JsonRequired]
    public bool UltimoEstado { get; init; }

    [JsonPropertyName("disparado_em"), JsonRequired]
    public DateTimeOffset? DisparadoEm { get; init; }

    [JsonPropertyName("criado_em"), JsonRequired]
    public DateTimeOffset CriadoEm { get; init; }

    [JsonPropertyName("atualizado_em"), JsonRequired]
    public DateTimeOffset AtualizadoEm { get; init; }
}
