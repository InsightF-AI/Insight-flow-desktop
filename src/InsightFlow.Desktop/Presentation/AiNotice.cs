namespace InsightFlow.Desktop.Presentation;

// Aviso obrigatório em toda tela que exibe texto gerado por IA. O texto enviado pela API tem preferência;
// este vale enquanto ela ainda não respondeu ou quando a notificação não traz o aviso.
internal static class AiNotice
{
    public const string Text = "Análise gerada por IA. Não constitui recomendação de investimento.";

    public static string OrDefault(string? fromApi) => string.IsNullOrWhiteSpace(fromApi) ? Text : fromApi;
}
