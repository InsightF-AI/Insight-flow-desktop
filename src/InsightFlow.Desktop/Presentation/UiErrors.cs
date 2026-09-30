using System.Net;
using InsightFlow.Desktop.Api;

namespace InsightFlow.Desktop.Presentation;

internal static class UiErrors
{
    public static string Message(Exception exception) => exception switch
    {
        ApiException { StatusCode: HttpStatusCode.Unauthorized } => "E-mail ou senha incorretos. Confira seus dados.",
        ApiException { StatusCode: HttpStatusCode.ServiceUnavailable } => "Os dados estão indisponíveis no momento. Tente novamente.",
        ApiException api => api.Message,
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
