using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class TimelineApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static async Task<EventResponse> CreateEvent(HttpClient client, string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Timeline Test {suffix}",
                null,
                new DateOnly(2030, 7, 1),
                new DateOnly(2030, 7, 8),
                "Amsterdam"
            )
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    private static async Task<List<TimelineEntryResponse>> Timeline(
        HttpClient client,
        Guid eventId,
        string query = ""
    )
    {
        var response = await client.GetAsync($"/api/v1/events/{eventId}/timeline{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<TimelineEntryResponse>>())!;
    }

    /// <summary>Joins <paramref name="joiner"/> and has the owner confirm them.</summary>
    private async Task<int> AddConfirmed(
        HttpClient owner,
        HttpClient joiner,
        Guid eventId,
        JoinLinkKind kind = JoinLinkKind.Attendee
    )
    {
        (await joiner.JoinAsync(api, eventId, kind)).EnsureSuccessStatusCode();
        var id = await MyId(joiner);
        (
            await owner.PostAsync($"/api/v1/events/{eventId}/attendees/{id}/confirm", null)
        ).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Timeline_ListsChangesNewestFirst_WithActorAndDetails()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-1");
        var ev = await CreateEvent(owner, "1");
        await owner.TransitionThroughAsync(ev.Id, "Open");

        var entries = await Timeline(owner, ev.Id);

        Assert.Equal(["StatusChanged", "EventCreated"], entries.Select(e => e.Type));
        var status = entries[0];
        Assert.Equal(await MyId(owner), status.Actor!.Id);
        Assert.Equal("Draft", status.Data!.Value.GetProperty("from").GetString());
        Assert.Equal("Open", status.Data!.Value.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Timeline_HidesOrganiserEntriesFromAttendees()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-2");
        var attendee = api.CreateClientWithUser("discord|tl-attendee-2");
        var ev = await CreateEvent(owner, "2");
        await owner.TransitionThroughAsync(ev.Id, "Open");
        var attendeeId = await AddConfirmed(owner, attendee, ev.Id);

        (
            await owner.PostAsJsonAsync(
                $"/api/v1/events/{ev.Id}/join-links/",
                new CreateJoinLinkRequest(null, null, null)
            )
        ).EnsureSuccessStatusCode();
        (
            await owner.PutAsJsonAsync(
                $"/api/v1/events/{ev.Id}/attendees/{attendeeId}",
                new UpdateAttendanceRequest(new DateOnly(2030, 7, 1), null, 42m)
            )
        ).EnsureSuccessStatusCode();

        var ownerView = (await Timeline(owner, ev.Id)).Select(e => e.Type).ToList();
        Assert.Contains("JoinRequested", ownerView);
        Assert.Contains("JoinLinkCreated", ownerView);
        Assert.Contains("CostOverrideChanged", ownerView);

        var attendeeView = (await Timeline(attendee, ev.Id)).Select(e => e.Type).ToList();
        Assert.Equal(
            ["TravelDatesChanged", "AttendeeConfirmed", "StatusChanged", "EventCreated"],
            attendeeView
        );
    }

    [Fact]
    public async Task Timeline_OrganiserLinkEntries_AreOwnerOnly()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-3");
        var organiser = api.CreateClientWithUser("discord|tl-org-3");
        var ev = await CreateEvent(owner, "3");
        await AddConfirmed(owner, organiser, ev.Id, JoinLinkKind.Organiser);

        (
            await owner.PostAsJsonAsync(
                $"/api/v1/events/{ev.Id}/join-links/",
                new CreateJoinLinkRequest("Organiser", null, null, "Co-orgs")
            )
        ).EnsureSuccessStatusCode();

        var ownerView = await Timeline(owner, ev.Id);
        var link = Assert.Single(ownerView, e => e.Type == "JoinLinkCreated");
        Assert.Equal("Owner", link.Visibility);
        Assert.Equal("Co-orgs", link.Data!.Value.GetProperty("label").GetString());

        var organiserView = await Timeline(organiser, ev.Id);
        Assert.DoesNotContain(organiserView, e => e.Type == "JoinLinkCreated");
        Assert.Contains(organiserView, e => e.Type == "JoinRequested");
    }

    [Fact]
    public async Task Timeline_Returns404ForNonMembers_And403ForPending()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-4");
        var pending = api.CreateClientWithUser("discord|tl-pending-4");
        var stranger = api.CreateClientWithUser("discord|tl-stranger-4");
        var ev = await CreateEvent(owner, "4");
        await owner.TransitionThroughAsync(ev.Id, "Open");
        (await pending.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/events/{ev.Id}/timeline")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.GetAsync($"/api/v1/events/{ev.Id}/timeline")).StatusCode
        );
    }

    [Fact]
    public async Task Timeline_DecliningPendingIsOrganiserOnly_LeavingIsPublic()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-5");
        var attendee = api.CreateClientWithUser("discord|tl-attendee-5");
        var declined = api.CreateClientWithUser("discord|tl-declined-5");
        var leaver = api.CreateClientWithUser("discord|tl-leaver-5");
        var ev = await CreateEvent(owner, "5");
        await owner.TransitionThroughAsync(ev.Id, "Open");
        await AddConfirmed(owner, attendee, ev.Id);
        var leaverId = await AddConfirmed(owner, leaver, ev.Id);
        (await declined.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();
        var declinedId = await MyId(declined);

        (
            await owner.DeleteAsync($"/api/v1/events/{ev.Id}/attendees/{declinedId}")
        ).EnsureSuccessStatusCode();
        (
            await leaver.DeleteAsync($"/api/v1/events/{ev.Id}/attendees/{leaverId}")
        ).EnsureSuccessStatusCode();

        var ownerView = await Timeline(owner, ev.Id);
        Assert.Equal(
            declinedId,
            Assert.Single(ownerView, e => e.Type == "JoinRequestDeclined").Subject!.Id
        );

        var attendeeView = await Timeline(attendee, ev.Id);
        Assert.DoesNotContain(attendeeView, e => e.Type == "JoinRequestDeclined");
        Assert.Equal(leaverId, attendeeView[0].Subject!.Id);
        Assert.Equal("AttendeeLeft", attendeeView[0].Type);
    }

    [Fact]
    public async Task Timeline_CoalescesRepeatedPollSaves()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-6");
        var ev = await CreateEvent(owner, "6");
        await owner.TransitionThroughAsync(ev.Id, "LookingForDate");
        (
            await owner.PutAsJsonAsync(
                $"/api/v1/events/{ev.Id}/date-poll/range",
                new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
            )
        ).EnsureSuccessStatusCode();

        foreach (
            var (week, status) in new[] { ("2030-06-03", "Available"), ("2030-06-10", "Partial") }
        )
            (
                await owner.PutAsJsonAsync(
                    $"/api/v1/events/{ev.Id}/date-poll/me",
                    new UpdateDatePollEntriesRequest([new(DateOnly.Parse(week), status)])
                )
            ).EnsureSuccessStatusCode();
        (
            await owner.DeleteAsync($"/api/v1/events/{ev.Id}/date-poll/me/2030-06-03")
        ).EnsureSuccessStatusCode();

        var entries = await Timeline(owner, ev.Id);
        Assert.Equal(
            ["DatePollResponded", "PollRangeChanged", "StatusChanged", "EventCreated"],
            entries.Select(e => e.Type)
        );
        Assert.Null(entries[0].Data);
    }

    [Fact]
    public async Task Timeline_PagesWithBeforeAndLimit()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-7");
        var ev = await CreateEvent(owner, "7");
        await owner.TransitionThroughAsync(ev.Id, "LookingForDate", "Draft", "LookingForDate");

        var first = await Timeline(owner, ev.Id, "?limit=2");
        Assert.Equal(2, first.Count);
        var rest = await Timeline(owner, ev.Id, $"?limit=2&before={first[^1].Id}");
        Assert.Equal(2, rest.Count);
        Assert.Equal("EventCreated", rest[^1].Type);
        Assert.Empty(await Timeline(owner, ev.Id, $"?before={rest[^1].Id}"));
    }

    [Fact]
    public async Task Timeline_RecordsOnlyFieldsThatChanged()
    {
        var owner = api.CreateClientWithUser("discord|tl-owner-8");
        var ev = await CreateEvent(owner, "8");

        (
            await owner.PutAsJsonAsync(
                $"/api/v1/events/{ev.Id}",
                new UpdateEventRequest(
                    ev.Name,
                    "Now with a description",
                    ev.StartDate,
                    ev.EndDate,
                    ev.Location
                )
            )
        ).EnsureSuccessStatusCode();
        // A no-op save adds nothing.
        (
            await owner.PutAsJsonAsync(
                $"/api/v1/events/{ev.Id}",
                new UpdateEventRequest(
                    ev.Name,
                    "Now with a description",
                    ev.StartDate,
                    ev.EndDate,
                    ev.Location
                )
            )
        ).EnsureSuccessStatusCode();

        var entries = await Timeline(owner, ev.Id);
        Assert.Equal(2, entries.Count);
        var changed = entries[0].Data!.Value.GetProperty("changed");
        Assert.Equal(["description"], changed.EnumerateArray().Select(c => c.GetString()));
    }
}
