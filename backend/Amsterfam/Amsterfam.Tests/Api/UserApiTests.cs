using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class UserApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GetMe_Returns401_WhenUnauthenticated()
    {
        var client = api.CreateClient();
        var response = await client.GetAsync("/api/v1/me/");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FirstRequests_InParallel_CreateTheUserOnce()
    {
        // The app shell fires /me and /events at the same time on first sign-in.
        var client = api.CreateClientWithUser("discord|parallel-first-login");

        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i => client.GetAsync(i % 2 == 0 ? "/api/v1/me/" : "/api/v1/events/"))
        );

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var db = await api.CreateDbContextAsync();
        Assert.Equal(
            1,
            await db.Users.CountAsync(u => u.ExternalId == "discord|parallel-first-login")
        );
    }

    [Fact]
    public async Task GetMe_AutoCreatesUser_OnFirstRequest()
    {
        var client = api.CreateClientWithUser("discord|new-user-1");
        var response = await client.GetAsync("/api/v1/me/");

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.NotNull(user);
        Assert.Equal("Test User discord|new-user-1", user.Handle);
        Assert.Null(user.DisplayName);
        Assert.Equal("discord|new-user-1@test.example", user.Email);
    }

    [Fact]
    public async Task GetMe_ReturnsSameUser_OnSubsequentRequests()
    {
        var client = api.CreateClientWithUser("discord|same-user");

        var first = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();
        var second = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public async Task PutMe_UpdatesDisplayName()
    {
        var client = api.CreateClientWithUser("discord|update-user");
        await client.GetAsync("/api/v1/me/");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest("Updated Name", null)
        );
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Updated Name", user!.DisplayName);
    }

    [Fact]
    public async Task PutMe_DisplayNamePersists_AndDoesNotAffectHandle_OnReload()
    {
        var client = api.CreateClientWithUser("discord|reload-user");
        var before = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var putResponse = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest("My Chosen Name", null)
        );
        putResponse.EnsureSuccessStatusCode();

        var reloaded = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal("My Chosen Name", reloaded!.DisplayName);
        Assert.Equal(before!.Handle, reloaded.Handle);
    }

    [Fact]
    public async Task GetMe_ResyncsHandle_AndKeepsProfile_WhenOAuthClaimChangesOnExistingUser()
    {
        const string externalId = "discord|handle-resync-user";
        await using (var db = await api.CreateDbContextAsync())
        {
            db.Users.Add(
                new User
                {
                    ExternalId = externalId,
                    Handle = "Stale Handle",
                    DisplayName = "Kept Display Name",
                    Email = $"{externalId}@test.example",
                    Pronouns = "they/them",
                    BirthdayMonth = 4,
                    BirthdayDay = 12,
                }
            );
            await db.SaveChangesAsync();
        }

        var client = api.CreateClientWithUser(externalId);
        var response = await client.GetAsync("/api/v1/me/");
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal($"Test User {externalId}", user!.Handle);
        Assert.Equal("Kept Display Name", user.DisplayName);
        Assert.Equal("they/them", user.Pronouns);
        Assert.Equal(new Birthday(4, 12, null), user.Birthday);
    }

    [Fact]
    public async Task PutMe_RoundTripsProfileFields()
    {
        var client = api.CreateClientWithUser("discord|profile-fields");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(
                "Sam",
                null,
                Pronouns: "  they/them ",
                Location: "Zürich",
                Bio: "Here for the stroopwafels.",
                Birthday: new Birthday(7, 14, 1990),
                DietaryOptionIds: [2, 10],
                DietaryNotes: "No coriander please"
            )
        );
        response.EnsureSuccessStatusCode();

        var user = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("they/them", user!.Pronouns);
        Assert.Equal("Zürich", user.Location);
        Assert.Equal("Here for the stroopwafels.", user.Bio);
        Assert.Equal(new Birthday(7, 14, 1990), user.Birthday);
        Assert.Equal(["vegan", "nut-allergy"], user.DietaryOptions.Select(o => o.Key));
        Assert.Equal("No coriander please", user.DietaryNotes);
    }

    [Fact]
    public async Task PutMe_StoresBlankTextAsNull()
    {
        var client = api.CreateClientWithUser("discord|profile-blank");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, Pronouns: "   ", Bio: "", DietaryNotes: " ")
        );
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Null(user!.Pronouns);
        Assert.Null(user.Bio);
        Assert.Null(user.DietaryNotes);
    }

    [Theory]
    [InlineData("pronouns")]
    [InlineData("location")]
    [InlineData("bio")]
    [InlineData("dietaryNotes")]
    public async Task PutMe_Returns400_WhenTextTooLong(string field)
    {
        var client = api.CreateClientWithUser($"discord|profile-long-{field}");
        var tooLong = new string('x', User.MaxBioLength + 1);

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new Dictionary<string, object?> { [field] = tooLong }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(4, 31, null)]
    [InlineData(2, 29, 2001)]
    [InlineData(13, 1, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 1, 1850)]
    [InlineData(1, 1, 2999)]
    public async Task PutMe_Returns400_ForInvalidBirthday(int month, int day, int? year)
    {
        var client = api.CreateClientWithUser("discord|profile-bad-birthday");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, Birthday: new Birthday(month, day, year))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutMe_AcceptsLeapDay_WithoutYear()
    {
        var client = api.CreateClientWithUser("discord|profile-leap-day");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, Birthday: new Birthday(2, 29, null))
        );
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(new Birthday(2, 29, null), user!.Birthday);
    }

    [Fact]
    public async Task PutMe_ReplacesDietaryOptions_AndCollapsesDuplicates()
    {
        var client = api.CreateClientWithUser("discord|profile-diet-replace");

        var first = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, DietaryOptionIds: [1, 1, 8, 12])
        );
        first.EnsureSuccessStatusCode();
        Assert.Equal(
            ["vegetarian", "gluten-free", "shellfish-allergy"],
            (await first.Content.ReadFromJsonAsync<UserResponse>())!.DietaryOptions.Select(o =>
                o.Key
            )
        );

        var second = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, DietaryOptionIds: [8])
        );
        second.EnsureSuccessStatusCode();

        var reloaded = await (
            await client.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(["gluten-free"], reloaded!.DietaryOptions.Select(o => o.Key));
    }

    [Fact]
    public async Task PutMe_Returns400_ForUnknownDietaryOption()
    {
        var client = api.CreateClientWithUser("discord|profile-diet-unknown");

        var response = await client.PutAsJsonAsync(
            "/api/v1/me/",
            new UpdateUserRequest(null, null, DietaryOptionIds: [1, 9999])
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDietaryOptions_ReturnsSeededOptionsInOrder()
    {
        var client = api.CreateClientWithUser("discord|diet-options");

        var options = await client.GetFromJsonAsync<List<DietaryOptionResponse>>(
            "/api/v1/dietary-options"
        );

        Assert.Equal(DietaryOption.Seed.Select(o => o.Key), options!.Select(o => o.Key));
    }
}
