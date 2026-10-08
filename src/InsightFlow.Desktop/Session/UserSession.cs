namespace InsightFlow.Desktop.Session;

public sealed class UserSession
{
    private readonly object _gate = new();
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset? _accessTokenExpiresAt;
    private int _version;

    public string? AccessToken
    {
        get { lock (_gate) { return _accessToken; } }
    }

    public string? RefreshToken
    {
        get { lock (_gate) { return _refreshToken; } }
    }

    public DateTimeOffset? AccessTokenExpiresAt
    {
        get { lock (_gate) { return _accessTokenExpiresAt; } }
    }

    // Identifica o login: muda ao entrar e ao sair, mas não quando os tokens do mesmo login são renovados.
    public int Version
    {
        get { lock (_gate) { return _version; } }
    }

    public void Start(string token, string? refreshToken = null, DateTimeOffset? expiresAt = null)
    {
        Validate(token, nameof(token));
        if (refreshToken is not null) Validate(refreshToken, nameof(refreshToken));

        lock (_gate)
        {
            _accessToken = token;
            _refreshToken = refreshToken;
            _accessTokenExpiresAt = expiresAt;
            _version++;
        }
    }

    // Troca os tokens do login identificado por "version"; se o usuário saiu ou entrou de novo nesse meio-tempo, nada muda.
    public bool Renew(int version, string token, string refreshToken, DateTimeOffset expiresAt)
    {
        Validate(token, nameof(token));
        Validate(refreshToken, nameof(refreshToken));

        lock (_gate)
        {
            if (_version != version || _accessToken is null) return false;
            _accessToken = token;
            _refreshToken = refreshToken;
            _accessTokenExpiresAt = expiresAt;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _accessToken = null;
            _refreshToken = null;
            _accessTokenExpiresAt = null;
            _version++;
        }
    }

    internal (int Version, string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt) Snapshot()
    {
        lock (_gate) { return (_version, _accessToken, _refreshToken, _accessTokenExpiresAt); }
    }

    private static void Validate(string token, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, parameterName);
        if (token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("O token de sessão contém caracteres inválidos.", parameterName);
        }
    }
}
