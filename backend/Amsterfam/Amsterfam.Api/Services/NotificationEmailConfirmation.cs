using System.Security.Cryptography;
using System.Text;
using Amsterfam.Core.Entities;
using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Services;

/// <summary>
/// Confirms a custom notification address (ADR-018) with a one-time link. Only the token's hash
/// is stored; the link opens the app, which posts the token back while signed in, so it only
/// works for the member who asked for it. Sent directly rather than through the outbox, which
/// is for news posts.
/// </summary>
public class NotificationEmailConfirmation(IEmailSender sender, IOptions<AppOptions> app)
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Mails a new link to the user's pending address, then puts the new token on
    /// <paramref name="user"/> for the caller to save. If sending throws, nothing changes.
    /// </summary>
    public async Task SendAsync(User user, DateTimeOffset now, CancellationToken ct)
    {
        var token = SecureToken.New();
        var link = $"{app.Value.BaseUrl}/profile/confirm-email?token={token}";
        var handle = UserHandle.Format(user.Handle, user.AuthSource);

        var text = $"""
            Hi {user.DisplayName ?? handle},

            Someone signed in to Amsterfam as {handle} and asked for news to be sent to this address. To confirm, open this link while signed in (it works for one hour):

            {link}

            If this wasn't you, ignore this email and nothing changes.
            """;
        var html = $"""
            <!DOCTYPE html>
            <html>
            <body style="font-family: sans-serif; line-height: 1.5; max-width: 40em;">
            <p>Hi {System.Net.WebUtility.HtmlEncode(user.DisplayName ?? handle)},</p>
            <p>Someone signed in to Amsterfam as <strong>{System.Net.WebUtility.HtmlEncode(
                handle
            )}</strong> and asked for news to be sent to this address. To confirm, open this link while signed in (it works for one hour):</p>
            <p><a href="{link}">Confirm this address</a></p>
            <p style="color: #666;">If this wasn't you, ignore this email and nothing changes.</p>
            </body>
            </html>
            """;

        var message = EmailChannel.Build(
            user.NotificationEmail!,
            "Confirm your email address for Amsterfam",
            text,
            html,
            app.Value
        );
        await sender.SendAsync(message, ct);

        user.NotificationEmailTokenHash = SecureToken.Hash(token);
        user.NotificationEmailTokenSentAt = now;
    }

    public static bool CanResend(User user, DateTimeOffset now) =>
        user.NotificationEmailTokenSentAt is not { } sent || now - sent >= ResendCooldown;

    /// <summary>True if <paramref name="token"/> is the user's pending, unexpired token.</summary>
    public static bool Matches(User user, string? token, DateTimeOffset now)
    {
        if (
            string.IsNullOrEmpty(token)
            || user.NotificationEmailTokenHash is not { } expected
            || user.NotificationEmailTokenSentAt is not { } sent
            || now - sent > TokenLifetime
        )
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(SecureToken.Hash(token)),
            Encoding.ASCII.GetBytes(expected)
        );
    }
}
