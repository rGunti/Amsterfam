using System.Net;
using System.Text.Json;
using Amsterfam.Core.Entities;
using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Services;

/// <summary>
/// Mirrors news posts to the event's Discord channel through its webhook (ADR-015): the title,
/// the start of the Markdown body and a link back to the post. Mentions are never parsed, so
/// a post can't ping @everyone. Errors never contain the webhook URL, which is a secret.
/// </summary>
public class DiscordChannel(HttpClient http, IOptions<AppOptions> app) : INotificationChannel
{
    /// <summary>Discord allows 2,000 characters; this leaves room for the title and link.</summary>
    public const int MaxBodyLength = 1500;

    public NotificationChannel Channel => NotificationChannel.Discord;

    public bool CanDelete => true;

    public bool IsConfigured(Event ev) => ev.DiscordWebhookUrl is not null;

    public async Task<string?> SendAsync(
        NotificationDispatch dispatch,
        NewsPost post,
        Event ev,
        CancellationToken ct
    ) => await PostAsync(ev.DiscordWebhookUrl!, Content(post), ct);

    public async Task DeleteAsync(NotificationDispatch dispatch, Event ev, CancellationToken ct)
    {
        using var response = await http.DeleteAsync(
            $"{ev.DiscordWebhookUrl}/messages/{Uri.EscapeDataString(dispatch.ExternalId!)}",
            ct
        );
        // Already gone (deleted by hand in Discord) is as good as deleted.
        if (response.StatusCode != HttpStatusCode.NotFound)
            EnsureSuccess(response);
    }

    /// <summary>Sends a short test message to a webhook, for the settings page.</summary>
    public Task SendTestAsync(Event ev, CancellationToken ct) =>
        PostAsync(
            ev.DiscordWebhookUrl!,
            $"👋 Amsterfam is connected. News for **{ev.Name}** will show up here.\n{app.Value.NewsUrl(ev.Id)}",
            ct
        );

    public string Content(NewsPost post)
    {
        var title = post.Title is null ? "" : $"**{post.Title}**\n\n";
        var body = NewsText.DiscordMarkdown(post.Body, MaxBodyLength);
        return $"{title}{body}\n\n{app.Value.NewsPostUrl(post.EventId, post.Id)}";
    }

    private async Task<string?> PostAsync(string webhookUrl, string content, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"{webhookUrl}?wait=true",
            new { content, allowed_mentions = new { parse = Array.Empty<string>() } },
            ct
        );
        EnsureSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return json.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Discord answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode
            );
    }
}
