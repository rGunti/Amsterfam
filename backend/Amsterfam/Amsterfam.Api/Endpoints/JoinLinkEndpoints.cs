using System.Security.Cryptography;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class JoinLinkEndpoints
{
    public static IEndpointRouteBuilder MapJoinLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var managed = app.MapGroup("/api/v1/events/{eventId:guid}/join-links")
            .RequireAuthorization();
        managed.MapGet("/", GetLinks);
        managed.MapPost("/", CreateLink);
        managed.MapDelete("/{id:int}", RevokeLink);

        var redeem = app.MapGroup("/api/v1/join-links/{token}").RequireAuthorization();
        redeem.MapGet("/", Preview);
        redeem.MapPost("/join", Join);

        return app;
    }

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JoinLinkResponse ToResponse(EventJoinLink l, DateTimeOffset now) =>
        new(
            l.Id,
            l.Token,
            l.Kind.ToString(),
            l.CreatedById,
            l.CreatedAt,
            l.ExpiresAt,
            l.MaxUses,
            l.UseCount,
            l.RevokedAt is not null,
            l.IsUsable(now)
        );

    private static async Task<IResult> GetLinks(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId))
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsOrganiser(db, eventId, user.Id))
            return TypedResults.Forbid();

        var isOwner = await EventGuards.IsOwner(db, eventId, user.Id);
        var query = db.EventJoinLinks.Where(l => l.EventId == eventId && l.RevokedAt == null);
        if (!isOwner)
            query = query.Where(l => l.Kind == JoinLinkKind.Attendee);

        var links = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
        var now = time.GetUtcNow();
        return TypedResults.Ok(links.Select(l => ToResponse(l, now)).ToList());
    }

    private static async Task<IResult> CreateLink(
        Guid eventId,
        [FromBody] CreateJoinLinkRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsOrganiser(db, eventId, user.Id))
            return TypedResults.Forbid();

        var kind = JoinLinkKind.Attendee;
        if (
            !string.IsNullOrEmpty(request.Kind)
            && !Enum.TryParse(request.Kind, ignoreCase: true, out kind)
        )
            return TypedResults.BadRequest(new { error = "Unknown link kind." });

        if (kind == JoinLinkKind.Organiser && !await EventGuards.IsOwner(db, eventId, user.Id))
            return TypedResults.Forbid();

        var now = time.GetUtcNow();
        if (request.ExpiresAt is { } exp && exp <= now)
            return TypedResults.BadRequest(new { error = "Expiry must be in the future." });
        if (request.MaxUses is < 1)
            return TypedResults.BadRequest(new { error = "Max uses must be at least 1." });

        var link = new EventJoinLink
        {
            EventId = eventId,
            Token = NewToken(),
            Kind = kind,
            CreatedById = user.Id,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt,
            MaxUses = request.MaxUses,
        };
        db.EventJoinLinks.Add(link);
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/v1/events/{eventId}/join-links/{link.Id}",
            ToResponse(link, now)
        );
    }

    private static async Task<IResult> RevokeLink(
        Guid eventId,
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsOrganiser(db, eventId, user.Id))
            return TypedResults.Forbid();

        var link = await db.EventJoinLinks.FirstOrDefaultAsync(l =>
            l.Id == id && l.EventId == eventId
        );
        if (link is null)
            return TypedResults.NotFound();

        var isOwner = await EventGuards.IsOwner(db, eventId, user.Id);
        if (!isOwner && (link.CreatedById != user.Id || link.Kind == JoinLinkKind.Organiser))
            return TypedResults.Forbid();

        link.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Attendee links need an event that accepts RSVPs; organiser links also work while the
    /// event is still a draft so the owner can bring co-organisers on early.
    /// </summary>
    private static bool CanJoin(EventStatus status, JoinLinkKind kind) =>
        kind == JoinLinkKind.Organiser
            ? !EventStateMachine.IsReadOnly(status)
            : EventStateMachine.AcceptsJoins(status);

    private static async Task<IResult> Preview(
        string token,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var link = await db
            .EventJoinLinks.Include(l => l.Event)
            .FirstOrDefaultAsync(l => l.Token == token);

        if (
            link is null
            || !link.IsUsable(time.GetUtcNow())
            || !CanJoin(link.Event.Status, link.Kind)
        )
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        var member = await EventGuards.IsMember(db, link.EventId, user.Id);

        return TypedResults.Ok(
            new JoinLinkPreviewResponse(
                link.EventId,
                link.Event.Name,
                link.Event.Location,
                link.Event.StartDate,
                link.Event.EndDate,
                link.Kind.ToString(),
                member
            )
        );
    }

    private static async Task<IResult> Join(
        string token,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var now = time.GetUtcNow();
        var link = await db
            .EventJoinLinks.Include(l => l.Event)
            .FirstOrDefaultAsync(l => l.Token == token);

        if (link is null || !link.IsUsable(now) || !CanJoin(link.Event.Status, link.Kind))
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (await EventGuards.IsMember(db, link.EventId, user.Id))
            return TypedResults.Conflict(new { error = "Already attending this event." });

        await using var tx = await db.Database.BeginTransactionAsync();

        // Conditional increment so concurrent redemptions can't exceed MaxUses.
        var claimed = await db
            .EventJoinLinks.Where(l =>
                l.Id == link.Id
                && l.RevokedAt == null
                && (l.ExpiresAt == null || l.ExpiresAt > now)
                && (l.MaxUses == null || l.UseCount < l.MaxUses)
            )
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UseCount, l => l.UseCount + 1));
        if (claimed == 0)
            return TypedResults.NotFound();

        db.EventAttendances.Add(
            new EventAttendance
            {
                EventId = link.EventId,
                UserId = user.Id,
                Role = AttendanceRole.Pending,
                RequestedOrganiser = link.Kind == JoinLinkKind.Organiser,
            }
        );

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Conflict(new { error = "Already attending this event." });
        }

        await tx.CommitAsync();
        return TypedResults.Created(
            $"/api/v1/events/{link.EventId}/attendees/{user.Id}",
            new { eventId = link.EventId }
        );
    }
}
