using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class AttendanceApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<EventResponse> CreateOpenEvent(HttpClient client, string suffix)
    {
        var ev = await (await client.PostAsJsonAsync("/api/v1/events/", new CreateEventRequest(
            $"Attendance Test {suffix}", null,
            new DateOnly(2030, 8, 1), new DateOnly(2030, 8, 7),
            "Amsterdam", 35.00m
        ))).Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{ev!.Id}/publish", null);
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
    public async Task Join_AddsPendingAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-c");
        var attendee = api.CreateClientWithUser("discord|att-user-c");
        var ev = await CreateOpenEvent(organiser, "c");

        var joinResponse = await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        Assert.Equal(HttpStatusCode.Created, joinResponse.StatusCode);

        var attendees = await (await organiser.GetAsync($"/api/v1/events/{ev.Id}/attendees/"))
            .Content.ReadFromJsonAsync<List<AttendeeResponse>>();

        Assert.Contains(attendees!, a => a.Role == "Pending");
    }

    [Fact]
    public async Task Join_Returns409_WhenAlreadyAttending()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-d");
        var attendee = api.CreateClientWithUser("discord|att-user-d");
        var ev = await CreateOpenEvent(organiser, "d");

        await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        var second = await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Confirm_ChangesRoleToAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-e");
        var attendee = api.CreateClientWithUser("discord|att-user-e");
        var ev = await CreateOpenEvent(organiser, "e");

        await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);

        var attendeeUser = await (await attendee.GetAsync("/api/v1/me/"))
            .Content.ReadFromJsonAsync<UserResponse>();

        var confirm = await organiser.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeUser!.Id}/confirm", null
        );
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var attendees = await (await organiser.GetAsync($"/api/v1/events/{ev.Id}/attendees/"))
            .Content.ReadFromJsonAsync<List<AttendeeResponse>>();

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

        await user1.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        var user1Info = await (await user1.GetAsync("/api/v1/me/")).Content.ReadFromJsonAsync<UserResponse>();

        var response = await user2.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{user1Info!.Id}/confirm", null
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAttendee_AllowsSelfRemoval()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-g");
        var attendee = api.CreateClientWithUser("discord|att-user-g");
        var ev = await CreateOpenEvent(organiser, "g");

        await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        var attendeeInfo = await (await attendee.GetAsync("/api/v1/me/")).Content.ReadFromJsonAsync<UserResponse>();

        var response = await attendee.DeleteAsync($"/api/v1/events/{ev.Id}/attendees/{attendeeInfo!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAttendee_UpdatesArrivalDates()
    {
        var organiser = api.CreateClientWithUser("discord|att-org-h");
        var attendee = api.CreateClientWithUser("discord|att-user-h");
        var ev = await CreateOpenEvent(organiser, "h");

        await attendee.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        var attendeeInfo = await (await attendee.GetAsync("/api/v1/me/")).Content.ReadFromJsonAsync<UserResponse>();

        var update = new UpdateAttendanceRequest(
            new DateOnly(2030, 8, 2),
            new DateOnly(2030, 8, 6),
            null
        );

        var response = await attendee.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/attendees/{attendeeInfo!.Id}", update
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
