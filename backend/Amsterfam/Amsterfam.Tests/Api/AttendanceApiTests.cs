using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class AttendanceApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<EventResponse> CreateOpenEvent(HttpClient client, string suffix)
    {
        var ev = await (
            await client.PostAsJsonAsync(
                "/api/v1/events/",
                new CreateEventRequest(
                    $"Attendance Test {suffix}",
                    null,
                    new DateOnly(2030, 8, 1),
                    new DateOnly(2030, 8, 7),
                    "Amsterdam"
                )
            )
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.TransitionThroughAsync(ev!.Id, "Open");
        return ev;
    }

    [Fact]
    public async Task GetAttendees_Returns404_ForUnknownEvent()
    {
        var client = api.CreateClientWithUser("discord|att-a");
        var response = await client.GetAsync("/api/v1/events/999999/attendees/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAttendees_IncludesOrganiserAfterEventCreation()
    {
        var client = api.CreateClientWithUser("discord|att-org-b");
        var ev = await CreateOpenEvent(client, "b");

        var response = await client.GetAsync($"/api/v1/events/{ev.Id}/attendees/");
        response.EnsureSuccessStatusCode();
        var attendees = await response.Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        Assert.NotNull(attendees);
        Assert.Contains(attendees, a => a.Role == "Organiser");
    }

    [Fact]
    public async Task GetAttendees_ReturnsOrganisersOnly_ForNonMember()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-nonmember");
        var stranger = api.CreateClientWithUser("discord|att-stranger");
        var ev = await CreateOpenEvent(organiser, "nonmember");

        var response = await stranger.GetAsync($"/api/v1/events/{ev.Id}/attendees/");
        response.EnsureSuccessStatusCode();
        var attendees = await response.Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        Assert.NotNull(attendees);
        Assert.All(attendees, a => Assert.Equal("Organiser", a.Role));
        Assert.Contains(attendees, a => a.Role == "Organiser");
    }

    [Fact]
    public async Task GetAttendees_Returns200_ForPendingAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-pending200");
        var pending = api.CreateClientWithUser("discord|att-pending200");
        var ev = await CreateOpenEvent(organiser, "pending200");

        await pending.JoinAsync(api, ev.Id);

        var response = await pending.GetAsync($"/api/v1/events/{ev.Id}/attendees/");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Join_AddsPendingAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-c");
        var attendee = api.CreateClientWithUser("discord|att-user-c");
        var ev = await CreateOpenEvent(organiser, "c");

        var joinResponse = await attendee.JoinAsync(api, ev.Id);
        Assert.Equal(HttpStatusCode.Created, joinResponse.StatusCode);

        var attendees = await (
            await organiser.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        Assert.Contains(attendees!, a => a.Role == "Pending");
    }

    [Fact]
    public async Task Join_Returns409_WhenAlreadyAttending()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-d");
        var attendee = api.CreateClientWithUser("discord|att-user-d");
        var ev = await CreateOpenEvent(organiser, "d");

        await attendee.JoinAsync(api, ev.Id);
        var second = await attendee.JoinAsync(api, ev.Id);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Confirm_ChangesRoleToAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-e");
        var attendee = api.CreateClientWithUser("discord|att-user-e");
        var ev = await CreateOpenEvent(organiser, "e");

        await attendee.JoinAsync(api, ev.Id);

        var attendeeUser = await (
            await attendee.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var confirm = await organiser.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeUser!.Id}/confirm",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var attendees = await (
            await organiser.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        var confirmed = attendees!.First(a => a.UserId == attendeeUser.Id);
        Assert.Equal("Attendee", confirmed.Role);
    }

    [Fact]
    public async Task Confirm_Returns403_ForNonOrganiser()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-f");
        var user1 = api.CreateClientWithUser("discord|att-user-f1");
        var user2 = api.CreateClientWithUser("discord|att-user-f2");
        var ev = await CreateOpenEvent(organiser, "f");

        await user1.JoinAsync(api, ev.Id);
        var user1Info = await (
            await user1.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var response = await user2.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{user1Info!.Id}/confirm",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAttendee_AllowsSelfRemoval()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-g");
        var attendee = api.CreateClientWithUser("discord|att-user-g");
        var ev = await CreateOpenEvent(organiser, "g");

        await attendee.JoinAsync(api, ev.Id);
        var attendeeInfo = await (
            await attendee.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var response = await attendee.DeleteAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo!.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetAttendees_IncludesAvatarUrl()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-i");
        var attendee = api.CreateClientWithUser("discord|att-user-i");
        var ev = await CreateOpenEvent(organiser, "i");

        await attendee.JoinAsync(api, ev.Id);
        var attendeeInfo = await (
            await attendee.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        await using (var db = await api.CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(u => u.Id == attendeeInfo!.Id);
            user.AvatarUrl = "https://cdn.discordapp.com/avatars/test/avatar.png";
            await db.SaveChangesAsync();
        }

        var attendees = await (
            await organiser.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        var confirmed = attendees!.First(a => a.UserId == attendeeInfo!.Id);
        Assert.Equal("https://cdn.discordapp.com/avatars/test/avatar.png", confirmed.AvatarUrl);
    }

    private async Task<UserResponse> JoinAndConfirm(
        HttpClient organiser,
        HttpClient attendee,
        Guid eventId
    )
    {
        await attendee.JoinAsync(api, eventId);
        var attendeeInfo = await (
            await attendee.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        await organiser.PostAsync(
            $"/api/v1/events/{eventId}/attendees/{attendeeInfo!.Id}/confirm",
            null
        );
        return attendeeInfo;
    }

    [Fact]
    public async Task Promote_ChangesAttendeeToOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-promote");
        var attendee = api.CreateClientWithUser("discord|att-user-promote");
        var ev = await CreateOpenEvent(owner, "promote");
        var attendeeInfo = await JoinAndConfirm(owner, attendee, ev.Id);

        var response = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo.Id}/promote",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var attendees = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(attendees!, a => a.UserId == attendeeInfo.Id && a.Role == "Organiser");
    }

    [Fact]
    public async Task Promote_Returns403_ForNonOwnerOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-promote403");
        var secondOrganiser = api.CreateClientWithUser("discord|att-org2-promote403");
        var attendee = api.CreateClientWithUser("discord|att-user-promote403");
        var ev = await CreateOpenEvent(owner, "promote403");

        var secondOrgInfo = await JoinAndConfirm(owner, secondOrganiser, ev.Id);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{secondOrgInfo.Id}/promote", null);

        var attendeeInfo = await JoinAndConfirm(owner, attendee, ev.Id);

        var response = await secondOrganiser.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo.Id}/promote",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Demote_ChangesOrganiserToAttendee()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-demote");
        var organiser = api.CreateClientWithUser("discord|att-org-demote");
        var ev = await CreateOpenEvent(owner, "demote");

        var orgInfo = await JoinAndConfirm(owner, organiser, ev.Id);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/promote", null);

        var response = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/demote",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var attendees = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(attendees!, a => a.UserId == orgInfo.Id && a.Role == "Attendee");
    }

    [Fact]
    public async Task Demote_Returns403_ForNonOwner()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-demote403");
        var organiser = api.CreateClientWithUser("discord|att-org-demote403");
        var ev = await CreateOpenEvent(owner, "demote403");

        var orgInfo = await JoinAndConfirm(owner, organiser, ev.Id);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/promote", null);

        var response = await organiser.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/demote",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAttendee_Returns403_WhenNonOwnerTargetsOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-removeorg");
        var organiserA = api.CreateClientWithUser("discord|att-orga-removeorg");
        var organiserB = api.CreateClientWithUser("discord|att-orgb-removeorg");
        var ev = await CreateOpenEvent(owner, "removeorg");

        await JoinAndConfirm(owner, organiserA, ev.Id);
        var orgBInfo = await JoinAndConfirm(owner, organiserB, ev.Id);
        var orgAInfo = await (
            await organiserA.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{orgAInfo!.Id}/promote", null);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{orgBInfo.Id}/promote", null);

        var response = await organiserA.DeleteAsync(
            $"/api/v1/events/{ev.Id}/attendees/{orgBInfo.Id}"
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAttendee_Returns409_WhenOwnerRemovesSelf()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-selfleave");
        var ev = await CreateOpenEvent(owner, "selfleave");
        var ownerInfo = await (
            await owner.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var response = await owner.DeleteAsync($"/api/v1/events/{ev.Id}/attendees/{ownerInfo!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_MovesOwnershipToOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-transfer");
        var organiser = api.CreateClientWithUser("discord|att-org-transfer");
        var ev = await CreateOpenEvent(owner, "transfer");

        var orgInfo = await JoinAndConfirm(owner, organiser, ev.Id);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/promote", null);

        var response = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{orgInfo.Id}/transfer-ownership",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/")
        ).Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal(orgInfo.Id, updated!.CreatedById);

        // Old owner keeps their Organiser role after handing over ownership.
        var attendees = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(attendees!, a => a.UserId == ev.CreatedById && a.Role == "Organiser");
    }

    [Fact]
    public async Task TransferOwnership_Returns409_WhenTargetIsNotOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|att-owner-transfer409");
        var attendee = api.CreateClientWithUser("discord|att-user-transfer409");
        var ev = await CreateOpenEvent(owner, "transfer409");

        var attendeeInfo = await JoinAndConfirm(owner, attendee, ev.Id);

        var response = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo.Id}/transfer-ownership",
            null
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAttendee_UpdatesArrivalDates()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-h");
        var attendee = api.CreateClientWithUser("discord|att-user-h");
        var ev = await CreateOpenEvent(organiser, "h");

        await attendee.JoinAsync(api, ev.Id);
        var attendeeInfo = await (
            await attendee.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();

        var update = new UpdateAttendanceRequest(
            new DateOnly(2030, 8, 2),
            new DateOnly(2030, 8, 6),
            null
        );

        var response = await attendee.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo!.Id}",
            update
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
