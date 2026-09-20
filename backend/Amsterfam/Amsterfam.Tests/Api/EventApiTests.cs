using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class EventApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CreateEventRequest SampleEvent(string suffix = "") =>
        new(
            $"Amsterfam 2030{suffix}",
            "Annual trip",
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam"
        );

    [Fact]
    public async Task GetEvents_Returns401_WhenUnauthenticated()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/events/");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEvents_ReturnsOnlyUserEvents_WithOrganiserRole()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-list");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-list"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var events = await organiser.GetFromJsonAsync<EventResponse[]>("/api/v1/events/");
        Assert.NotNull(events);
        var mine = Assert.Single(events, e => e.Id == created!.Id);
        Assert.Equal("Organiser", mine.CurrentUserRole);
    }

    [Fact]
    public async Task GetEvents_ExcludesEventsUserIsNotAssociatedWith()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-excl");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-excl"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var other = api.CreateClientWithUser("discord|other-excl");
        var events = await other.GetFromJsonAsync<EventResponse[]>("/api/v1/events/");
        Assert.NotNull(events);
        Assert.DoesNotContain(events, e => e.Id == created!.Id);
    }

    [Fact]
    public async Task GetEvent_IncludesCurrentUserRole()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-role");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-role"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var ev = await organiser.GetFromJsonAsync<EventResponse>($"/api/v1/events/{created!.Id}");
        Assert.Equal("Organiser", ev!.CurrentUserRole);
    }

    [Fact]
    public async Task GetEvent_ReturnsPreview_ForNonMember()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-nullrole");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-nullrole"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var other = api.CreateClientWithUser("discord|other-nullrole");
        var ev = await other.GetFromJsonAsync<EventResponse>($"/api/v1/events/{created!.Id}");
        Assert.Null(ev!.CurrentUserRole);
        Assert.False(ev.IsMember);
        Assert.Null(ev.Description);
        Assert.Null(ev.PollRangeStart);
        Assert.Null(ev.PollRangeEnd);
    }

    [Fact]
    public async Task GetEvent_ReturnsFullDetail_ForPendingAttendee()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-pending-full");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-pending-full"))
        ).Content.ReadFromJsonAsync<EventResponse>();
        await organiser.TransitionThroughAsync(created!.Id, "Open");

        var pending = api.CreateClientWithUser("discord|pending-full-user");
        await pending.PostAsync($"/api/v1/events/{created.Id}/attendees/join", null);

        var ev = await pending.GetFromJsonAsync<EventResponse>($"/api/v1/events/{created.Id}");
        Assert.Equal("Pending", ev!.CurrentUserRole);
        Assert.True(ev.IsMember);
    }

    [Fact]
    public async Task CreateEvent_ReturnsCreated_AndEventIsDraft()
    {
        var client = api.CreateClientWithUser("discord|organiser-a");
        var response = await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-a"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(ev);
        Assert.Equal("Draft", ev.Status);
    }

    [Fact]
    public async Task GetEvent_ReturnsEvent()
    {
        var client = api.CreateClientWithUser("discord|organiser-b");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-b"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.GetAsync($"/api/v1/events/{created!.Id}");
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal(created.Id, ev!.Id);
        Assert.True(ev.IsMember);
    }

    [Fact]
    public async Task GetEvent_Returns404_WhenNotFound()
    {
        var client = api.CreateClientWithUser("discord|organiser-c");
        var response = await client.GetAsync("/api/v1/events/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_UpdatesName()
    {
        var client = api.CreateClientWithUser("discord|organiser-d");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-d"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var updateRequest = new UpdateEventRequest(
            "Updated Name",
            null,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam"
        );

        var response = await client.PutAsJsonAsync($"/api/v1/events/{created!.Id}", updateRequest);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Updated Name", ev!.Name);
    }

    [Fact]
    public async Task UpdateEvent_Returns403_ForNonOrganiser()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-e");
        var other = api.CreateClientWithUser("discord|other-e");

        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-e"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var updateRequest = new UpdateEventRequest(
            "Hacked Name",
            null,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam"
        );

        var response = await other.PutAsJsonAsync($"/api/v1/events/{created!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(HttpClient Client, EventResponse Event)> CreateAs(
        string user,
        CreateEventRequest? request = null
    )
    {
        var client = api.CreateClientWithUser(user);
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", request ?? SampleEvent($"-{user}"))
        ).Content.ReadFromJsonAsync<EventResponse>();
        return (client, created!);
    }

    private async Task<HttpClient> AddConfirmedAttendee(
        HttpClient owner,
        Guid eventId,
        string user,
        bool promote = false
    )
    {
        var client = api.CreateClientWithUser(user);
        await client.PostAsync($"/api/v1/events/{eventId}/attendees/join", null);
        var info = await client.GetFromJsonAsync<UserResponse>("/api/v1/me/");
        await owner.PostAsync($"/api/v1/events/{eventId}/attendees/{info!.Id}/confirm", null);
        if (promote)
            await owner.PostAsync($"/api/v1/events/{eventId}/attendees/{info.Id}/promote", null);
        return client;
    }

    [Fact]
    public async Task Transition_WalksTheHappyPath()
    {
        var (client, created) = await CreateAs("discord|sm-happy");

        foreach (
            var target in new[] { "LookingForDate", "Open", "InProgress", "Closed", "Archived" }
        )
        {
            var response = await client.TransitionAsync(created.Id, target);
            response.EnsureSuccessStatusCode();
            var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
            Assert.Equal(target, ev!.Status);
        }
    }

    [Fact]
    public async Task Transition_Returns400_ForUnknownStatus()
    {
        var (client, created) = await CreateAs("discord|sm-unknown");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.TransitionAsync(created.Id, "Bogus")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.TransitionAsync(created.Id, "2")).StatusCode
        );
    }

    [Fact]
    public async Task Transition_Returns409_ForTransitionNotInStateMachine()
    {
        var (client, created) = await CreateAs("discord|sm-invalid");

        var response = await client.TransitionAsync(created.Id, "Closed");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Transition_Returns409_WhenOpeningWithoutDates()
    {
        var (client, created) = await CreateAs(
            "discord|sm-nodates",
            new CreateEventRequest("No dates", null, null, null, "Amsterdam")
        );

        var response = await client.TransitionAsync(created.Id, "Open");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Transition_Returns403_ForNonOrganiser()
    {
        var (owner, created) = await CreateAs("discord|sm-403-owner");
        await owner.TransitionThroughAsync(created.Id, "LookingForDate");
        var attendee = await AddConfirmedAttendee(owner, created.Id, "discord|sm-403-att");

        var response = await attendee.TransitionAsync(created.Id, "Open");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transition_Cancel_IsOwnerOnly()
    {
        var (owner, created) = await CreateAs("discord|sm-cancel-owner");
        await owner.TransitionThroughAsync(created.Id, "LookingForDate");
        var organiser = await AddConfirmedAttendee(
            owner,
            created.Id,
            "discord|sm-cancel-org",
            promote: true
        );

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await organiser.TransitionAsync(created.Id, "Cancelled")).StatusCode
        );
        var ev = await owner.TransitionThroughAsync(created.Id, "Cancelled");
        Assert.Equal("Cancelled", ev.Status);
    }

    [Fact]
    public async Task Transition_ResetToOpen_IsOwnerOnly_AndPausesAutoTransitions()
    {
        var (owner, created) = await CreateAs("discord|sm-reset-owner");
        await owner.TransitionThroughAsync(created.Id, "Open");
        var organiser = await AddConfirmedAttendee(
            owner,
            created.Id,
            "discord|sm-reset-org",
            promote: true
        );
        await owner.TransitionThroughAsync(created.Id, "InProgress", "Closed");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await organiser.TransitionAsync(created.Id, "Open")).StatusCode
        );

        var ev = await owner.TransitionThroughAsync(created.Id, "Open");
        Assert.Equal("Open", ev.Status);
        Assert.True(ev.AutoTransitionsPaused);

        ev = await owner.TransitionThroughAsync(created.Id, "InProgress");
        Assert.False(ev.AutoTransitionsPaused);
    }

    [Fact]
    public async Task AllowedTransitions_DependOnRoleAndState()
    {
        var (owner, created) = await CreateAs("discord|sm-allowed-owner");
        Assert.Equal(
            ["LookingForDate", "Open", "Archived", "Cancelled"],
            created.AllowedTransitions
        );

        await owner.TransitionThroughAsync(created.Id, "LookingForDate");
        var organiser = await AddConfirmedAttendee(
            owner,
            created.Id,
            "discord|sm-allowed-org",
            promote: true
        );
        var attendee = await AddConfirmedAttendee(owner, created.Id, "discord|sm-allowed-att");

        var asOrganiser = await organiser.GetFromJsonAsync<EventResponse>(
            $"/api/v1/events/{created.Id}"
        );
        Assert.Equal(["Draft", "Open"], asOrganiser!.AllowedTransitions);

        var asAttendee = await attendee.GetFromJsonAsync<EventResponse>(
            $"/api/v1/events/{created.Id}"
        );
        Assert.Empty(asAttendee!.AllowedTransitions);
    }

    [Fact]
    public async Task UpdateEvent_Returns409_WhenChangingDatesOfOpenEvent()
    {
        var (client, created) = await CreateAs("discord|sm-datelock");
        await client.TransitionThroughAsync(created.Id, "Open");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{created.Id}",
            new UpdateEventRequest(
                created.Name,
                null,
                new DateOnly(2030, 7, 2),
                new DateOnly(2030, 7, 9),
                "Amsterdam"
            )
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var nameOnly = await client.PutAsJsonAsync(
            $"/api/v1/events/{created.Id}",
            new UpdateEventRequest("Renamed", null, created.StartDate, created.EndDate, "Amsterdam")
        );
        nameOnly.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UpdateEvent_Returns400_WhenStartIsInThePast()
    {
        var (client, created) = await CreateAs("discord|sm-past");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{created.Id}",
            new UpdateEventRequest(
                created.Name,
                null,
                new DateOnly(2000, 1, 1),
                new DateOnly(2000, 1, 5),
                "Amsterdam"
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_Returns409_WhenArchived()
    {
        var (client, created) = await CreateAs("discord|sm-ro");
        await client.TransitionThroughAsync(created.Id, "Archived");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{created.Id}",
            new UpdateEventRequest("Renamed", null, created.StartDate, created.EndDate, "Amsterdam")
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Attendance_Returns409_WhenArchived()
    {
        var (owner, created) = await CreateAs("discord|sm-ro-att");
        await owner.TransitionThroughAsync(created.Id, "Open");
        var attendee = await AddConfirmedAttendee(owner, created.Id, "discord|sm-ro-att-user");
        await owner.TransitionThroughAsync(created.Id, "Archived");

        var info = await attendee.GetFromJsonAsync<UserResponse>("/api/v1/me/");
        var response = await attendee.DeleteAsync(
            $"/api/v1/events/{created.Id}/attendees/{info!.Id}"
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Join_OnlyAllowed_WhileLookingForDateOrOpen()
    {
        var (owner, created) = await CreateAs("discord|sm-join-owner");
        var joiner = api.CreateClientWithUser("discord|sm-join-user");

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await joiner.PostAsync($"/api/v1/events/{created.Id}/attendees/join", null)).StatusCode
        );

        await owner.TransitionThroughAsync(created.Id, "LookingForDate");
        Assert.Equal(
            HttpStatusCode.Created,
            (await joiner.PostAsync($"/api/v1/events/{created.Id}/attendees/join", null)).StatusCode
        );

        await owner.TransitionThroughAsync(created.Id, "Open", "InProgress");
        var late = api.CreateClientWithUser("discord|sm-join-late");
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await late.PostAsync($"/api/v1/events/{created.Id}/attendees/join", null)).StatusCode
        );
    }

    [Fact]
    public async Task CancelledEvent_IsHiddenFromNonOrganisers()
    {
        var (owner, created) = await CreateAs("discord|sm-hidden-owner");
        await owner.TransitionThroughAsync(created.Id, "Open");
        var attendee = await AddConfirmedAttendee(owner, created.Id, "discord|sm-hidden-att");
        await owner.TransitionThroughAsync(created.Id, "Cancelled");

        var asAttendee = await attendee.GetFromJsonAsync<EventResponse>(
            $"/api/v1/events/{created.Id}"
        );
        Assert.Equal("Cancelled", asAttendee!.Status);
        Assert.Equal("Attendee", asAttendee.CurrentUserRole);
        Assert.Empty(asAttendee.AllowedTransitions);

        var attendees = await attendee.GetAsync($"/api/v1/events/{created.Id}/attendees/");
        Assert.Equal(HttpStatusCode.Forbidden, attendees.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_Returns409_WhenNotArchivedOrCancelled()
    {
        var (client, created) = await CreateAs("discord|sm-del409");

        var response = await client.DeleteAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_Returns204()
    {
        var client = api.CreateClientWithUser("discord|organiser-i");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-i"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.TransitionThroughAsync(created!.Id, "Archived");
        var response = await client.DeleteAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_Returns403_ForNonOwnerOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|organiser-delete403");
        var secondOrganiser = api.CreateClientWithUser("discord|org2-delete403");
        var created = await (
            await owner.PostAsJsonAsync("/api/v1/events/", SampleEvent("-delete403"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await owner.TransitionThroughAsync(created!.Id, "Open");
        await secondOrganiser.PostAsync($"/api/v1/events/{created.Id}/attendees/join", null);
        var secondOrgInfo = await (
            await secondOrganiser.GetAsync("/api/v1/me/")
        ).Content.ReadFromJsonAsync<UserResponse>();
        await owner.PostAsync(
            $"/api/v1/events/{created.Id}/attendees/{secondOrgInfo!.Id}/confirm",
            null
        );
        await owner.PostAsync(
            $"/api/v1/events/{created.Id}/attendees/{secondOrgInfo.Id}/promote",
            null
        );

        await owner.TransitionThroughAsync(created.Id, "Archived");
        var response = await secondOrganiser.DeleteAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
