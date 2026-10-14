using MimeKit;

namespace Amsterfam.Api.Services;

/// <summary>
/// SMTP settings for news by email (ADR-018). Without a host, email is off: nothing is queued
/// and the profile page hides the setting.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>The sender, e.g. <c>Amsterfam &lt;news@example.com&gt;</c>.</summary>
    public string? From { get; set; }

    /// <summary>Require STARTTLS. Turn off only for a local test server like Mailpit.</summary>
    public bool StartTls { get; set; } = true;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Host);

    public bool HasValidFrom() =>
        !IsEnabled || (MailboxAddress.TryParse(From, out var from) && from.Address.Contains('@'));
}
