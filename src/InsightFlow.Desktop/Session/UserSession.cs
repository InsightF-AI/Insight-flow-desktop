namespace InsightFlow.Desktop.Session;

public sealed class UserSession
{
    private readonly object _gate = new();
    private string? _accessToken;
    private int _version;

    public string? AccessToken
    {
        get { lock (_gate) { return _accessToken; } }
    }

    public int Version
    {
        get { lock (_gate) { return _version; } }
    }

    public void Start(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("O token de sessão contém caracteres inválidos.", nameof(token));
        }

        lock (_gate)
        {
            _accessToken = token;
            _version++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _accessToken = null;
            _version++;
        }
    }
}
