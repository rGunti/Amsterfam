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
    public async Task GetMe_ResyncsHandle_WhenOAuthClaimChangesOnExistingUser()
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
    }
}
