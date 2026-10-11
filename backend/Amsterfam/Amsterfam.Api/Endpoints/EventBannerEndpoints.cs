using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class EventBannerEndpoints
{
    public static IEndpointRouteBuilder MapEventBannerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/banner").RequireAuthorization();

        group.MapGet("/", GetBanner);
        group.MapPut("/", PutBanner).LogsToTimeline();
        group.MapDelete("/", DeleteBanner).LogsToTimeline();

        return app;
    }

    private static async Task<IResult> GetBanner(
        Guid eventId,
        HttpRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (!await EventGuards.IsMember(db, eventId, user.Id))
            return TypedResults.NotFound();
        if (await EventGuards.EnsureVisibleAsync(db, eventId, user.Id) is { } hidden)
            return hidden;

        return await ServeAsync(db, eventId, request);
    }

    /// <summary>Streams the event's banner, or 404 when it has none. Callers must have checked access already.</summary>
    internal static async Task<IResult> ServeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        HttpRequest request
    )
    {
        var bannerId = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => e.BannerFileId)
            .FirstOrDefaultAsync();
        return await ImageUpload.ServeAsync(db, bannerId, request);
    }

    private static async Task<IResult> PutBanner(
        Guid eventId,
        string? fileName,
        HttpRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (await EnsureOrganiserAsync(db, eventId, user.Id) is { } denied)
            return denied;
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var (image, invalid) = await ImageUpload.ReadAsync(request, "Banner");
        if (image is null)
            return invalid!;

        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        var previousId = ev.BannerFileId;

        var file = new EventFile
        {
            EventId = eventId,
            FileName = ImageUpload.CleanFileName(fileName, "banner"),
            ContentType = image.ContentType,
            Size = image.Data.Length,
            Data = image.Data,
            UploadedById = user.Id,
            CreatedAt = time.GetUtcNow(),
        };
        db.EventFiles.Add(file);
        ev.BannerFileId = file.Id;
        log.Record(
            eventId,
            EventLogType.EventDetailsUpdated,
            user.Id,
            data: new { changed = new[] { "banner" } }
        );
        await db.SaveChangesAsync();

        if (previousId is { } old)
            await db.EventFiles.Where(f => f.Id == old).ExecuteDeleteAsync();

        return TypedResults.Ok(new { bannerFileId = file.Id });
    }

    private static async Task<IResult> DeleteBanner(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        if (await EnsureOrganiserAsync(db, eventId, user.Id) is { } denied)
            return denied;
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        if (ev.BannerFileId is not { } bannerId)
            return TypedResults.NoContent();

        ev.BannerFileId = null;
        log.Record(
            eventId,
            EventLogType.EventDetailsUpdated,
            user.Id,
            data: new { changed = new[] { "banner" } }
        );
        await db.SaveChangesAsync();
        await db.EventFiles.Where(f => f.Id == bannerId).ExecuteDeleteAsync();
        return TypedResults.NoContent();
    }

    /// <summary>404 for non-members (events stay invisible to them), 403 for other members.</summary>
    private static async Task<IResult?> EnsureOrganiserAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int userId
    )
    {
        if (await EventGuards.IsOrganiser(db, eventId, userId))
            return null;
        return await EventGuards.IsMember(db, eventId, userId)
            ? TypedResults.Forbid()
            : TypedResults.NotFound();
    }
}
