using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class UserProfileApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Me(int Id, string ProfileUrl);

    private static async Task<Me> MeAsync(HttpClient client)
    {
        var me = (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!;
        return new(me.Id, ProfileUrl(me.ProfileHandle));
    }

    private static string ProfileUrl(string profileHandle) =>
        $"/api/v1/users/{Uri.EscapeDataString(profileHandle)}/profile";

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
        var response = await api.CreateClient().GetAsync(ProfileUrl("anyone"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_ReturnsOwnProfile()
    {
        var client = api.CreateClientWithUser("discord|profile-self");
        var me = await MeAsync(client);

        var response = await client.GetAsync(me.ProfileUrl);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetProfile_ReturnsProfile_WithoutEmail_ForFellowMember()
    {
        var viewer = api.CreateClientWithUser("discord|profile-viewer");
        var target = api.CreateClientWithUser("discord|profile-target");
        var viewerMe = await MeAsync(viewer);
        var targetMe = await MeAsync(target);
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
            (viewerMe.Id, AttendanceRole.Organiser),
            (targetMe.Id, AttendanceRole.Attendee)
        );

        var response = await viewer.GetAsync(targetMe.ProfileUrl);
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
        var targetMe = await MeAsync(target);

        var response = await viewer.GetAsync(targetMe.ProfileUrl);

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
        var viewerMe = await MeAsync(viewer);
        var targetMe = await MeAsync(target);
        await SeedEventAsync((viewerMe.Id, viewerRole), (targetMe.Id, targetRole));

        var response = await viewer.GetAsync(targetMe.ProfileUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_OrganiserSeesPendingMemberOfTheirEvent()
    {
        var organiser = api.CreateClientWithUser("discord|profile-org-sees-pending-o");
        var pending = api.CreateClientWithUser("discord|profile-org-sees-pending-p");
        var organiserMe = await MeAsync(organiser);
        var pendingMe = await MeAsync(pending);
        await SeedEventAsync(
            (organiserMe.Id, AttendanceRole.Organiser),
            (pendingMe.Id, AttendanceRole.Pending)
        );

        var response = await organiser.GetAsync(pendingMe.ProfileUrl);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetProfile_PendingOnOneEvent_DoesNotLeakToOrganiserOfAnother()
    {
        var organiser = api.CreateClientWithUser("discord|profile-other-event-o");
        var pending = api.CreateClientWithUser("discord|profile-other-event-p");
        var someoneElse = api.CreateClientWithUser("discord|profile-other-event-x");
        var organiserMe = await MeAsync(organiser);
        var pendingMe = await MeAsync(pending);
        var someoneElseMe = await MeAsync(someoneElse);
        // The organiser runs one event; the pending user waits on a different one.
        await SeedEventAsync((organiserMe.Id, AttendanceRole.Organiser));
        await SeedEventAsync(
            (someoneElseMe.Id, AttendanceRole.Organiser),
            (pendingMe.Id, AttendanceRole.Pending)
        );

        var response = await organiser.GetAsync(pendingMe.ProfileUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_Returns404_ForUnknownUser()
    {
        var client = api.CreateClientWithUser("discord|profile-unknown");

        var response = await client.GetAsync(ProfileUrl("nobody@discord"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_FindsUser_ByHandleAndSource()
    {
        var viewer = api.CreateClientWithUser("discord|profile-by-source-v", "discord");
        var target = api.CreateClientWithUser("discord|profile-by-source-t", "discord");
        var viewerMe = await MeAsync(viewer);
        var targetMe = await MeAsync(target);
        await SeedEventAsync(
            (viewerMe.Id, AttendanceRole.Attendee),
            (targetMe.Id, AttendanceRole.Attendee)
        );

        var profile = await viewer.GetFromJsonAsync<UserProfileResponse>(
            ProfileUrl("Test User discord|profile-by-source-t@discord")
        );

        Assert.Equal(targetMe.Id, profile!.Id);
        Assert.Equal("discord", profile.AuthSource);
    }

    [Fact]
    public async Task GetProfile_FallsBackToHandleWithAt_WhenSourceIsUnknown()
    {
        // An internal account can use an email as username; before its source is known the
        // whole string is the handle, so "@example.com" must not be read as a source.
        var viewer = api.CreateClientWithUser("discord|profile-email-handle-v");
        var viewerMe = await MeAsync(viewer);
        int targetId;
        await using (var db = await api.CreateDbContextAsync())
        {
            var target = new User
            {
                ExternalId = "internal|profile-email-handle-t",
                Handle = "mila@example.com",
                Email = "mila@example.com",
            };
            db.Users.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }
        await SeedEventAsync(
            (viewerMe.Id, AttendanceRole.Attendee),
            (targetId, AttendanceRole.Attendee)
        );

        var profile = await viewer.GetFromJsonAsync<UserProfileResponse>(
            ProfileUrl("mila@example.com")
        );

        Assert.Equal(targetId, profile!.Id);
    }

    [Fact]
    public async Task GetProfile_FindsUser_WhoseHandleContainsASlash()
    {
        var viewer = api.CreateClientWithUser("discord|profile-slash-handle-v");
        var viewerMe = await MeAsync(viewer);
        int targetId;
        await using (var db = await api.CreateDbContextAsync())
        {
            var target = new User
            {
                ExternalId = "internal|profile-slash-handle-t",
                Handle = "a/b",
                AuthSource = "internal",
                Email = "slash@example.com",
            };
            db.Users.Add(target);
            await db.SaveChangesAsync();
            targetId = target.Id;
        }
        await SeedEventAsync(
            (viewerMe.Id, AttendanceRole.Attendee),
            (targetId, AttendanceRole.Attendee)
        );

        var profile = await viewer.GetFromJsonAsync<UserProfileResponse>(
            ProfileUrl("a/b@internal")
        );

        Assert.Equal(targetId, profile!.Id);
    }
}
