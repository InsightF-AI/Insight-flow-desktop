using System.Net;

namespace InsightFlow.Desktop.Api;

internal static class OptionalRequest
{
    // Para as consultas complementares de uma tela (cotação de cada linha, por exemplo): sem resposta, o dado
    // fica em branco em vez de virar erro. Só a sessão recusada e o cancelamento de quem chamou interrompem a carga.
    public static async Task<T?> OrNullAsync<T>(this Task<T> request, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await request.ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is not ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            return null;
        }
    }
}
