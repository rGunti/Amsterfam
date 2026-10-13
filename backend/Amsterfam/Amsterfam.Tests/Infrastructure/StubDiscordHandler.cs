using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Amsterfam.Tests.Infrastructure;

/// <summary>
/// Stands in for Discord in tests: records every webhook call and answers like Discord does,
/// with a message id for each post. Webhooks listed in <see cref="Failing"/> get that status.
/// </summary>
public class StubDiscordHandler : HttpMessageHandler
{
    public record Call(HttpMethod Method, Uri Uri, string? Body);

    private long _nextMessageId = 1000;

    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>Webhook id → the status every call to it gets.</summary>
    public ConcurrentDictionary<string, HttpStatusCode> Failing { get; } = new();

    public IReadOnlyList<Call> CallsTo(string webhookId) =>
        Calls.Where(c => c.Uri.AbsolutePath.StartsWith($"/api/webhooks/{webhookId}/")).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Enqueue(new Call(request.Method, request.RequestUri!, body));

        var webhookId = request.RequestUri!.AbsolutePath.Split('/')[3];
        if (Failing.TryGetValue(webhookId, out var status))
            return new HttpResponseMessage(status);
        if (request.Method == HttpMethod.Delete)
            return new HttpResponseMessage(HttpStatusCode.NoContent);

        var id = Interlocked.Increment(ref _nextMessageId);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"id":"{{id}}"}""", Encoding.UTF8, "application/json"),
        };
    }
}
