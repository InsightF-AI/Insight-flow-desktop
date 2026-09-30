using System.Net;

namespace InsightFlow.Desktop.Api;

public sealed class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public TimeSpan? RetryAfter { get; }

    public ApiException(HttpStatusCode statusCode, string message, TimeSpan? retryAfter = null) : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
