using System.Security.Claims;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amsterfam.Api.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor, AmsterfamDbContext db)
    : ICurrentUserService
{
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

        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is not null)
        {
            if (user.Handle != handle || user.Email != email || user.AvatarUrl != avatarUrl)
            {
                user.Handle = handle;
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
            // once on first sign-in) inserted the row first. Use theirs.
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.FirstAsync(u => u.ExternalId == externalId, ct);
        }

        return user;
    }
}
