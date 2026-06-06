using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

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
    public async Task GetMe_AutoCreatesUser_OnFirstRequest()
    {
        var client = api.CreateClientWithUser("discord|new-user-1");
        var response = await client.GetAsync("/api/v1/me/");

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.NotNull(user);
        Assert.Equal("Test User discord|new-user-1", user.DisplayName);
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
}
