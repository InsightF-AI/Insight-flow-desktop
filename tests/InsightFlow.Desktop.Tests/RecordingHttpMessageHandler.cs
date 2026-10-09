using System.Net;
using System.Text;

namespace InsightFlow.Desktop.Tests;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    string? ContentType,
    string? Body);

internal sealed class RecordingHttpMessageHandler(
    Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public static RecordingHttpMessageHandler ReturningJson(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(Json(body, statusCode)));

    public static HttpResponseMessage Json(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."),
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

        // As repetições após a renovação do token podem chegar de threads diferentes.
        lock (Requests) Requests.Add(recorded);
        return await respond(recorded, cancellationToken);
    }
}
