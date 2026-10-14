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
        catch (AuthenticationException)
        {
            throw new EmailSendException("The mail server rejected the login.");
        }
        catch (SmtpCommandException ex)
        {
            throw new EmailSendException(
                $"The mail server refused the message ({(int)ex.StatusCode})."
            );
        }
        catch (Exception ex)
            when (!ct.IsCancellationRequested
                && ex
                    is SmtpProtocolException
                        or IOException
                        or System.Net.Sockets.SocketException
                        or SslHandshakeException
                        or TimeoutException
                        or OperationCanceledException
            )
        {
            throw new EmailSendException(
                $"The mail server couldn't be reached ({ex.GetType().Name})."
            );
        }
    }
}

public class EmailSendException(string message) : Exception(message);
