using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Amsterfam.Api.Services;

/// <summary>Hands a finished message to the mail server; replaced by a fake in tests.</summary>
public interface IEmailSender
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}

/// <summary>
/// Sends through <see cref="SmtpOptions"/> with MailKit, one connection per message: news goes
/// out a few times a year to a few dozen people, so pooling isn't worth it. Failures are
/// rethrown with a message of our own, since the outbox stores it and MailKit's can carry
/// server replies.
/// </summary>
public class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var smtp = options.Value;
        message.From.Clear();
        message.From.Add(MailboxAddress.Parse(smtp.From!));

        using var client = new SmtpClient { Timeout = 15_000 };
        try
        {
            await client.ConnectAsync(
                smtp.Host!,
                smtp.Port,
                smtp.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct
            );
            if (!string.IsNullOrEmpty(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && Translate(ex) is { } error)
        {
            throw error;
        }
    }

    /// <summary>
    /// MailKit's failures as our own messages, or null for anything else. Only a 5xx refusal of
    /// the recipient is permanent: other 5xx replies (sender refused, relaying denied) are about
    /// our setup, and should be retried once it's fixed rather than fail every queued row.
    /// </summary>
    public static EmailSendException? Translate(Exception ex) =>
        ex switch
        {
            AuthenticationException => new("The mail server rejected the login."),
            ServiceNotAuthenticatedException => new("The mail server requires a login."),
            SmtpCommandException command => new(
                $"The mail server refused the message ({(int)command.StatusCode}).",
                permanent: command.ErrorCode == SmtpErrorCode.RecipientNotAccepted
                    && (int)command.StatusCode >= 500
            ),
            SmtpProtocolException
            or ServiceNotConnectedException
            or IOException
            or System.Net.Sockets.SocketException
            or SslHandshakeException
            or TimeoutException
            or OperationCanceledException => new(
                $"The mail server couldn't be reached ({ex.GetType().Name})."
            ),
            _ => null,
        };
}

/// <summary>
/// Sending failed; the message is our own. <see cref="Permanent"/> means retrying won't help.
/// </summary>
public class EmailSendException(string message, bool permanent = false) : Exception(message)
{
    public bool Permanent { get; } = permanent;
}
