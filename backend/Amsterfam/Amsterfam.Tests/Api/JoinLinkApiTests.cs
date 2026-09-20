using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class JoinLinkApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static async Task<EventResponse> CreateEvent(
        HttpClient client,
        string suffix,
        bool open
    )
    {
        var ev = await (
            await client.PostAsJsonAsync(
                "/api/v1/events/",
                new CreateEventRequest(
                    $"Join Link Test {suffix}",
                    null,
                    new DateOnly(2030, 8, 1),
                    new DateOnly(2030, 8, 7),
                    "Amsterdam"
                )
            )
        ).Content.ReadFromJsonAsync<EventResponse>();
        if (open)
            await client.TransitionThroughAsync(ev!.Id, "Open");
        return ev!;
    }

    private static async Task<JoinLinkResponse> CreateLink(
        HttpClient client,
        Guid eventId,
        CreateJoinLinkRequest? body = null
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/events/{eventId}/join-links/",
            body ?? new CreateJoinLinkRequest(null, null, null)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JoinLinkResponse>())!;
    }

    [Fact]
    public async Task Create_ReturnsUnguessableToken_AndJoinLandsPending()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-1");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-1");
        var ev = await CreateEvent(owner, "1", open: true);

        var link = await CreateLink(owner, ev.Id);
        Assert.True(link.Token.Length >= 40);

        var join = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.Created, join.StatusCode);

        var attendees = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(attendees!, a => a.Role == "Pending" && !a.RequestedOrganiser);
    }

    [Fact]
    public async Task Join_Twice_Returns409()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-2");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-2");
        var ev = await CreateEvent(owner, "2", open: true);
        var link = await CreateLink(owner, ev.Id);

        await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        var second = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UnknownToken_Returns404()
    {
        var joiner = api.CreateClientWithUser("discord|jl-joiner-3");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await joiner.GetAsync("/api/v1/join-links/nope")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await joiner.PostAsync("/api/v1/join-links/nope/join", null)).StatusCode
        );
    }

    [Fact]
    public async Task RevokedLink_Returns404()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-4");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-4");
        var ev = await CreateEvent(owner, "4", open: true);
        var link = await CreateLink(owner, ev.Id);

        var revoke = await owner.DeleteAsync($"/api/v1/events/{ev.Id}/join-links/{link.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var join = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.NotFound, join.StatusCode);
    }

    [Fact]
    public async Task ExpiredLink_Returns404()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-5");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-5");
        var ev = await CreateEvent(owner, "5", open: true);
        var link = await CreateLink(owner, ev.Id);

        await using var db = await api.CreateDbContextAsync();
        await db
            .EventJoinLinks.Where(l => l.Id == link.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))
            );

        var join = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.NotFound, join.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsPastExpiryAndBadMaxUses()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-6");
        var ev = await CreateEvent(owner, "6", open: true);

        var past = await owner.PostAsJsonAsync(
            $"/api/v1/events/{ev.Id}/join-links/",
            new CreateJoinLinkRequest(null, DateTimeOffset.UtcNow.AddDays(-1), null)
        );
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var zero = await owner.PostAsJsonAsync(
            $"/api/v1/events/{ev.Id}/join-links/",
            new CreateJoinLinkRequest(null, null, 0)
        );
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task MaxUses_IsEnforced_UnderConcurrency()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-7");
        var ev = await CreateEvent(owner, "7", open: true);
        var link = await CreateLink(owner, ev.Id, new CreateJoinLinkRequest(null, null, 1));

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 5)
                .Select(i =>
                    api.CreateClientWithUser($"discord|jl-race-{i}")
                        .PostAsync($"/api/v1/join-links/{link.Token}/join", null)
                )
        );

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task NonOrganiser_CannotCreateOrListLinks()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-8");
        var member = api.CreateClientWithUser("discord|jl-member-8");
        var ev = await CreateEvent(owner, "8", open: true);
        await member.JoinAsync(api, ev.Id);

        var create = await member.PostAsJsonAsync(
            $"/api/v1/events/{ev.Id}/join-links/",
            new CreateJoinLinkRequest(null, null, null)
        );
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync($"/api/v1/events/{ev.Id}/join-links/")).StatusCode
        );
    }

    [Fact]
    public async Task Organiser_CannotCreateOrganiserLink_OrSeeIt()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-9");
        var coOrg = api.CreateClientWithUser("discord|jl-coorg-9");
        var ev = await CreateEvent(owner, "9", open: true);
        await coOrg.JoinAsync(api, ev.Id);
        var coOrgId = (
            await (
                await coOrg.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .Single(a => a.Role == "Pending")
            .UserId;
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{coOrgId}/confirm", null);
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{coOrgId}/promote", null);

        var forbidden = await coOrg.PostAsJsonAsync(
            $"/api/v1/events/{ev.Id}/join-links/",
            new CreateJoinLinkRequest("Organiser", null, null)
        );
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await CreateLink(owner, ev.Id, new CreateJoinLinkRequest("Organiser", null, null));
        var visible = await (
            await coOrg.GetAsync($"/api/v1/events/{ev.Id}/join-links/")
        ).Content.ReadFromJsonAsync<List<JoinLinkResponse>>();
        Assert.DoesNotContain(visible!, l => l.Kind == "Organiser");
    }

    [Fact]
    public async Task OrganiserLink_OnlyOwnerCanConfirm_AndGrantsOrganiser()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-10");
        var coOrg = api.CreateClientWithUser("discord|jl-coorg-10");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-10");
        var ev = await CreateEvent(owner, "10", open: false); // draft

        // Bring in a regular organiser first.
        await coOrg.JoinAsync(api, ev.Id, JoinLinkKind.Organiser);
        var coOrgId = (
            await (
                await coOrg.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .Single(a => a.RequestedOrganiser)
            .UserId;
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{coOrgId}/confirm", null)
            ).StatusCode
        );

        var link = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest("Organiser", null, null)
        );
        var join = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.Created, join.StatusCode);

        var joinerId = (
            await (
                await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .Single(a => a.Role == "Pending")
            .UserId;

        var byOrganiser = await coOrg.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{joinerId}/confirm",
            null
        );
        Assert.Equal(HttpStatusCode.Forbidden, byOrganiser.StatusCode);

        var byOwner = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/attendees/{joinerId}/confirm",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        var after = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(after!, a => a.UserId == joinerId && a.Role == "Organiser");
    }

    [Fact]
    public async Task AttendeeLink_OnDraftEvent_Returns404()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-11");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-11");
        var ev = await CreateEvent(owner, "11", open: false);
        var link = await CreateLink(owner, ev.Id);

        var join = await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);
        Assert.Equal(HttpStatusCode.NotFound, join.StatusCode);
    }

    [Fact]
    public async Task Preview_ReturnsEventBasics()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-12");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-12");
        var ev = await CreateEvent(owner, "12", open: true);
        var link = await CreateLink(owner, ev.Id);

        var preview = await (
            await joiner.GetAsync($"/api/v1/join-links/{link.Token}/")
        ).Content.ReadFromJsonAsync<JoinLinkPreviewResponse>();
        Assert.Equal(ev.Id, preview!.EventId);
        Assert.False(preview.AlreadyMember);
    }

    [Fact]
    public async Task Event_IsHiddenFromNonMembers()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-13");
        var stranger = api.CreateClientWithUser("discord|jl-stranger-13");
        var ev = await CreateEvent(owner, "13", open: true);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/events/{ev.Id}")).StatusCode
        );
    }

    [Fact]
    public async Task OldOpenJoinEndpoint_IsGone()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-14");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-14");
        var ev = await CreateEvent(owner, "14", open: true);

        var response = await joiner.PostAsync($"/api/v1/events/{ev.Id}/attendees/join", null);
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
        );
    }
}
