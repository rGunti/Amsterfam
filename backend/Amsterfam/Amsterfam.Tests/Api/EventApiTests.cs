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
    public async Task GetEvent_ReturnsNullRole_ForNonAssociatedUser()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-nullrole");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-nullrole"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var other = api.CreateClientWithUser("discord|other-nullrole");
        var ev = await other.GetFromJsonAsync<EventResponse>($"/api/v1/events/{created!.Id}");
        Assert.Null(ev!.CurrentUserRole);
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

        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-e"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var updateRequest = new UpdateEventRequest(
            "Hacked Name",
            null,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam",
            35.00m
        );

        var response = await other.PutAsJsonAsync($"/api/v1/events/{created!.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishEvent_TransitionsDraftToOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-f");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-f"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Open", ev!.Status);
    }

    [Fact]
    public async Task UnpublishEvent_TransitionsOpenToDraft()
    {
        var client = api.CreateClientWithUser("discord|organiser-unpub");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-unpub"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await client.PostAsync($"/api/v1/events/{created.Id}/unpublish", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Draft", ev!.Status);
    }

    [Fact]
    public async Task UnpublishEvent_Returns409_WhenNotOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-unpub2");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-unpub2"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.PostAsync($"/api/v1/events/{created!.Id}/unpublish", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnpublishEvent_Returns403_ForNonOrganiser()
    {
        var organiser = api.CreateClientWithUser("discord|organiser-unpub3");
        var other = api.CreateClientWithUser("discord|other-unpub3");
        var created = await (
            await organiser.PostAsJsonAsync("/api/v1/events/", SampleEvent("-unpub3"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await organiser.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await other.PostAsync($"/api/v1/events/{created.Id}/unpublish", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishEvent_Returns409_WhenAlreadyOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-g");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-g"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await client.PostAsync($"/api/v1/events/{created.Id}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CloseEvent_TransitionsOpenToClosed()
    {
        var client = api.CreateClientWithUser("discord|organiser-h");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-h"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        var response = await client.PostAsync($"/api/v1/events/{created.Id}/close", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Closed", ev!.Status);
    }

    [Fact]
    public async Task ReopenEvent_TransitionsClosedToOpen()
    {
        var client = api.CreateClientWithUser("discord|organiser-reopen");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-reopen"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        await client.PostAsync($"/api/v1/events/{created!.Id}/publish", null);
        await client.PostAsync($"/api/v1/events/{created.Id}/close", null);

        var response = await client.PostAsync($"/api/v1/events/{created.Id}/reopen", null);
        response.EnsureSuccessStatusCode();
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Open", ev!.Status);
    }

    [Fact]
    public async Task ReopenEvent_Returns409_WhenNotClosed()
    {
        var client = api.CreateClientWithUser("discord|organiser-reopen2");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-reopen2"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.PostAsync($"/api/v1/events/{created!.Id}/reopen", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_Returns204()
    {
        var client = api.CreateClientWithUser("discord|organiser-i");
        var created = await (
            await client.PostAsJsonAsync("/api/v1/events/", SampleEvent("-i"))
        ).Content.ReadFromJsonAsync<EventResponse>();

        var response = await client.DeleteAsync($"/api/v1/events/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }
}
