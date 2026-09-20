using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Core.Entities;

namespace Amsterfam.Tests.Infrastructure;

public static class EventClientExtensions
{
    public static Task<HttpResponseMessage> TransitionAsync(
        this HttpClient client,
        Guid eventId,
        string target
    ) =>
        client.PostAsJsonAsync(
            $"/api/v1/events/{eventId}/status",
            new TransitionEventRequest(target)
        );

    /// <summary>Walks the event through each target in order, failing on the first error.</summary>
    public static async Task<EventResponse> TransitionThroughAsync(
        this HttpClient client,
        Guid eventId,
        params string[] targets
    )
    {
        EventResponse? ev = null;
        foreach (var target in targets)
        {
            var response = await client.TransitionAsync(eventId, target);
            response.EnsureSuccessStatusCode();
            ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        }
        return ev!;
    }

    /// <summary>
    /// Seeds a fresh attendee join link straight into the database and redeems it as
    /// <paramref name="joiner"/>. Returns the join response.
    /// </summary>
    public static async Task<HttpResponseMessage> JoinAsync(
        this HttpClient joiner,
        ApiFixture api,
        Guid eventId,
        JoinLinkKind kind = JoinLinkKind.Attendee
    )
    {
        await using var db = await api.CreateDbContextAsync();
        var creatorId = db.Events.Where(e => e.Id == eventId).Select(e => e.CreatedById).First();
        var token = Guid.NewGuid().ToString("N");
        db.EventJoinLinks.Add(
            new EventJoinLink
            {
                EventId = eventId,
                Token = token,
                Kind = kind,
                CreatedById = creatorId,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        return await joiner.PostAsync($"/api/v1/join-links/{token}/join", null);
    }
}
