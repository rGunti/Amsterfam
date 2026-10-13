using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PushSubscription = Amsterfam.Core.Entities.PushSubscription;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// Web push for news (ADR-017). A member turns it on per device by saving that device's
/// subscription; deleting it turns it off. Everything 404s while push isn't configured.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/push/config", GetConfig).RequireAuthorization();

        var mine = app.MapGroup("/api/v1/me/push-subscriptions").RequireAuthorization();
        mine.MapPut("/", SaveSubscription).NotLoggedToTimeline("user settings");
        mine.MapDelete("/", DeleteSubscription).NotLoggedToTimeline("user settings");
        return app;
    }

    private static IResult GetConfig(IOptions<PushOptions> options) =>
        options.Value.IsEnabled
            ? TypedResults.Ok(new PushConfigResponse(options.Value.VapidPublicKey!))
            : TypedResults.NotFound();

    /// <summary>
    /// Saves this device's subscription. An endpoint is one browser profile, so if it's stored
    /// for someone else, the device changed hands and it moves to the current user.
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
                    Endpoint = request.Endpoint!,
                    P256dh = p256dh!,
                    Auth = auth!,
                    CreatedAt = time.GetUtcNow(),
                }
            );
        }
        else
        {
            existing.UserId = user.Id;
            existing.P256dh = p256dh!;
            existing.Auth = auth!;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (existing is null)
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
}
