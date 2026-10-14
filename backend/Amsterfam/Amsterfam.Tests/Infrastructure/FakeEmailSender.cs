using System.Collections.Concurrent;
using Amsterfam.Api.Services;
using MimeKit;

namespace Amsterfam.Tests.Infrastructure;

/// <summary>
/// Stands in for the mail server in tests: records every message, unless one of its
/// recipients is listed in <see cref="Failing"/>, in which case sending throws.
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

    public Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        if (message.To.Mailboxes.Any(a => Failing.ContainsKey(a.Address)))
            throw new EmailSendException("The mail server couldn't be reached (test).");
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
