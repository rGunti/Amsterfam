using System.Text.RegularExpressions;
using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// Checks Discord webhook URLs before they're stored (ADR-015). The server posts to whatever
/// is saved, so only Discord's own webhook endpoint is accepted, never an arbitrary host.
/// </summary>
public static partial class DiscordWebhook
{
    private static readonly string[] Hosts = ["discord.com", "discordapp.com"];

    /// <summary>The normalised URL, or null with an error for the organiser.</summary>
    public static (string? Url, string? Error) Validate(string? input)
    {
        var url = input?.Trim();
        if (string.IsNullOrEmpty(url))
            return (null, "Paste the webhook URL from Discord.");
        if (url.Length > Event.MaxDiscordWebhookUrlLength)
            return (null, "That URL is too long to be a Discord webhook.");
        if (
            !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || !Hosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)
            || !WebhookPath().IsMatch(uri.AbsolutePath)
        )
        {
            return (
                null,
                "That isn't a Discord webhook URL. It should look like https://discord.com/api/webhooks/…"
            );
        }

        return ($"https://{uri.IdnHost.ToLowerInvariant()}{uri.AbsolutePath}", null);
    }

    /// <summary>The last characters of the URL, so organisers can tell webhooks apart.</summary>
    public static string Hint(string url) => url.Length <= 4 ? url : url[^4..];

    [GeneratedRegex(@"^/api/webhooks/\d{1,30}/[A-Za-z0-9_-]{1,200}$")]
    private static partial Regex WebhookPath();
}
