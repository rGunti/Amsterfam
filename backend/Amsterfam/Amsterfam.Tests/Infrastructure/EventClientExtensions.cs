using System.Net.Http.Json;
using Amsterfam.Api.Dtos;

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
}
