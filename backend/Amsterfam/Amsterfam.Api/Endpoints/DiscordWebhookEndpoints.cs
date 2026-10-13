using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// The event's Discord webhook, which news posts are mirrored to (ADR-015). Organisers only.
/// The URL is a secret: it's never returned, and never written to the timeline.
/// </summary>
public static class DiscordWebhookEndpoints
{
    public static IEndpointRouteBuilder MapDiscordWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/discord-webhook")
            .RequireAuthorization();
        group.MapGet("/", GetWebhook);
        group.MapPut("/", SetWebhook).LogsToTimeline();
        group.MapDelete("/", RemoveWebhook).LogsToTimeline();
        group.MapPost("/test", SendTest).NotLoggedToTimeline("test message");
        return app;
    }

    private static async Task<IResult> GetWebhook(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null)
            return TypedResults.NotFound();
        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsOrganiser(db, eventId, user.Id))
            return TypedResults.Forbid();

        return TypedResults.Ok(ToStatus(ev));
    }

    private static async Task<IResult> SetWebhook(
        Guid eventId,
        [FromBody] SetDiscordWebhookRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (ev, userId, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;

        var (url, invalid) = DiscordWebhook.Validate(request.Url);
        if (url is null)
            return TypedResults.BadRequest(new { error = invalid });

        if (ev.DiscordWebhookUrl != url)
        {
            ev.DiscordWebhookUrl = url;
            log.Record(
                eventId,
                EventLogType.DiscordWebhookChanged,
                userId,
                data: new { configured = true }
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.Ok(ToStatus(ev));
    }

    private static async Task<IResult> RemoveWebhook(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (ev, userId, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;

        if (ev.DiscordWebhookUrl is not null)
        {
            ev.DiscordWebhookUrl = null;
            log.Record(
                eventId,
                EventLogType.DiscordWebhookChanged,
                userId,
                data: new { configured = false }
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SendTest(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        DiscordChannel discord,
        DiscordTestThrottle throttle,
        HttpContext http,
        CancellationToken ct
    )
    {
        var (ev, _, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;
        if (ev.DiscordWebhookUrl is null)
            return TypedResults.BadRequest(new { error = "Set up a Discord webhook first." });
        if (!throttle.TryAcquire(eventId, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            http.Response.Headers.RetryAfter = seconds.ToString();
            return TypedResults.Json(
                new { error = $"A test was just sent. Try again in {seconds} s." },
                statusCode: StatusCodes.Status429TooManyRequests
            );
        }

        try
        {
            await discord.SendTestAsync(ev, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Json(
                new { error = $"Discord didn't take the test message. {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway
            );
        }
        return TypedResults.NoContent();
    }

    private static async Task<(Event? Event, int UserId, IResult? Error)> AuthorizeWriteAsync(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return (null, 0, notWritable);
        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsOrganiser(db, eventId, user.Id))
            return (null, user.Id, TypedResults.Forbid());
        return (await db.Events.SingleAsync(e => e.Id == eventId), user.Id, null);
    }

    private static DiscordWebhookStatus ToStatus(Event ev) =>
        ev.DiscordWebhookUrl is { } url
            ? new DiscordWebhookStatus(true, DiscordWebhook.Hint(url))
            : new DiscordWebhookStatus(false, null);
}
