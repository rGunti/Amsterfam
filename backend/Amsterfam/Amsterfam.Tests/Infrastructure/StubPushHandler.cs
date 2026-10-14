using System.Collections.Concurrent;
using System.Net;

namespace Amsterfam.Tests.Infrastructure;

/// <summary>
/// Stands in for the browsers' push services in tests: records every delivery and accepts it
/// with 201, unless its endpoint is listed in <see cref="Failing"/>.
/// </summary>
public class StubPushHandler : HttpMessageHandler
{
    public record Call(Uri Uri, string? Authorization, byte[] Body);

    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>Endpoint URL → the status every delivery to it gets.</summary>
    public ConcurrentDictionary<string, HttpStatusCode> Failing { get; } = new();

    public IReadOnlyList<Call> CallsTo(string endpoint) =>
        Calls.Where(c => c.Uri.ToString() == endpoint).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Calls.Enqueue(
            new Call(request.RequestUri!, request.Headers.Authorization?.ToString(), body)
        );

        return Failing.TryGetValue(request.RequestUri!.ToString(), out var status)
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(HttpStatusCode.Created);
    }
}

/// <summary>Throwaway keys, generated for the tests only.</summary>
public static class PushTestKeys
{
    public const string VapidPublicKey =
        "BHGX-ILgM_QPwPjN4xskjOD5zZJzKnOyakHgLK7Ulq83xjGEq5vqaGTGZpMHtdspOQV8Bw6sdDFVGKaFon5nmwM";
    public const string VapidPrivateKey = "cEfjRWKIqN8ghjt8TdzNpW6AzLNTb0l0oaJMmRla9pY";
    public const string Subject = "mailto:push@amsterfam.test";

    /// <summary>A browser's subscription keys; the p256dh is a real P-256 point.</summary>
    public const string P256dh =
        "BF9VCLs8NmxJrFP9ykhAXK6OVm32ahafY6jgkTxGkYOll9hR3dTv0iKHNJLvFKCOMDl4AM0frE_0LH8PJydkjIU";
    public const string Auth = "WFGrJpX86AwoVZIwsyvcfw";

    public static string Endpoint(string id) => $"https://fcm.googleapis.com/fcm/send/{id}";
}
