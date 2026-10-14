using Amsterfam.Core.Entities;
using MimeKit;

namespace Amsterfam.Api.Services;

/// <summary>
/// Which address news emails go to (ADR-018): a confirmed custom address if the member set one,
/// otherwise the login address, unless that is the fallback Authentik leaves us when it sends
/// no email claim. A custom address waiting for confirmation never gets news.
/// </summary>
public static class NotificationAddress
{
    /// <summary>Suffix of the address made up when the login has no email claim.</summary>
    public const string FallbackSuffix = "@unknown";

    /// <summary>
    /// Case-sensitive, like the same check in <c>NewsPublisher</c>'s query: the fallback is
    /// always generated in lower case.
    /// </summary>
    public static bool IsFallback(string email) =>
        email.EndsWith(FallbackSuffix, StringComparison.Ordinal);

    public static string? Effective(User user) =>
        user is { NotificationEmail: { } custom, NotificationEmailConfirmedAt: not null } ? custom
        : IsFallback(user.Email) ? null
        : user.Email;

    public static bool IsPending(User user) =>
        user is { NotificationEmail: not null, NotificationEmailConfirmedAt: null };

    /// <summary>
    /// A plain <c>name@domain</c> address: one mailbox, no display name or group, nothing the
    /// fallback could be confused with.
    /// </summary>
    public static bool IsValid(string email) =>
        email.Length <= User.MaxEmailLength
        && !email.EndsWith(FallbackSuffix, StringComparison.OrdinalIgnoreCase)
        && MailboxAddress.TryParse(email, out var mailbox)
        && mailbox.Address == email
        && string.IsNullOrEmpty(mailbox.Name)
        && email.Count(c => c == '@') == 1
        && email.IndexOf('@') is > 0 and var at
        && at < email.Length - 1
        && email[(at + 1)..].Contains('.');
}
