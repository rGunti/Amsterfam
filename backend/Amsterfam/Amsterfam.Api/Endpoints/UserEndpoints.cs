using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class UserEndpoints
{
    private const int MinBirthYear = 1900;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").RequireAuthorization();

        group.MapGet("/", GetMe);
        group.MapPut("/", UpdateMe).NotLoggedToTimeline("Profile changes aren't tied to an event.");
        group
            .MapPut("/about", UpdateAbout)
            .NotLoggedToTimeline("Profile changes aren't tied to an event.");

        app.MapGet("/api/v1/users/{handle}/profile", GetProfile).RequireAuthorization();
        app.MapGet("/api/v1/dietary-options", GetDietaryOptions).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetMe(ICurrentUserService currentUser, AmsterfamDbContext db)
    {
        var user = await currentUser.GetOrCreateAsync();
        await db.Entry(user).Collection(u => u.DietaryOptions).LoadAsync();
        return TypedResults.Ok(ToResponse(user));
    }

    private static async Task<IResult> UpdateMe(
        [FromBody] UpdateUserRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var displayName = Clean(request.DisplayName);
        if (TooLong("Display name", displayName, User.MaxDisplayNameLength) is { } error)
            return TypedResults.BadRequest(new { error });

        var user = await currentUser.GetOrCreateAsync();
        user.DisplayName = displayName;
        user.AvatarUrl = request.AvatarUrl;
        await db.SaveChangesAsync();

        await db.Entry(user).Collection(u => u.DietaryOptions).LoadAsync();
        return TypedResults.Ok(ToResponse(user));
    }

    private static async Task<IResult> UpdateAbout(
        [FromBody] UpdateAboutRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var pronouns = Clean(request.Pronouns);
        var location = Clean(request.Location);
        var bio = Clean(request.Bio);
        var dietaryNotes = Clean(request.DietaryNotes);

        var lengthError =
            TooLong("Pronouns", pronouns, User.MaxPronounsLength)
            ?? TooLong("Location", location, User.MaxLocationLength)
            ?? TooLong("Bio", bio, User.MaxBioLength)
            ?? TooLong("Dietary notes", dietaryNotes, User.MaxDietaryNotesLength);
        if (lengthError is not null)
            return TypedResults.BadRequest(new { error = lengthError });

        if (request.Birthday is { } birthday && !IsValidBirthday(birthday, time))
            return TypedResults.BadRequest(new { error = "That birthday isn't a valid date." });

        var optionIds = (request.DietaryOptionIds ?? []).Distinct().ToList();
        var options = await db.DietaryOptions.Where(o => optionIds.Contains(o.Id)).ToListAsync();
        if (options.Count != optionIds.Count)
            return TypedResults.BadRequest(new { error = "Unknown dietary option." });

        var user = await currentUser.GetOrCreateAsync();
        await db.Entry(user).Collection(u => u.DietaryOptions).LoadAsync();

        user.Pronouns = pronouns;
        user.Location = location;
        user.Bio = bio;
        user.DietaryNotes = dietaryNotes;
        user.BirthdayDay = (short?)request.Birthday?.Day;
        user.BirthdayMonth = (short?)request.Birthday?.Month;
        user.BirthYear = (short?)request.Birthday?.Year;
        user.DietaryOptions.Clear();
        foreach (var option in options)
            user.DietaryOptions.Add(option);

        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(user));
    }

    /// <summary>
    /// Looks the user up by their qualified handle ("klaus@discord"). Unknown handles get the
    /// same 404 as profiles you can't see, so handles can't be probed.
    /// </summary>
    private static async Task<IResult> GetProfile(
        string handle,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var viewer = await currentUser.GetOrCreateAsync();

        // Routing decodes every escape in a route value except %2F, which would otherwise
        // be a path separator. Handles may contain "/", so undo just that one; decoding
        // everything again would mangle a handle that literally contains "%41". The cost: a
        // handle containing the literal text "%2F" can't be looked up. Rare enough to accept.
        handle = handle.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

        var userId = await FindByProfileHandle(db, handle);
        if (userId is null)
            return TypedResults.NotFound();

        if (viewer.Id != userId && !await CanSeeProfile(db, viewer.Id, userId.Value))
            return TypedResults.NotFound();

        var user = await db
            .Users.AsNoTracking()
            .Include(u => u.DietaryOptions)
            .FirstAsync(u => u.Id == userId);

        return TypedResults.Ok(
            new UserProfileResponse(
                user.Id,
                user.Handle,
                user.AuthSource,
                UserHandle.Format(user.Handle, user.AuthSource),
                user.DisplayName,
                user.AvatarUrl,
                user.Pronouns,
                user.Location,
                user.Bio,
                ToBirthday(user),
                ToOptions(user),
                user.DietaryNotes
            )
        );
    }

    /// <summary>
    /// Tries "handle@source" first, then the whole string as a handle whose source isn't
    /// known yet — an internal account's handle may itself contain an "@".
    /// </summary>
    private static async Task<int?> FindByProfileHandle(AmsterfamDbContext db, string qualified)
    {
        var (handle, authSource) = UserHandle.Parse(qualified);
        var id = await db
            .Users.Where(u => u.Handle == handle && u.AuthSource == authSource)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();
        if (id is not null || authSource is null)
            return id;

        return await db
            .Users.Where(u => u.Handle == qualified && u.AuthSource == null)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Profiles are visible to people you're actually on a trip with: confirmed members see
    /// each other, and organisers also see who's pending on their event, to help decide
    /// whether to confirm them. Everyone else gets the same 404 as an unknown id, so ids
    /// can't be probed.
    /// </summary>
    private static Task<bool> CanSeeProfile(AmsterfamDbContext db, int viewerId, int targetId) =>
        db.EventAttendances.AnyAsync(v =>
            v.UserId == viewerId
            && v.Event.Attendances.Any(t =>
                t.UserId == targetId
                && (
                    (
                        (v.Role == AttendanceRole.Attendee || v.Role == AttendanceRole.Organiser)
                        && (t.Role == AttendanceRole.Attendee || t.Role == AttendanceRole.Organiser)
                    ) || (v.Role == AttendanceRole.Organiser && t.Role == AttendanceRole.Pending)
                )
            )
        );

    private static async Task<IResult> GetDietaryOptions(AmsterfamDbContext db)
    {
        var options = await db
            .DietaryOptions.OrderBy(o => o.SortOrder)
            .Select(o => new DietaryOptionResponse(o.Id, o.Key, o.Label))
            .ToListAsync();
        return TypedResults.Ok(options);
    }

    private static UserResponse ToResponse(User user) =>
        new(
            user.Id,
            user.Handle,
            user.AuthSource,
            UserHandle.Format(user.Handle, user.AuthSource),
            user.DisplayName,
            user.Email,
            user.AvatarUrl,
            user.Pronouns,
            user.Location,
            user.Bio,
            ToBirthday(user),
            ToOptions(user),
            user.DietaryNotes
        );

    private static Birthday? ToBirthday(User user) =>
        user is { BirthdayMonth: { } month, BirthdayDay: { } day }
            ? new Birthday(month, day, user.BirthYear)
            : null;

    private static List<DietaryOptionResponse> ToOptions(User user) =>
        user
            .DietaryOptions.OrderBy(o => o.SortOrder)
            .Select(o => new DietaryOptionResponse(o.Id, o.Key, o.Label))
            .ToList();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? TooLong(string field, string? value, int max) =>
        value is not null && value.Length > max
            ? $"{field} can be at most {max} characters."
            : null;

    private static bool IsValidBirthday(Birthday birthday, TimeProvider time)
    {
        if (birthday.Month is < 1 or > 12 || birthday.Day < 1)
            return false;
        if (birthday.Year is { } year && (year < MinBirthYear || year > time.GetUtcNow().Year))
            return false;
        // Without a year, check against a leap year so 29 February is allowed.
        return birthday.Day <= DateTime.DaysInMonth(birthday.Year ?? 2000, birthday.Month);
    }
}
