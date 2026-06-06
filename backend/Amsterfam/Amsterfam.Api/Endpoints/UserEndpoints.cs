using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Amsterfam.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization();

        group.MapGet("/", GetMe);
        group.MapPut("/", UpdateMe);

        return app;
    }

    private static async Task<IResult> GetMe(ICurrentUserService currentUser)
    {
        var user = await currentUser.GetOrCreateAsync();
        return TypedResults.Ok(
            new UserResponse(user.Id, user.DisplayName, user.Email, user.AvatarUrl)
        );
    }

    private static async Task<IResult> UpdateMe(
        [FromBody] UpdateUserRequest request,
        ICurrentUserService currentUser,
        Amsterfam.Infrastructure.AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        user.DisplayName = request.DisplayName;
        user.AvatarUrl = request.AvatarUrl;
        await db.SaveChangesAsync();
        return TypedResults.Ok(
            new UserResponse(user.Id, user.DisplayName, user.Email, user.AvatarUrl)
        );
    }
}
