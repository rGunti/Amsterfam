using System.Security.Claims;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amsterfam.Api.Services;

public class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    AmsterfamDbContext db,
    ILogger<CurrentUserService> logger
) : ICurrentUserService
{
    /// <summary>
    /// Emitted by the "auth_source" scope mapping in infra/terraform/authentik/main.tf: the
    /// slug of the user's oldest linked source, or "internal".
    /// </summary>
    public const string AuthSourceClaim = "auth_source";

    public async Task<User> GetOrCreateAsync(CancellationToken ct = default)
    {
        var principal =
            httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context.");

        var externalId =
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Token missing sub claim.");

        var handle =
            principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? externalId;

        var email =
            principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? $"{externalId}@unknown";

        var avatarUrl = principal.FindFirstValue("picture");

        // Missing on tokens issued before the scope mapping existed; keep what we have then.
        var authSource = principal.FindFirstValue(AuthSourceClaim);

        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is not null)
        {
            authSource ??= user.AuthSource;

            if (
                (user.Handle != handle || user.AuthSource != authSource)
                && await HandleTakenAsync(handle, authSource, user.Id, ct)
            )
            {
                // Someone else already has this handle, e.g. two people swapped usernames in
                // Authentik and the other one signed in first. Keep the old one until that
                // sorts itself out on a later sign-in rather than failing the request.
                logger.LogWarning(
                    "Not resyncing handle of user {UserId}: {Handle} is already taken.",
                    user.Id,
                    UserHandle.Format(handle, authSource)
                );
                handle = user.Handle;
                authSource = user.AuthSource;
            }

            if (
                user.Handle != handle
                || user.AuthSource != authSource
                || user.Email != email
                || user.AvatarUrl != avatarUrl
            )
            {
                user.Handle = handle;
                user.AuthSource = authSource;
                user.Email = email;
                user.AvatarUrl = avatarUrl;
                await db.SaveChangesAsync(ct);
            }

            return user;
        }

        user = new User
        {
            ExternalId = externalId,
            Handle = handle,
            AuthSource = authSource,
            Email = email,
            AvatarUrl = avatarUrl,
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            // A parallel request for the same new user (the app fires /me and /events at
            // once on first sign-in) inserted the row first. Use theirs. Anything else is a
            // genuine handle clash with another user (see HandleTakenException).
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, ct)
                ?? throw new HandleTakenException(handle, authSource, ex);
        }

        return user;
    }

    private Task<bool> HandleTakenAsync(
        string handle,
        string? authSource,
        int exceptUserId,
        CancellationToken ct
    ) =>
        db.Users.AnyAsync(
            u => u.Id != exceptUserId && u.Handle == handle && u.AuthSource == authSource,
            ct
        );
}
