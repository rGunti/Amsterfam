using System.Globalization;
using Amsterfam.Core.Entities;
using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Services;

/// <summary>
/// Mirrors news posts to the event's Telegram group through its bot (ADR-015): the title, the
/// start of the body as plain text and a link back to the post. No parse_mode, so nothing in
/// the post needs escaping. Errors never contain the bot token, which is a secret.
/// </summary>
public class TelegramChannel(TelegramBotApi telegram, IOptions<AppOptions> app)
    : INotificationChannel
{
    /// <summary>Telegram allows 4,096 characters; this leaves room for the title and link.</summary>
    public const int MaxBodyLength = 3500;

    public NotificationChannel Channel => NotificationChannel.Telegram;

    public bool CanDelete => true;

    public bool IsConfigured(Event ev) =>
        ev.TelegramBotToken is not null && ev.TelegramChatId is not null;

    /// <summary>
    /// Returns "{chatId}:{messageId}": message ids are only unique within a chat, and the event
    /// may have moved to another group by the time the post is deleted.
    /// </summary>
    public async Task<string?> SendAsync(
        NotificationDispatch dispatch,
        NewsPost post,
        Event ev,
        CancellationToken ct
    )
    {
        var chatId = ev.TelegramChatId!.Value;
        var messageId = await telegram.SendMessageAsync(
            ev.TelegramBotToken!,
            chatId,
            Text(post),
            ct
        );
        return ExternalId(chatId, messageId);
    }

    public async Task DeleteAsync(NotificationDispatch dispatch, Event ev, CancellationToken ct)
    {
        if (!TryParseExternalId(dispatch.ExternalId, out var chatId, out var messageId))
            return;
        try
        {
            await telegram.DeleteMessageAsync(ev.TelegramBotToken!, chatId, messageId, ct);
        }
        // Already gone, older than the 48 hours Telegram allows, or (after the token was
        // replaced) the bot is no longer in that group: nothing more can be done.
        catch (TelegramApiException ex)
            when (ex.ErrorCode == 403 || (ex.ErrorCode == 400 && IsFinal(ex.Description))) { }
    }

    /// <summary>Sends a short test message to the group, for the settings page.</summary>
    public Task SendTestAsync(Event ev, CancellationToken ct) =>
        telegram.SendMessageAsync(
            ev.TelegramBotToken!,
            ev.TelegramChatId!.Value,
            $"👋 Amsterfam is connected. News for {ev.Name} will show up here.\n{app.Value.NewsUrl(ev.Id)}",
            ct
        );

    public string Text(NewsPost post)
    {
        var title = post.Title is null ? "" : $"{post.Title}\n\n";
        var body = NewsText.Truncate(NewsText.PlainText(post.Body), MaxBodyLength);
        return $"{title}{body}\n\n{app.Value.NewsPostUrl(post.EventId, post.Id)}";
    }

    public static string ExternalId(long chatId, long messageId) =>
        string.Create(CultureInfo.InvariantCulture, $"{chatId}:{messageId}");

    public static bool TryParseExternalId(string? externalId, out long chatId, out long messageId)
    {
        chatId = messageId = 0;
        var parts = externalId?.Split(':');
        return parts is [var chat, var message]
            && long.TryParse(chat, CultureInfo.InvariantCulture, out chatId)
            && long.TryParse(message, CultureInfo.InvariantCulture, out messageId);
    }

    private static bool IsFinal(string description) =>
        description.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase)
        || description.Contains("message can't be deleted", StringComparison.OrdinalIgnoreCase)
        || description.Contains("chat not found", StringComparison.OrdinalIgnoreCase);
}
