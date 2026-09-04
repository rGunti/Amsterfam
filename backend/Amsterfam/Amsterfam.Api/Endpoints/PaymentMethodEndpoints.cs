using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class PaymentMethodEndpoints
{
    public static IEndpointRouteBuilder MapPaymentMethodEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/v1/me/payment-methods").RequireAuthorization();

        mine.MapGet("/", GetMine);
        mine.MapPost("/", Create);
        mine.MapPut("/{id:int}", Update);
        mine.MapDelete("/{id:int}", Delete);

        app.MapGet("/api/v1/users/{userId:int}/payment-methods", GetForUser).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetMine(
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        var methods = await QueryFor(db, user.Id).ToListAsync();
        return TypedResults.Ok(methods);
    }

    private static async Task<IResult> GetForUser(int userId, AmsterfamDbContext db)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!exists)
            return TypedResults.NotFound();

        var methods = await QueryFor(db, userId).ToListAsync();
        return TypedResults.Ok(methods);
    }

    private static async Task<IResult> Create(
        [FromBody] UpsertPaymentMethodRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();

        var method = new PaymentMethod
        {
            UserId = user.Id,
            Title = request.Title,
            Icon = request.Icon,
            Link = request.Link,
            Description = request.Description,
        };

        db.PaymentMethods.Add(method);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/v1/me/payment-methods/{method.Id}", ToResponse(method));
    }

    private static async Task<IResult> Update(
        int id,
        [FromBody] UpsertPaymentMethodRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();

        var method = await db.PaymentMethods.FirstOrDefaultAsync(p =>
            p.Id == id && p.UserId == user.Id
        );

        if (method is null)
            return TypedResults.NotFound();

        method.Title = request.Title;
        method.Icon = request.Icon;
        method.Link = request.Link;
        method.Description = request.Description;

        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(method));
    }

    private static async Task<IResult> Delete(
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();

        var method = await db.PaymentMethods.FirstOrDefaultAsync(p =>
            p.Id == id && p.UserId == user.Id
        );

        if (method is null)
            return TypedResults.NotFound();

        db.PaymentMethods.Remove(method);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static IQueryable<PaymentMethodResponse> QueryFor(AmsterfamDbContext db, int userId) =>
        db
            .PaymentMethods.Where(p => p.UserId == userId)
            .Select(p => new PaymentMethodResponse(p.Id, p.Title, p.Icon, p.Link, p.Description));

    private static PaymentMethodResponse ToResponse(PaymentMethod method) =>
        new(method.Id, method.Title, method.Icon, method.Link, method.Description);
}
