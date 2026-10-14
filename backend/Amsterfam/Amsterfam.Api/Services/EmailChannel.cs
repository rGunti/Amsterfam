using System.Net;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace Amsterfam.Api.Services;

/// <summary>
/// Sends news posts by email to members who opted in (ADR-018): one outbox row per member. The
/// address is looked up when sending, so a change made after queueing still counts. The text
/// part is the plain-text body, the HTML part the body rendered without raw HTML; both end with
/// a link to the post and one to the profile page, where email can be turned off.
/// </summary>
public class EmailChannel(
    AmsterfamDbContext db,
    IEmailSender sender,
    IOptions<SmtpOptions> options,
    IOptions<AppOptions> app
) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool CanDelete => false;

    public bool IsConfigured(Event ev) => options.Value.IsEnabled;

    public async Task<string?> SkipReasonAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        if (dispatch.Action != NotificationAction.Send)
            return null;
        var user = await Recipient(dispatch, ct);
        if (user is null || !user.EmailNotifications)
            return "The recipient turned email off.";
        return NotificationAddress.Effective(user) is null
            ? "The recipient has no email address."
            : null;
    }

    public async Task<string?> SendAsync(
        NotificationDispatch dispatch,
        NewsPost post,
        Event ev,
        CancellationToken ct
    )
    {
        var user =
            await Recipient(dispatch, ct)
            ?? throw new InvalidOperationException("The recipient is gone.");
        var to =
            NotificationAddress.Effective(user)
            ?? throw new InvalidOperationException("The recipient has no email address.");

        var message = BuildMessage(post, ev, to, app.Value);
        try
        {
            await sender.SendAsync(message, ct);
        }
        catch (EmailSendException ex) when (ex.Permanent)
        {
            throw new NotificationRejectedException(ex.Message);
        }
        return message.MessageId;
    }

    public Task DeleteAsync(NotificationDispatch dispatch, Event ev, CancellationToken ct) =>
        Task.CompletedTask;

    public static MimeMessage BuildMessage(NewsPost post, Event ev, string to, AppOptions app)
    {
        // The subject already starts with the event name, so an untitled post doesn't repeat it
        // (push has no such prefix and falls back to the event name instead).
        var title = post.Title ?? "News";
        var postUrl = app.NewsPostUrl(ev.Id, post.Id);
        var profileUrl = ProfileUrl(app);

        var text = $"""
            {title}

            {NewsText.PlainText(post.Body)}

            Read it in Amsterfam: {postUrl}

            --
            You get this because you turned on news by email. Manage notifications: {profileUrl}
            """;

        var html = $"""
            <!DOCTYPE html>
            <html>
            <body style="font-family: sans-serif; line-height: 1.5; max-width: 40em;">
            <p style="color: #666; margin: 0;">{Encode(ev.Name)}</p>
            <h1 style="font-size: 1.4em; margin-top: 0.2em;">{Encode(title)}</h1>
            {NewsText.Html(post.Body, app.BaseUrl)}
            <p><a href="{Encode(postUrl)}">View in Amsterfam</a></p>
            <hr>
            <p style="color: #666; font-size: 0.85em;">You get this because you turned on news by email. <a href="{Encode(
                profileUrl
            )}">Manage notifications</a></p>
            </body>
            </html>
            """;

        return Build(to, $"[{ev.Name}] {title}", text, html, app);
    }

    public static string ProfileUrl(AppOptions app) => $"{app.BaseUrl}/profile";

    /// <summary>A message without a sender; <see cref="IEmailSender"/> adds the configured one.</summary>
    public static MimeMessage Build(
        string to,
        string subject,
        string text,
        string html,
        AppOptions app
    )
    {
        var message = new MimeMessage { Subject = subject };
        message.To.Add(MailboxAddress.Parse(to));
        message.MessageId = MimeUtils.GenerateMessageId(
            Uri.TryCreate(app.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "amsterfam"
        );
        message.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();
        return message;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private Task<User?> Recipient(NotificationDispatch dispatch, CancellationToken ct) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dispatch.RecipientUserId, ct);
}
