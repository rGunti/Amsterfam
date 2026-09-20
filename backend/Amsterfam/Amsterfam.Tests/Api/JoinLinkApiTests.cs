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
        Assert.Equal(ev.CreatedById, preview.OwnerId);
        var organiser = Assert.Single(preview.Organisers);
        Assert.Equal(ev.CreatedById, organiser.UserId);
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

    [Fact]
    public async Task Label_DefaultsByKind_AndCanBeCustomised()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-15");
        var ev = await CreateEvent(owner, "15", open: true);

        var plain = await CreateLink(owner, ev.Id);
        Assert.Equal("Attendee link", plain.Label);

        var orga = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest("Organiser", null, null)
        );
        Assert.Equal("Organiser link", orga.Label);

        var named = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest(null, null, null, "  Friends from work  ")
        );
        Assert.Equal("Friends from work", named.Label);

        var blank = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest(null, null, null, "   ")
        );
        Assert.Equal("Attendee link", blank.Label);
    }

    [Fact]
    public async Task Create_RejectsTooLongLabel()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-16");
        var ev = await CreateEvent(owner, "16", open: true);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/events/{ev.Id}/join-links/",
            new CreateJoinLinkRequest(
                null,
                null,
                null,
                new string('x', EventJoinLink.MaxLabelLength + 1)
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PendingAttendee_ShowsJoinLinkLabel_ToOrganisersOnly()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-17");
        var member = api.CreateClientWithUser("discord|jl-member-17");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-17");
        var ev = await CreateEvent(owner, "17", open: true);
        await member.JoinAsync(api, ev.Id);
        var memberId = (
            await (
                await member.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .Single(a => a.Role == "Pending")
            .UserId;
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{memberId}/confirm", null);

        var link = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest(null, null, null, "Summer crew")
        );
        await joiner.PostAsync($"/api/v1/join-links/{link.Token}/join", null);

        var asOwner = await (
            await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.Contains(asOwner!, a => a.Role == "Pending" && a.JoinLinkLabel == "Summer crew");

        var asMember = await (
            await member.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
        ).Content.ReadFromJsonAsync<List<AttendeeResponse>>();
        Assert.All(asMember!, a => Assert.Null(a.JoinLinkLabel));
    }

    [Fact]
    public async Task Regenerate_RevokesOldLink_AndCopiesParameters()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-18");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-18");
        var ev = await CreateEvent(owner, "18", open: true);
        var expiry = DateTimeOffset.UtcNow.AddDays(3);
        var old = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest(null, expiry, 5, "Summer crew")
        );

        var response = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/join-links/{old.Id}/regenerate",
            null
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fresh = (await response.Content.ReadFromJsonAsync<JoinLinkResponse>())!;

        Assert.NotEqual(old.Token, fresh.Token);
        Assert.Equal("Summer crew", fresh.Label);
        Assert.Equal("Attendee", fresh.Kind);
        Assert.Equal(5, fresh.MaxUses);
        Assert.Equal(0, fresh.UseCount);
        Assert.Equal(expiry.ToUnixTimeSeconds(), fresh.ExpiresAt!.Value.ToUnixTimeSeconds());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await joiner.PostAsync($"/api/v1/join-links/{old.Token}/join", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Created,
            (await joiner.PostAsync($"/api/v1/join-links/{fresh.Token}/join", null)).StatusCode
        );
    }

    [Fact]
    public async Task Regenerate_KeepsDefaultLabel_AndRejectsRevokedLink()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-19");
        var ev = await CreateEvent(owner, "19", open: true);
        var old = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest("Organiser", null, null)
        );

        var fresh = (
            await (
                await owner.PostAsync(
                    $"/api/v1/events/{ev.Id}/join-links/{old.Id}/regenerate",
                    null
                )
            ).Content.ReadFromJsonAsync<JoinLinkResponse>()
        )!;
        Assert.Equal("Organiser link", fresh.Label);
        Assert.Equal("Organiser", fresh.Kind);

        var again = await owner.PostAsync(
            $"/api/v1/events/{ev.Id}/join-links/{old.Id}/regenerate",
            null
        );
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Regenerate_Permissions()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-20");
        var coOrg = api.CreateClientWithUser("discord|jl-coorg-20");
        var member = api.CreateClientWithUser("discord|jl-member-20");
        var ev = await CreateEvent(owner, "20", open: true);

        foreach (var c in new[] { coOrg, member })
            await c.JoinAsync(api, ev.Id);
        var roster = (
            await (
                await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .Where(a => a.Role == "Pending")
            .ToList();
        foreach (var a in roster)
            await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{a.UserId}/confirm", null);
        var coOrgId = (
            await (
                await owner.GetAsync($"/api/v1/events/{ev.Id}/attendees/")
            ).Content.ReadFromJsonAsync<List<AttendeeResponse>>()
        )!
            .First(a => a.Role == "Attendee")
            .UserId;
        await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{coOrgId}/promote", null);

        var attendeeLink = await CreateLink(owner, ev.Id);
        var orgaLink = await CreateLink(
            owner,
            ev.Id,
            new CreateJoinLinkRequest("Organiser", null, null)
        );

        // The promoted user is an organiser; the other is a plain attendee.
        var organiserClient = await ClientFor(ev.Id, coOrg, member);
        var plainClient = organiserClient == coOrg ? member : coOrg;

        Assert.Equal(
            HttpStatusCode.Created,
            (
                await organiserClient.PostAsync(
                    $"/api/v1/events/{ev.Id}/join-links/{attendeeLink.Id}/regenerate",
                    null
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await organiserClient.PostAsync(
                    $"/api/v1/events/{ev.Id}/join-links/{orgaLink.Id}/regenerate",
                    null
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await plainClient.PostAsync(
                    $"/api/v1/events/{ev.Id}/join-links/{orgaLink.Id}/regenerate",
                    null
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task Event_ReportsPendingCount_ToOrganisersOnly()
    {
        var owner = api.CreateClientWithUser("discord|jl-owner-21");
        var joiner = api.CreateClientWithUser("discord|jl-joiner-21");
        var other = api.CreateClientWithUser("discord|jl-joiner-21b");
        var ev = await CreateEvent(owner, "21", open: true);

        var before = await owner.GetFromJsonAsync<EventResponse>($"/api/v1/events/{ev.Id}");
        Assert.Equal(0, before!.PendingAttendeeCount);

        await joiner.JoinAsync(api, ev.Id);
        await other.JoinAsync(api, ev.Id);

        var asOwner = await owner.GetFromJsonAsync<EventResponse>($"/api/v1/events/{ev.Id}");
        Assert.Equal(2, asOwner!.PendingAttendeeCount);

        var asPending = await joiner.GetFromJsonAsync<EventResponse>($"/api/v1/events/{ev.Id}");
        Assert.Null(asPending!.PendingAttendeeCount);
    }

    private static async Task<HttpClient> ClientFor(Guid eventId, params HttpClient[] candidates)
    {
        foreach (var c in candidates)
        {
            var mine = (
                await (
                    await c.GetAsync($"/api/v1/events/{eventId}")
                ).Content.ReadFromJsonAsync<EventResponse>()
            )!;
            if (mine.CurrentUserRole == "Organiser")
                return c;
        }
        throw new InvalidOperationException("No organiser client found.");
    }
}
