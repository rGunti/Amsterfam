using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Amsterfam.Api.Endpoints;

public static class EventBannerEndpoints
{
    public const int MaxBannerBytes = 5 * 1024 * 1024;

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

    /// <summary>
    /// Streams the event's banner, or 404 when it has none. Callers must have checked access
    /// already. The file id doubles as the ETag, so revalidation never loads the content.
    /// </summary>
    internal static async Task<IResult> ServeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        HttpRequest request
    )
    {
        request.HttpContext.Response.Headers.CacheControl = "private, no-cache";
        var bannerId = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => e.BannerFileId)
            .FirstOrDefaultAsync();
        if (bannerId is null)
            return TypedResults.NotFound();

        var etag = new EntityTagHeaderValue($"\"{bannerId:N}\"");
        if (
            request
                .GetTypedHeaders()
                .IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: false))
        )
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);

        var file = await db
            .EventFiles.Where(f => f.Id == bannerId)
            .Select(f => new { f.Data, f.ContentType })
            .FirstOrDefaultAsync();
        if (file is null)
            return TypedResults.NotFound();

        return TypedResults.File(file.Data, file.ContentType, entityTag: etag);
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

        if (request.ContentLength > MaxBannerBytes)
            return TooLarge();

        // Content-Length can be absent (chunked), so check the real size too. Kestrel's own body limit bounds the buffer.
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        if (buffer.Length > MaxBannerBytes)
            return TooLarge();
        var data = buffer.ToArray();

        // Trust the bytes, not the client's Content-Type: the sniffed type is what we serve back.
        var contentType = ImageSniffer.Detect(data);
        if (contentType is null)
            return TypedResults.BadRequest(
                new { error = "Banner must be a JPEG, PNG or WebP image." }
            );

        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        var previousId = ev.BannerFileId;

        var file = new EventFile
        {
            EventId = eventId,
            FileName = CleanFileName(fileName),
            ContentType = contentType,
            Size = data.Length,
            Data = data,
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

    private static IResult TooLarge() =>
        TypedResults.Json(
            new { error = $"Banner can be at most {MaxBannerBytes / (1024 * 1024)} MB." },
            statusCode: StatusCodes.Status413PayloadTooLarge
        );

    private static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? "");
        if (string.IsNullOrEmpty(name))
            return "banner";
        return name.Length > EventFile.MaxFileNameLength
            ? name[..EventFile.MaxFileNameLength]
            : name;
    }
}
