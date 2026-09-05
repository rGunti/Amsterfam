using System.Security.Claims;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

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

        var displayName =
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
            if (
                user.DisplayName != displayName
                || user.Email != email
                || user.AvatarUrl != avatarUrl
            )
            {
                user.DisplayName = displayName;
                user.Email = email;
                user.AvatarUrl = avatarUrl;
                await db.SaveChangesAsync(ct);
            }

            return user;
        }

        user = new User
        {
            ExternalId = externalId,
            DisplayName = displayName,
            Email = email,
            AvatarUrl = avatarUrl,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
