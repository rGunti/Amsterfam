using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// News by email (ADR-018): the opt-in, and an optional custom address that has to be confirmed
/// through a link mailed to it before news goes there. Until then, news keeps going to the login
/// address. Changing the address 404s while SMTP isn't configured.
/// </summary>
public static class NotificationSettingsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationSettingsEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization();
        group.MapGet("/notification-settings", GetSettings);
        group.MapPut("/notification-settings", UpdateSettings).NotLoggedToTimeline("user settings");

        var email = group.MapGroup("/notification-email");
        email.MapPut("/", SetEmail).NotLoggedToTimeline("user settings");
        email.MapDelete("/", ClearEmail).NotLoggedToTimeline("user settings");
        email.MapPost("/resend", Resend).NotLoggedToTimeline("user settings");
        email.MapPost("/confirm", Confirm).NotLoggedToTimeline("user settings");
        return app;
    }

    private static async Task<IResult> GetSettings(
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp
    ) => TypedResults.Ok(ToResponse(await currentUser.GetOrCreateAsync(), smtp.Value));

    /// <summary>
    /// Turning email on needs an address to send to, or one waiting for confirmation (it starts
    /// once confirmed). Turning it off always works.
    /// </summary>
    private static async Task<IResult> UpdateSettings(
        [FromBody] UpdateNotificationSettingsRequest request,
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (
            request.EmailNotifications
            && NotificationAddress.Effective(user) is null
            && !NotificationAddress.IsPending(user)
        )
        {
            return TypedResults.BadRequest(
                new { error = "Add an email address before turning on email." }
            );
        }

        user.EmailNotifications = request.EmailNotifications;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(user, smtp.Value));
    }

    /// <summary>
    /// Saves the address as pending and mails it a confirmation link. If that mail can't be
    /// sent, the address stays pending without a link (503) and can be resent. Shares the
    /// resend cooldown, whatever the address, so it can't be used to mail any inbox at will.
    /// </summary>
    private static async Task<IResult> SetEmail(
        [FromBody] SetNotificationEmailRequest request,
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp,
        NotificationEmailConfirmation confirmation,
        AmsterfamDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!smtp.Value.IsEnabled)
            return TypedResults.NotFound();

        var email = request.Email?.Trim() ?? "";
        if (!NotificationAddress.IsValid(email))
            return TypedResults.BadRequest(new { error = "That isn't a valid email address." });

        var user = await currentUser.GetOrCreateAsync(ct);
        if (
            string.Equals(user.NotificationEmail, email, StringComparison.OrdinalIgnoreCase)
            && user.NotificationEmailConfirmedAt is not null
        )
        {
            return TypedResults.Ok(ToResponse(user, smtp.Value));
        }
        if (!await NotificationEmailConfirmation.TryClaimAsync(db, user, time.GetUtcNow(), ct))
            return TooSoon();

        // A link sent for the previous address must not confirm this one.
        user.NotificationEmail = email;
        user.NotificationEmailConfirmedAt = null;
        user.NotificationEmailTokenHash = null;

        return await SendConfirmationAsync(user, confirmation, db, time, smtp.Value, ct);
    }

    private static async Task<IResult> ClearEmail(
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        user.NotificationEmail = null;
        user.NotificationEmailConfirmedAt = null;
        // TokenSentAt stays, so dropping the address doesn't reset the cooldown.
        user.NotificationEmailTokenHash = null;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(user, smtp.Value));
    }

    private static async Task<IResult> Resend(
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp,
        NotificationEmailConfirmation confirmation,
        AmsterfamDbContext db,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!smtp.Value.IsEnabled)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync(ct);
        if (!NotificationAddress.IsPending(user))
            return TypedResults.Conflict(new { error = "There's no address to confirm." });
        if (!await NotificationEmailConfirmation.TryClaimAsync(db, user, time.GetUtcNow(), ct))
            return TooSoon();

        return await SendConfirmationAsync(user, confirmation, db, time, smtp.Value, ct);
    }

    /// <summary>
    /// Only for the signed-in member the link was sent for: someone else's token, an old one or
    /// one past <see cref="NotificationEmailConfirmation.TokenLifetime"/> is a 400.
    /// </summary>
    private static async Task<IResult> Confirm(
        [FromBody] ConfirmNotificationEmailRequest request,
        ICurrentUserService currentUser,
        IOptions<SmtpOptions> smtp,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        var now = time.GetUtcNow();
        if (
            !NotificationAddress.IsPending(user)
            || !NotificationEmailConfirmation.Matches(user, request.Token, now)
        )
        {
            return TypedResults.BadRequest(new { error = "This link is invalid or has expired." });
        }

        user.NotificationEmailConfirmedAt = now;
        user.NotificationEmailTokenHash = null;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(user, smtp.Value));
    }

    private static async Task<IResult> SendConfirmationAsync(
        User user,
        NotificationEmailConfirmation confirmation,
        AmsterfamDbContext db,
        TimeProvider time,
        SmtpOptions smtp,
        CancellationToken ct
    )
    {
        try
        {
            await confirmation.SendAsync(user, time.GetUtcNow(), ct);
        }
        catch (EmailSendException ex)
        {
            // Saved anyway: the attempt counts towards the cooldown.
            await db.SaveChangesAsync(ct);
            if (ex.Permanent)
            {
                return TypedResults.BadRequest(
                    new { error = "The mail server refused this address." }
                );
            }
            return TypedResults.Json(
                new { error = "The confirmation email couldn't be sent. Try again later." },
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(user, smtp));
    }

    private static IResult TooSoon() =>
        TypedResults.Json(
            new { error = "Wait a minute before asking for another link." },
            statusCode: StatusCodes.Status429TooManyRequests
        );

    private static NotificationSettingsResponse ToResponse(User user, SmtpOptions smtp) =>
        new(
            smtp.IsEnabled,
            user.EmailNotifications,
            NotificationAddress.IsFallback(user.Email) ? null : user.Email,
            user.NotificationEmail,
            user.NotificationEmailConfirmedAt is not null,
            NotificationAddress.Effective(user)
        );
}
