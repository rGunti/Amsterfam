using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Amsterfam.Tests.Infrastructure;

/// <summary>
/// Stands in for the Telegram Bot API in tests: records every call and answers like Telegram
/// does. Any well-formed token is a working bot; groups in <see cref="Chats"/> are the ones
/// every bot can see.
/// </summary>
public class StubTelegramHandler : HttpMessageHandler
{
    public record Call(string Token, string Method, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement;
    }

    public record Failure(int Code, string Description, int? RetryAfter = null);

    private long _nextMessageId = 500;

    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>Chat id → title of a group the bots are in.</summary>
    public ConcurrentDictionary<long, string> Chats { get; } = new();

    /// <summary>Token → the raw getUpdates result (a JSON array); "[]" when not set.</summary>
    public ConcurrentDictionary<string, string> Updates { get; } = new();

    /// <summary>"{token}" or "{token}/{method}" → the error every matching call gets.</summary>
    public ConcurrentDictionary<string, Failure> Failing { get; } = new();

    public IReadOnlyList<Call> CallsBy(string token, string? method = null) =>
        Calls.Where(c => c.Token == token && (method is null || c.Method == method)).ToList();

    public static string Token(int n) => $"{7_000_000 + n}:AAH-secret_token_value_{n:D10}xyz";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Assert.Equal("api.telegram.org", request.RequestUri!.Host);
        // /bot{token}/{method}
        var segments = request.RequestUri.AbsolutePath.Split('/');
        var token = segments[1]["bot".Length..];
        var method = segments[2];
        var body = request.Content is null
            ? "{}"
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Enqueue(new Call(token, method, body));

        if (
            Failing.TryGetValue($"{token}/{method}", out var failure)
            || Failing.TryGetValue(token, out failure)
        )
            return Error(failure);

        var json = JsonDocument.Parse(body).RootElement;
        return method switch
        {
            "getMe" => Ok(
                $$"""{"id":{{token.Split(':')[0]}},"is_bot":true,"username":"amsterfam_{{token.Split(':')[0]}}_bot"}"""
            ),
            "getChat" => Chats.TryGetValue(json.GetProperty("chat_id").GetInt64(), out var title)
                ? Ok(
                    JsonSerializer.Serialize(
                        new
                        {
                            id = json.GetProperty("chat_id").GetInt64(),
                            type = "supergroup",
                            title,
                        }
                    )
                )
                : Error(new Failure(400, "Bad Request: chat not found")),
            "getUpdates" => Ok(Updates.GetValueOrDefault(token, "[]")),
            "sendMessage" => Ok(
                JsonSerializer.Serialize(
                    new
                    {
                        message_id = Interlocked.Increment(ref _nextMessageId),
                        chat = new
                        {
                            id = json.GetProperty("chat_id").GetInt64(),
                            type = "supergroup",
                        },
                    }
                )
            ),
            "deleteMessage" => Ok("true"),
            _ => Error(new Failure(404, "Not Found")),
        };
    }

    private static HttpResponseMessage Ok(string result) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"ok":true,"result":{{result}}}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static HttpResponseMessage Error(Failure f) =>
        new((HttpStatusCode)f.Code)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        ok = false,
                        error_code = f.Code,
                        description = f.Description,
                        parameters = f.RetryAfter is { } s ? new { retry_after = s } : null,
                    }
                ),
                Encoding.UTF8,
                "application/json"
            ),
        };
}
