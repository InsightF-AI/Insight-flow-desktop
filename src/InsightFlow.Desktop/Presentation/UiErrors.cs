using System.Net;
using InsightFlow.Desktop.Api;

namespace InsightFlow.Desktop.Presentation;

internal static class UiErrors
{
    // O backend envia vários detalhes sem acentuação ou com nomes de campo; estes têm redação própria no desktop.
    // Serve apenas à apresentação: o fluxo continua decidido pelo status HTTP, e um texto desconhecido é exibido como veio.
    private static readonly Dictionary<string, string> KnownDetails = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ativo nao encontrado"] = "Ativo não encontrado.",
        ["Ativo ja esta na watchlist"] = "Este ativo já está na sua watchlist.",
        ["Item nao encontrado na watchlist"] = "Este ativo não está mais na sua watchlist. Atualize a lista.",
        ["Cotacao nao encontrada para o ativo"] = "Não há cotação disponível para este ativo.",
        ["Historico nao encontrado para o ativo"] = "Não há histórico disponível para este ativo.",
        ["Regra nao encontrada"] = "A regra deste sinal não foi encontrada.",
        ["Alerta nao encontrado"] = "Este alerta não existe mais. Atualize a lista.",
        ["Notificacao nao encontrada"] = "Esta notificação não existe mais. Atualize a lista.",
        ["Operacao nao encontrada"] = "Esta operação não existe mais. Atualize a lista.",
        ["Nenhuma operacao registrada"] = "Nenhuma operação registrada.",
        ["Dados insuficientes para gerar a analise"] = "Ainda não há dados suficientes para gerar a análise deste ativo.",
        ["A ultima mensagem deve ser do usuario"] = "A última mensagem deve ser do usuário.",
        ["data nao pode ser futura"] = "A data da operação não pode ser futura.",
        ["quantidade deve ser maior que zero"] = "A quantidade deve ser maior que zero.",
        ["preco_unitario deve ser maior que zero"] = "O preço unitário deve ser maior que zero.",
        ["valor_alvo deve ser maior que zero"] = "O preço-alvo deve ser maior que zero.",
        ["Not authenticated"] = "A API não reconheceu a sessão. Saia e entre novamente."
    };

    public static string Message(Exception exception) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.Unauthorized } => "E-mail ou senha incorretos. Confira seus dados.",
        ApiException { StatusCode: HttpStatusCode.ServiceUnavailable } => "Os dados estão indisponíveis no momento. Tente novamente.",
        ApiException api => KnownDetails.GetValueOrDefault(api.Message, api.Message),
        HttpRequestException => "Não foi possível conectar à API. Confira a conexão e o endereço configurado.",
        OperationCanceledException => "A API demorou a responder. Tente novamente.",
        ArgumentException argument => argument.Message.Split(" (Parameter")[0],
        _ => "Não foi possível concluir a operação. Tente novamente."
    };

    // 502 e 503 da IA têm significados próprios no backend (guardrail, cota e provedor desabilitado).
    public static string AiMessage(Exception exception) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.BadGateway } =>
            "A resposta da IA foi bloqueada pelas regras de segurança do servidor e não foi exibida. Tente reformular.",
        ApiException { StatusCode: HttpStatusCode.ServiceUnavailable, RetryAfter: { } wait } =>
            $"A cota do provedor de IA foi excedida. Tente novamente em {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} s.",
        ApiException { StatusCode: HttpStatusCode.ServiceUnavailable } =>
            "A IA está indisponível no servidor no momento. Os dados numéricos continuam disponíveis.",
        _ => Message(exception)
    };
}
