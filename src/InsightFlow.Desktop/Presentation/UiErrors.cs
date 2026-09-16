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
        _ => "Não foi possível concluir a operação. Tente novamente."
    };
}
