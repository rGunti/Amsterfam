using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class UserProfileApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<int> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>Puts each user on one fresh event with the given role.</summary>
    private async Task SeedEventAsync(params (int UserId, AttendanceRole Role)[] members)
    {
        await using var db = await api.CreateDbContextAsync();
        var ev = new Event
        {
            Name = "Profile test",
            Location = "Amsterdam",
            CreatedById = members[0].UserId,
        };
        foreach (var (userId, role) in members)
            ev.Attendances.Add(new EventAttendance { UserId = userId, Role = role });
        db.Events.Add(ev);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProfile_Returns401_WhenUnauthenticated()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/users/1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_ReturnsOwnProfile()
    {
        var client = api.CreateClientWithUser("discord|profile-self");
        var id = await MeAsync(client);

        var response = await client.GetAsync($"/api/v1/users/{id}/profile");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetProfile_ReturnsProfile_WithoutEmail_ForFellowMember()
    {
        var viewer = api.CreateClientWithUser("discord|profile-viewer");
        var target = api.CreateClientWithUser("discord|profile-target");
        var viewerId = await MeAsync(viewer);
        var targetId = await MeAsync(target);
        await target.PutAsJsonAsync("/api/v1/me/", new UpdateUserRequest("Target", null));
        await target.PutAsJsonAsync(
            "/api/v1/me/about",
            new UpdateAboutRequest(
                Pronouns: "she/her",
                Birthday: new Birthday(3, 1, null),
                DietaryOptionIds: [1],
                DietaryNotes: "Loves olives"
            )
        );
        await SeedEventAsync(
            (viewerId, AttendanceRole.Organiser),
            (targetId, AttendanceRole.Attendee)
        );

        var response = await viewer.GetAsync($"/api/v1/users/{targetId}/profile");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        Assert.Equal("Target", profile!.DisplayName);
        Assert.Equal("she/her", profile.Pronouns);
        Assert.Equal(new Birthday(3, 1, null), profile.Birthday);
        Assert.Equal(["vegetarian"], profile.DietaryOptions.Select(o => o.Key));
        Assert.Equal("Loves olives", profile.DietaryNotes);
    }

    [Fact]
    public async Task GetProfile_Returns404_WithoutSharedEvent()
    {
        var viewer = api.CreateClientWithUser("discord|profile-stranger-a");
        var target = api.CreateClientWithUser("discord|profile-stranger-b");
        await MeAsync(viewer);
        var targetId = await MeAsync(target);

        var response = await viewer.GetAsync($"/api/v1/users/{targetId}/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(AttendanceRole.Pending, AttendanceRole.Attendee)]
    [InlineData(AttendanceRole.Pending, AttendanceRole.Organiser)]
    [InlineData(AttendanceRole.Attendee, AttendanceRole.Pending)]
    [InlineData(AttendanceRole.Pending, AttendanceRole.Pending)]
    public async Task GetProfile_Returns404_ForPendingUnlessViewerIsOrganiser(
        AttendanceRole viewerRole,
        AttendanceRole targetRole
    )
    {
        var suffix = $"{viewerRole}-{targetRole}";
        var viewer = api.CreateClientWithUser($"discord|profile-pending-{suffix}-v");
        var target = api.CreateClientWithUser($"discord|profile-pending-{suffix}-t");
        var viewerId = await MeAsync(viewer);
        var targetId = await MeAsync(target);
        await SeedEventAsync((viewerId, viewerRole), (targetId, targetRole));

        var response = await viewer.GetAsync($"/api/v1/users/{targetId}/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_OrganiserSeesPendingMemberOfTheirEvent()
    {
        var organiser = api.CreateClientWithUser("discord|profile-org-sees-pending-o");
        var pending = api.CreateClientWithUser("discord|profile-org-sees-pending-p");
        var organiserId = await MeAsync(organiser);
        var pendingId = await MeAsync(pending);
        await SeedEventAsync(
            (organiserId, AttendanceRole.Organiser),
            (pendingId, AttendanceRole.Pending)
        );

        var response = await organiser.GetAsync($"/api/v1/users/{pendingId}/profile");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetProfile_PendingOnOneEvent_DoesNotLeakToOrganiserOfAnother()
    {
        var organiser = api.CreateClientWithUser("discord|profile-other-event-o");
        var pending = api.CreateClientWithUser("discord|profile-other-event-p");
        var someoneElse = api.CreateClientWithUser("discord|profile-other-event-x");
        var organiserId = await MeAsync(organiser);
        var pendingId = await MeAsync(pending);
        var someoneElseId = await MeAsync(someoneElse);
        // The organiser runs one event; the pending user waits on a different one.
        await SeedEventAsync((organiserId, AttendanceRole.Organiser));
        await SeedEventAsync(
            (someoneElseId, AttendanceRole.Organiser),
            (pendingId, AttendanceRole.Pending)
        );

        var response = await organiser.GetAsync($"/api/v1/users/{pendingId}/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_Returns404_ForUnknownUser()
    {
        var client = api.CreateClientWithUser("discord|profile-unknown");

        var response = await client.GetAsync("/api/v1/users/999999/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
