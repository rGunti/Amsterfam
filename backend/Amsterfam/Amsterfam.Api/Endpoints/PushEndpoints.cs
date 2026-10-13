using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PushSubscription = Amsterfam.Core.Entities.PushSubscription;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// Web push for news (ADR-017). A member turns it on per device by saving that device's
/// subscription; deleting it turns it off. They can see, rename and revoke all their devices.
/// Saving 404s while push isn't configured.
/// </summary>
public static class PushEndpoints
{
    private const string UnnamedDevice = "Unknown device";

    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/push/config", GetConfig).RequireAuthorization();

        var mine = app.MapGroup("/api/v1/me/push-subscriptions").RequireAuthorization();
        mine.MapGet("/", ListDevices);
        mine.MapPut("/", SaveSubscription).NotLoggedToTimeline("user settings");
        mine.MapDelete("/", DeleteSubscription).NotLoggedToTimeline("user settings");
        mine.MapPatch("/{id:int}", RenameDevice).NotLoggedToTimeline("user settings");
        mine.MapDelete("/{id:int}", RevokeDevice).NotLoggedToTimeline("user settings");
        return app;
    }

    private static IResult GetConfig(IOptions<PushOptions> options) =>
        options.Value.IsEnabled
            ? TypedResults.Ok(new PushConfigResponse(options.Value.VapidPublicKey!))
            : TypedResults.NotFound();

    private static async Task<IResult> ListDevices(
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        var devices = await db
            .PushSubscriptions.Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.LastSuccessAt ?? s.CreatedAt)
            .Select(s => new PushDeviceResponse(
                s.Id,
                s.Name,
                s.Endpoint,
                s.CreatedAt,
                s.LastSuccessAt
            ))
            .ToListAsync();
        return TypedResults.Ok(devices);
    }

    /// <summary>
    /// Saves this device's subscription. An endpoint is one browser profile, so if it's stored
    /// for someone else, the device changed hands and it moves to the current user. A member
    /// past <see cref="PushSubscription.MaxPerUser"/> devices loses the one used least recently.
    /// </summary>
    private static async Task<IResult> SaveSubscription(
        [FromBody] SavePushSubscriptionRequest request,
        IOptions<PushOptions> options,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        if (!options.Value.IsEnabled)
            return TypedResults.NotFound();

        var p256dh = request.Keys?.P256dh;
        var auth = request.Keys?.Auth;
        if (PushEndpointValidator.Validate(request.Endpoint, p256dh, auth) is { } error)
            return TypedResults.BadRequest(new { error });

        var user = await currentUser.GetOrCreateAsync();
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s =>
            s.Endpoint == request.Endpoint
        );
        if (existing is null)
        {
            db.PushSubscriptions.Add(
                new PushSubscription
                {
                    UserId = user.Id,
                    Name = CleanName(request.Name) ?? UnnamedDevice,
                    Endpoint = request.Endpoint!,
                    P256dh = p256dh!,
                    Auth = auth!,
                    CreatedAt = time.GetUtcNow(),
                }
            );
        }
        else
        {
            // A device that changed hands gets the new owner's name for it.
            if (existing.UserId != user.Id)
                existing.Name = CleanName(request.Name) ?? UnnamedDevice;
            existing.UserId = user.Id;
            existing.P256dh = p256dh!;
            existing.Auth = auth!;
        }

        var oldest = await db
            .PushSubscriptions.Where(s => s.UserId == user.Id && s.Endpoint != request.Endpoint)
            .OrderByDescending(s => s.LastSuccessAt ?? s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(PushSubscription.MaxPerUser - 1)
            .ToListAsync();
        db.PushSubscriptions.RemoveRange(oldest);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (existing is null
                && ex.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            // The same device subscribed twice at once; the other request stored it.
        }
        return TypedResults.NoContent();
    }

    /// <summary>Removes the caller's own subscription; anyone else's is left alone, quietly.</summary>
    private static async Task<IResult> DeleteSubscription(
        [FromBody] DeletePushSubscriptionRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (string.IsNullOrEmpty(request.Endpoint))
            return TypedResults.BadRequest(new { error = "The subscription has no endpoint." });

        var user = await currentUser.GetOrCreateAsync();
        await db
            .PushSubscriptions.Where(s => s.Endpoint == request.Endpoint && s.UserId == user.Id)
            .ExecuteDeleteAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RenameDevice(
        int id,
        [FromBody] RenamePushDeviceRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (CleanName(request.Name) is not { } name)
            return TypedResults.BadRequest(new { error = "The device needs a name." });

        var user = await currentUser.GetOrCreateAsync();
        var renamed = await db
            .PushSubscriptions.Where(s => s.Id == id && s.UserId == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, name));
        return renamed == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// Revokes one of the caller's devices from anywhere. The device itself doesn't hear about
    /// it right away: it gets nothing more, and the app unsubscribes it in the browser on its
    /// next start, when the server no longer lists it.
    /// </summary>
    private static async Task<IResult> RevokeDevice(
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        var revoked = await db
            .PushSubscriptions.Where(s => s.Id == id && s.UserId == user.Id)
            .ExecuteDeleteAsync();
        return revoked == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length <= PushSubscription.MaxNameLength)
            return trimmed;

        var cut = trimmed[..PushSubscription.MaxNameLength];
        // Half an emoji (a lone high surrogate) can't be stored as UTF-8.
        if (char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut.TrimEnd();
    }
}
