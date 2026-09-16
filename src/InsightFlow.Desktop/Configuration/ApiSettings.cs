using System.Text.Json;

namespace InsightFlow.Desktop.Configuration;

public sealed class ApiSettings
{
    public Uri BaseUrl { get; }
    public int TimeoutSeconds { get; }

    private ApiSettings(Uri baseUrl, int timeoutSeconds)
    {
        BaseUrl = baseUrl;
        TimeoutSeconds = timeoutSeconds;
    }

    public static ApiSettings Load()
    {
        const string defaultBaseUrl = "http://localhost:8000";
        var baseUrl = defaultBaseUrl;
        var timeoutSeconds = 30;
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (File.Exists(settingsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("appsettings.json deve conter um objeto JSON.");
                }

                if (document.RootElement.TryGetProperty("Api", out var api))
                {
                    if (api.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidOperationException("A seção Api de appsettings.json deve ser um objeto.");
                    }

                    if (api.TryGetProperty("BaseUrl", out var configuredUrl))
                    {
                        if (configuredUrl.ValueKind != JsonValueKind.String)
                        {
                            throw new InvalidOperationException("Api.BaseUrl deve ser uma URL em texto.");
                        }

                        baseUrl = configuredUrl.GetString() ?? defaultBaseUrl;
                    }

                    if (api.TryGetProperty("TimeoutSeconds", out var configuredTimeout))
                    {
                        if (configuredTimeout.ValueKind != JsonValueKind.Number ||
                            !configuredTimeout.TryGetInt32(out timeoutSeconds))
                        {
                            throw new InvalidOperationException("Api.TimeoutSeconds deve ser um número inteiro.");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("appsettings.json contém JSON inválido.");
            }
            catch (IOException)
            {
                throw new InvalidOperationException("Não foi possível ler appsettings.json.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Não foi possível acessar appsettings.json.");
            }
        }

        var environmentUrl = Environment.GetEnvironmentVariable("INSIGHTFLOW_API_URL");
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            baseUrl = environmentUrl;
        }

        if (timeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("Api.TimeoutSeconds deve estar entre 1 e 300 segundos.");
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(baseUri.Host))
        {
            throw new InvalidOperationException("A URL da API deve ser uma URL HTTP ou HTTPS absoluta.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("A URL da API não pode conter credenciais.");
        }

        if (baseUri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("Configure a raiz do servidor da API, sem /api/v1, caminho, consulta ou fragmento.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException("A URL da API deve usar HTTPS fora do computador local.");
        }

        return new ApiSettings(baseUri, timeoutSeconds);
    }
}
