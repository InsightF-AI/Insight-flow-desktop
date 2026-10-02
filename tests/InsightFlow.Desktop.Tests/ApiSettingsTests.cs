using InsightFlow.Desktop.Configuration;

namespace InsightFlow.Desktop.Tests;

public sealed class ApiSettingsTests
{
    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/")]
    [InlineData("http://127.0.0.1:8000", "http://127.0.0.1:8000/")]
    [InlineData("http://[::1]:8000", "http://[::1]:8000/")]
    [InlineData("  https://api.example.test  ", "https://api.example.test/")]
    public void Accepts_loopback_over_http_and_remote_servers_over_https(string configured, string expected)
    {
        var settings = ApiSettings.Create(configured, 30);

        Assert.Equal(expected, settings.BaseUrl.AbsoluteUri);
        Assert.Equal(30, settings.TimeoutSeconds);
    }

    [Theory]
    [InlineData("http://api.example.test", "HTTPS")]
    [InlineData("https://user:secret@api.example.test", "credenciais")]
    [InlineData("https://api.example.test/api/v1", "raiz do servidor")]
    [InlineData("https://api.example.test/?debug=1", "raiz do servidor")]
    [InlineData("ftp://api.example.test", "HTTP ou HTTPS")]
    [InlineData("api.example.test", "HTTP ou HTTPS")]
    public void Rejects_urls_that_would_expose_the_session_or_break_the_route_prefix(string configured, string expectedReason)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiSettings.Create(configured, 30));

        Assert.Contains(expectedReason, error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Rejects_timeouts_outside_the_supported_range(int timeoutSeconds)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiSettings.Create("http://localhost:8000", timeoutSeconds));

        Assert.Contains("TimeoutSeconds", error.Message);
    }
}
