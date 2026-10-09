using System.Globalization;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Configuration;
using InsightFlow.Desktop.Forms;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        ApplicationConfiguration.Initialize();
        ApiSettings settings;
        try
        {
            settings = ApiSettings.Load();
        }
        catch (Exception exception)
        {
            // ApiSettings descreve o problema em InvalidOperationException; outras falhas não expõem detalhes internos.
            var reason = exception is InvalidOperationException ? exception.Message : "Não foi possível carregar a configuração.";
            MessageBox.Show($"{reason}\n\nConfira a URL e o timeout da API no appsettings.json ou na variável INSIGHTFLOW_API_URL.",
                "InsightFlow — Configuração", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = settings.BaseUrl,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };
        var session = new UserSession();
        var api = new InsightFlowApiClient(httpClient, session);
        var notifications = new NotificationStream(settings.BaseUrl, session, api);
        var revocation = Task.CompletedTask;
        string? notice = null;
        try
        {
            while (true)
            {
                using (var login = new LoginForm(api, session, settings.BaseUrl, notice))
                {
                    login.ShowDialog();
                    if (!login.Authenticated) break;
                }

                using var main = new MainForm(api, session, settings.BaseUrl, notifications);
                main.ShowDialog();
                revocation = main.Revocation;
                if (!main.LogoutRequested) break;
                notice = main.SessionExpired ? "Sua sessão expirou. Entre novamente para continuar." : "Você saiu da conta.";
            }
        }
        finally
        {
            session.Clear();
            // Ao fechar o aplicativo, dá um instante para a revogação do login chegar ao servidor.
            revocation.Wait(TimeSpan.FromSeconds(3));
        }
    }
}
