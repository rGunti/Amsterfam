using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Amsterfam.Api.Services;

/// <summary>A group the bot can see.</summary>
public record TelegramChat(long Id, string Title, string Type);

/// <summary>Telegram refused a call; the message is Telegram's description, never the URL.</summary>
public class TelegramApiException(int errorCode, string description)
    : HttpRequestException(
        $"Telegram answered {errorCode}: {description}",
        null,
        (HttpStatusCode)errorCode
    )
{
    public int ErrorCode { get; } = errorCode;
    public string Description { get; } = description;
}

/// <summary>
/// The few Bot API calls Amsterfam makes (ADR-015). The host is fixed, so a stored token can
/// only ever reach Telegram. The token is part of every URL, so this client's HTTP logging is
/// turned off where it's registered, and errors only carry Telegram's own description.
/// </summary>
public class TelegramBotApi(HttpClient http)
{
    public const string BaseAddress = "https://api.telegram.org/";

    private static readonly string[] GroupTypes = ["group", "supergroup"];

    /// <summary>Update fields that carry the chat they happened in.</summary>
    private static readonly string[] ChatCarriers =
    [
        "message",
        "edited_message",
        "channel_post",
        "my_chat_member",
        "chat_member",
    ];

    /// <summary>The bot's @username, which also proves the token works.</summary>
    public async Task<string> GetMeAsync(string token, CancellationToken ct)
    {
        using var result = await CallAsync(token, "getMe", new { }, ct);
        return result.RootElement.GetProperty("result").GetProperty("username").GetString()!;
    }

    /// <summary>The chat's title, if the bot can see it.</summary>
    public async Task<TelegramChat> GetChatAsync(string token, long chatId, CancellationToken ct)
    {
        using var result = await CallAsync(token, "getChat", new { chat_id = chatId }, ct);
        return ReadChat(result.RootElement.GetProperty("result"))!;
    }

    /// <summary>
    /// Groups the bot has heard from in the last day. Updates aren't confirmed (no offset), so
    /// asking again shows the same groups.
    /// </summary>
    public async Task<IReadOnlyList<TelegramChat>> GetGroupsAsync(
        string token,
        CancellationToken ct
    )
    {
        using var result = await CallAsync(token, "getUpdates", new { timeout = 0 }, ct);
        return ParseGroups(result.RootElement.GetProperty("result"));
    }

    /// <summary>Sends plain text (no parse_mode, so nothing needs escaping); returns the message id.</summary>
    public async Task<long> SendMessageAsync(
        string token,
        long chatId,
        string text,
        CancellationToken ct
    )
    {
        using var result = await CallAsync(
            token,
            "sendMessage",
            new
            {
                chat_id = chatId,
                text,
                link_preview_options = new { is_disabled = true },
            },
            ct
        );
        return result.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
    }

    public async Task DeleteMessageAsync(
        string token,
        long chatId,
        long messageId,
        CancellationToken ct
    )
    {
        using var _ = await CallAsync(
            token,
            "deleteMessage",
            new { chat_id = chatId, message_id = messageId },
            ct
        );
    }

    /// <summary>
    /// The groups in a getUpdates result, most recently seen title first wins; private chats and
    /// channels are left out.
    /// </summary>
    public static IReadOnlyList<TelegramChat> ParseGroups(JsonElement updates)
    {
        var groups = new Dictionary<long, TelegramChat>();
        foreach (var update in updates.EnumerateArray())
        {
            foreach (var carrier in ChatCarriers)
            {
                if (
                    update.TryGetProperty(carrier, out var payload)
                    && payload.TryGetProperty("chat", out var chatJson)
                    && ReadChat(chatJson) is { } chat
                    && GroupTypes.Contains(chat.Type)
                )
                {
                    // Updates come oldest first, so a later title (after a rename) replaces it.
                    groups[chat.Id] = chat;
                }
            }
        }
        return groups
            .Values.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static TelegramChat? ReadChat(JsonElement chat)
    {
        if (!chat.TryGetProperty("id", out var id) || !chat.TryGetProperty("type", out var type))
            return null;
        var title = chat.TryGetProperty("title", out var t) ? t.GetString() : null;
        return new TelegramChat(
            id.GetInt64(),
            title ?? id.GetInt64().ToString(),
            type.GetString()!
        );
    }

    private async Task<JsonDocument> CallAsync(
        string token,
        string method,
        object body,
        CancellationToken ct
    )
    {
        using var response = await http.PostAsJsonAsync(
            $"{BaseAddress}bot{token}/{method}",
            body,
            ct
        );
        JsonDocument json;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new TelegramApiException((int)response.StatusCode, "an unreadable answer");
        }

        var root = json.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return json;

        using (json)
        {
            var code =
                root.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var n)
                    ? n
                    : (int)response.StatusCode;
            var description = root.TryGetProperty("description", out var d)
                ? d.GetString() ?? "no reason given"
                : "no reason given";
            var parameters = root.TryGetProperty("parameters", out var p) ? p : default;

            if (
                code == 429
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("retry_after", out var ra)
                && ra.TryGetInt32(out var seconds)
            )
            {
                throw new NotificationRateLimitedException(
                    $"Telegram answered 429: {description}",
                    TimeSpan.FromSeconds(Math.Max(1, seconds))
                );
            }
            if (
                parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("migrate_to_chat_id", out _)
            )
            {
                description =
                    $"{description}. The group was upgraded; pick it again in the notification settings";
            }
            throw new TelegramApiException(code, description);
        }
    }
}
