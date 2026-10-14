using System.Collections.Concurrent;
using Amsterfam.Api.Services;
using MimeKit;

namespace Amsterfam.Tests.Infrastructure;

/// <summary>
/// Stands in for the mail server in tests: records every message, unless one of its
/// recipients is listed in <see cref="Failing"/> (sending throws, worth retrying) or
/// <see cref="Rejecting"/> (sending throws for good, like a 5xx).
/// </summary>
public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<MimeMessage> Sent { get; } = new();

    public ConcurrentDictionary<string, bool> Failing { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MimeMessage> SentTo(string address) =>
        Sent.Where(m =>
                m.To.Mailboxes.Any(a =>
                    string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();

    public ConcurrentDictionary<string, bool> Rejecting { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        if (message.To.Mailboxes.Any(a => Rejecting.ContainsKey(a.Address)))
            throw new EmailSendException("The mail server refused the message (550).", true);
        if (message.To.Mailboxes.Any(a => Failing.ContainsKey(a.Address)))
            throw new EmailSendException("The mail server couldn't be reached (test).");
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
