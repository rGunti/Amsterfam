using System.Text.RegularExpressions;

namespace Amsterfam.Api.Services;

/// <summary>
/// Checks Telegram bot tokens before they're stored (ADR-015). The token ends up in the path
/// of every Bot API call, so only the shape @BotFather hands out is accepted.
/// </summary>
public static partial class TelegramToken
{
    /// <summary>The trimmed token, or null with an error for the organiser.</summary>
    public static (string? Token, string? Error) Validate(string? input)
    {
        var token = input?.Trim();
        if (string.IsNullOrEmpty(token))
            return (null, "Paste the bot token from @BotFather.");
        if (!TokenShape().IsMatch(token))
            return (
                null,
                "That isn't a Telegram bot token. It should look like 123456789:AAE…, as @BotFather shows it."
            );
        return (token, null);
    }

    /// <summary>The last characters of the token, so organisers can tell bots apart.</summary>
    public static string Hint(string token) => token.Length <= 4 ? token : token[^4..];

    [GeneratedRegex(@"^\d{5,16}:[A-Za-z0-9_-]{30,64}$")]
    private static partial Regex TokenShape();
}
