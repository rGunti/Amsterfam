using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// The event's Telegram bot and group, which news posts are mirrored to (ADR-015). Organisers
/// only. The bot token is a secret: it's never returned, and never written to the timeline.
/// Setting up takes two steps: save the token, then pick a group the bot is in.
/// </summary>
public static class TelegramEndpoints
{
    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/telegram").RequireAuthorization();
        group.MapGet("/", GetTelegram);
        group.MapPut("/", SetTelegram).LogsToTimeline();
        group.MapDelete("/", RemoveTelegram).LogsToTimeline();
        group.MapGet("/chats", ListChats);
        group.MapPost("/test", SendTest).NotLoggedToTimeline("test message");
        return app;
    }

    private static async Task<IResult> GetTelegram(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (await OrganiserOnly(db, eventId, user.Id) is { } denied)
            return denied;

        return TypedResults.Ok(ToStatus(await db.Events.SingleAsync(e => e.Id == eventId)));
    }

    private static async Task<IResult> SetTelegram(
        Guid eventId,
        [FromBody] SetTelegramRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log,
        TelegramBotApi telegram,
        CancellationToken ct
    )
    {
        var (ev, userId, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;
        if (request.Token is null && request.ChatId is null)
            return TypedResults.BadRequest(new { error = "Give a bot token or pick a group." });

        var wasConfigured = IsConfigured(ev);
        var oldChatId = ev.TelegramChatId;

        if (request.Token is not null)
        {
            var (token, invalid) = TelegramToken.Validate(request.Token);
            if (token is null)
                return TypedResults.BadRequest(new { error = invalid });

            if (token != ev.TelegramBotToken)
            {
                string username;
                try
                {
                    username = await telegram.GetMeAsync(token, ct);
                }
                catch (TelegramApiException ex)
                {
                    return TypedResults.BadRequest(
                        new { error = $"Telegram didn't accept that token. {ex.Message}" }
                    );
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    return Unreachable(ex);
                }

                ev.TelegramBotToken = token;
                ev.TelegramBotUsername = Cut(username, Event.MaxTelegramBotUsernameLength);
                // Another bot may not be in the same group, so the chat is picked again.
                ev.TelegramChatId = null;
                ev.TelegramChatTitle = null;
            }
        }

        if (request.ChatId is { } chatId)
        {
            if (ev.TelegramBotToken is null)
                return TypedResults.BadRequest(new { error = "Save the bot token first." });

            TelegramChat chat;
            try
            {
                chat = await telegram.GetChatAsync(ev.TelegramBotToken, chatId, ct);
            }
            catch (TelegramApiException ex)
            {
                return TypedResults.BadRequest(
                    new
                    {
                        error = $"The bot can't see that group. Add it to the group first. {ex.Message}",
                    }
                );
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return Unreachable(ex);
            }
            if (chat.Type is not ("group" or "supergroup"))
                return TypedResults.BadRequest(new { error = "That chat isn't a group." });

            ev.TelegramChatId = chat.Id;
            ev.TelegramChatTitle = Cut(chat.Title, Event.MaxTelegramChatTitleLength);
        }

        var configured = IsConfigured(ev);
        if (configured != wasConfigured || (configured && ev.TelegramChatId != oldChatId))
        {
            log.Record(eventId, EventLogType.TelegramChanged, userId, data: new { configured });
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return TypedResults.Ok(ToStatus(ev));
    }

    private static async Task<IResult> RemoveTelegram(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (ev, userId, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;

        if (ev.TelegramBotToken is not null || ev.TelegramChatId is not null)
        {
            if (IsConfigured(ev))
            {
                log.Record(
                    eventId,
                    EventLogType.TelegramChanged,
                    userId,
                    data: new { configured = false }
                );
            }
            ev.TelegramBotToken = null;
            ev.TelegramBotUsername = null;
            ev.TelegramChatId = null;
            ev.TelegramChatTitle = null;
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListChats(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TelegramBotApi telegram,
        CancellationToken ct
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (await OrganiserOnly(db, eventId, user.Id) is { } denied)
            return denied;
        var ev = await db.Events.SingleAsync(e => e.Id == eventId, ct);
        if (ev.TelegramBotToken is null)
            return TypedResults.BadRequest(new { error = "Save the bot token first." });

        try
        {
            var groups = await telegram.GetGroupsAsync(ev.TelegramBotToken, ct);
            return TypedResults.Ok(
                groups.Select(g => new TelegramChatOption(g.Id, g.Title, g.Type)).ToList()
            );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Json(
                new { error = $"Couldn't ask Telegram for the bot's groups. {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway
            );
        }
    }

    private static async Task<IResult> SendTest(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TelegramChannel channel,
        ChannelTestThrottle throttle,
        HttpContext http,
        CancellationToken ct
    )
    {
        var (ev, _, error) = await AuthorizeWriteAsync(eventId, currentUser, db);
        if (ev is null)
            return error!;
        if (!IsConfigured(ev))
            return TypedResults.BadRequest(new { error = "Set up a Telegram group first." });
        if (!throttle.TryAcquire(eventId, NotificationChannel.Telegram, out var retryAfter))
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
            await channel.SendTestAsync(ev, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Json(
                new { error = $"Telegram didn't take the test message. {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway
            );
        }
        return TypedResults.NoContent();
    }

    private static bool IsConfigured(Event ev) =>
        ev.TelegramBotToken is not null && ev.TelegramChatId is not null;

    private static string Cut(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    /// <summary>Transport failures carry no secrets: the token is in the path, not the host.</summary>
    private static IResult Unreachable(Exception ex) =>
        TypedResults.Json(
            new { error = $"Couldn't reach Telegram. {ex.Message}" },
            statusCode: StatusCodes.Status502BadGateway
        );

    private static async Task<(Event? Event, int UserId, IResult? Error)> AuthorizeWriteAsync(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (await OrganiserOnly(db, eventId, user.Id) is { } denied)
            return (null, user.Id, denied);
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return (null, user.Id, notWritable);
        return (await db.Events.SingleAsync(e => e.Id == eventId), user.Id, null);
    }

    /// <summary>Like the news endpoints: 404 for non-members (and unknown events), 403 for others.</summary>
    private static async Task<IResult?> OrganiserOnly(
        AmsterfamDbContext db,
        Guid eventId,
        int userId
    ) =>
        await EventGuards.RoleOf(db, eventId, userId) switch
        {
            null => TypedResults.NotFound(),
            AttendanceRole.Organiser => null,
            _ => TypedResults.Forbid(),
        };

    private static TelegramStatus ToStatus(Event ev) =>
        new(
            IsConfigured(ev),
            ev.TelegramBotToken is not null,
            ev.TelegramBotToken is { } token ? TelegramToken.Hint(token) : null,
            ev.TelegramBotUsername,
            ev.TelegramChatId,
            ev.TelegramChatTitle
        );
}
