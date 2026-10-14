using System.Net;

namespace Amsterfam.Api.Services;

/// <summary>
/// A channel was told to slow down. The outbox waits <see cref="RetryAfter"/> before the next
/// attempt instead of its usual backoff (ADR-015).
/// </summary>
public class NotificationRateLimitedException(string message, TimeSpan retryAfter)
    : HttpRequestException(message, null, HttpStatusCode.TooManyRequests)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
