using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class EventApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CreateEventRequest SampleEvent(string suffix = "") => new(
        $"Amsterfam 2030{suffix}",
        "Annual trip",
        new DateOnly(2030, 7, 1),
        new DateOnly(2030, 7, 8),
        "Amsterdam",
        35.00m
    );

    [Fact]
    public async Task GetEvents_Returns401_WhenUnauthenticated()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/events/");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-b")))
            .Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.GetAsync($"/api/v1/events/{created!.Id}");
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal(created.Id, ev!.Id);
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
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-d")))
            .Content.ReadFromJsonAsync<EventResponse>();

        var updateRequest = new UpdateEventRequest(
            "Updated Name",
            null,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam",
            35.00m
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

        var created = await (await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-e")))
            .Content.ReadFromJsonAsync<EventResponse>();

        var updateRequest = new UpdateEventRequest(
            "Hacked Name", null,
            new DateOnly(2030, 7, 1), new DateOnly(2030, 7, 8),
            "Amsterdam", 35.00m
        );

        var response = await other.PutAsJsonAsync($"/api/v1/events/{created!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishEvent_TransitionsDraftToOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-f");
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-f")))
            .Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Open", ev!.Status);
    }

    [Fact]
    public async Task PublishEvent_Returns409_WhenAlreadyOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-g");
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-g")))
            .Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await client.PostAsync($"/api/v1/events/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CloseEvent_TransitionsOpenToClosed()
    {
        var client = api.CreateClientWithUser("discord|organiser-h");
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-h")))
            .Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await client.PostAsync($"/api/v1/events/{created.Id}/close", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Closed", ev!.Status);
    }

    [Fact]
    public async Task DeleteEvent_Returns204()
    {
        var client = api.CreateClientWithUser("discord|organiser-i");
        var created = await (await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-i")))
            .Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.DeleteAsync($"/api/v1/events/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }
}
