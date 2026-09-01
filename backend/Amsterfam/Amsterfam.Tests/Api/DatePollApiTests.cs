using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class DatePollApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CreateEventRequest SampleEvent(string suffix = "") =>
        new(
            $"Date Poll Test {suffix}",
            null,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 8),
            "Amsterdam",
            35.00m
        );

    private async Task<EventResponse> CreateDraftEvent(HttpClient client, string suffix) =>
        (
            await (
                await client.PostAsJsonAsync("/api/v1/events/", SampleEvent(suffix))
            ).Content.ReadFromJsonAsync<EventResponse>()
        )!;

    [Fact]
    public async Task SetRange_UpdatesPollRange_ForOrganiser()
    {
        var client = api.CreateClientWithUser("discord|poll-org-a");
        var ev = await CreateDraftEvent(client, "a");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        response.EnsureSuccessStatusCode();
        var summary = await response.Content.ReadFromJsonAsync<DatePollSummaryResponse>();
        Assert.Equal(new DateOnly(2030, 6, 1), summary!.PollRangeStart);
        Assert.Equal(new DateOnly(2030, 8, 1), summary.PollRangeEnd);
    }

    [Fact]
    public async Task SetRange_Returns403_ForNonOrganiser()
    {
        var organiser = api.CreateClientWithUser("discord|poll-org-b");
        var other = api.CreateClientWithUser("discord|poll-other-b");
        var ev = await CreateDraftEvent(organiser, "b");

        var response = await other.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetRange_Returns400_WhenStartNotBeforeEnd()
    {
        var client = api.CreateClientWithUser("discord|poll-org-c");
        var ev = await CreateDraftEvent(client, "c");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 8, 1), new DateOnly(2030, 6, 1))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetRange_Returns409_WhenEventNotDraft()
    {
        var client = api.CreateClientWithUser("discord|poll-org-d");
        var ev = await CreateDraftEvent(client, "d");
        await client.PostAsync($"/api/v1/events/{ev.Id}/publish", null);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyEntries_UpsertsWeeks_WithinRange()
    {
        var client = api.CreateClientWithUser("discord|poll-org-e");
        var ev = await CreateDraftEvent(client, "e");
        await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 6, 3), "Available"),
                new DatePollEntryDto(new DateOnly(2030, 6, 10), "Partial"),
            ])
        );

        response.EnsureSuccessStatusCode();
        var entries = await response.Content.ReadFromJsonAsync<List<DatePollEntryDto>>();
        Assert.Equal(2, entries!.Count);
        Assert.Contains(
            entries,
            e => e.WeekStart == new DateOnly(2030, 6, 3) && e.Status == "Available"
        );

        // Re-submitting the same week updates rather than duplicating.
        var second = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 6, 3), "Unavailable"),
            ])
        );
        var updated = await second.Content.ReadFromJsonAsync<List<DatePollEntryDto>>();
        Assert.Equal(2, updated!.Count);
        Assert.Contains(
            updated,
            e => e.WeekStart == new DateOnly(2030, 6, 3) && e.Status == "Unavailable"
        );
    }

    [Fact]
    public async Task DeleteMyEntry_RemovesEntry()
    {
        var client = api.CreateClientWithUser("discord|poll-org-clear");
        var ev = await CreateDraftEvent(client, "clear");
        await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );
        await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 6, 3), "Available"),
            ])
        );

        var response = await client.DeleteAsync($"/api/v1/events/{ev.Id}/date-poll/me/2030-06-03");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var entries = await client.GetFromJsonAsync<List<DatePollEntryDto>>(
            $"/api/v1/events/{ev.Id}/date-poll/me"
        );
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task DeleteMyEntry_IsIdempotent_WhenEntryDoesNotExist()
    {
        var client = api.CreateClientWithUser("discord|poll-org-clear2");
        var ev = await CreateDraftEvent(client, "clear2");
        await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        var response = await client.DeleteAsync($"/api/v1/events/{ev.Id}/date-poll/me/2030-06-03");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyEntries_Returns400_WhenWeekOutsideRange()
    {
        var client = api.CreateClientWithUser("discord|poll-org-f");
        var ev = await CreateDraftEvent(client, "f");
        await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 1), new DateOnly(2030, 8, 1))
        );

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 9, 1), "Available"),
            ])
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyEntries_Returns409_WhenNoRangeSet()
    {
        var client = api.CreateClientWithUser("discord|poll-org-g");
        var ev = await CreateDraftEvent(client, "g");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 6, 3), "Available"),
            ])
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetSummary_AggregatesResponsesAcrossUsers()
    {
        var organiser = api.CreateClientWithUser("discord|poll-org-h");
        var ev = await CreateDraftEvent(organiser, "h");
        await organiser.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/range",
            new UpdatePollRangeRequest(new DateOnly(2030, 6, 3), new DateOnly(2030, 6, 17))
        );

        await organiser.PutAsJsonAsync(
            $"/api/v1/events/{ev.Id}/date-poll/me",
            new UpdateDatePollEntriesRequest([
                new DatePollEntryDto(new DateOnly(2030, 6, 3), "Available"),
            ])
        );

        var response = await organiser.GetAsync($"/api/v1/events/{ev.Id}/date-poll/");
        response.EnsureSuccessStatusCode();
        var summary = await response.Content.ReadFromJsonAsync<DatePollSummaryResponse>();

        var week = Assert.Single(summary!.Weeks, w => w.WeekStart == new DateOnly(2030, 6, 3));
        Assert.Equal(1, week.Available);
        Assert.Equal(0, week.NoResponse);

        var laterWeek = Assert.Single(summary.Weeks, w => w.WeekStart == new DateOnly(2030, 6, 10));
        Assert.Equal(0, laterWeek.Available);
        Assert.Equal(1, laterWeek.NoResponse);
    }
}
