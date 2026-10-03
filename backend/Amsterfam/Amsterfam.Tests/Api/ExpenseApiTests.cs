using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class ExpenseApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Trip(Guid EventId, HttpClient Owner, int OwnerId, HttpClient Guest, int GuestId);

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>An open event with its owner and one confirmed attendee.</summary>
    private async Task<Trip> CreateTrip(string suffix, string? currency = null)
    {
        var owner = api.CreateClientWithUser($"discord|exp-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|exp-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Expense Test {suffix}",
                null,
                new DateOnly(2030, 7, 1),
                new DateOnly(2030, 7, 8),
                "Amsterdam",
                currency
            )
        );
        response.EnsureSuccessStatusCode();
        var ev = (await response.Content.ReadFromJsonAsync<EventResponse>())!;
        await owner.TransitionThroughAsync(ev.Id, "Open");

        (await guest.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();
        var guestId = await MyId(guest);
        (
            await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{guestId}/confirm", null)
        ).EnsureSuccessStatusCode();

        return new Trip(ev.Id, owner, await MyId(owner), guest, guestId);
    }

    private static UpsertExpenseRequest EqualSplit(decimal amount, int payer, params int[] ids) =>
        new(
            "Groceries",
            amount,
            payer,
            "Equal",
            ids.Select(id => new ExpenseShareRequest(id, null)).ToList()
        );

    private static Task<HttpResponseMessage> AddExpense(
        HttpClient client,
        Guid eventId,
        UpsertExpenseRequest request
    ) => client.PostAsJsonAsync($"/api/v1/events/{eventId}/expenses/", request);

    private static async Task<ExpenseResponse> AddExpenseOk(
        HttpClient client,
        Guid eventId,
        UpsertExpenseRequest request
    )
    {
        var response = await AddExpense(client, eventId, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseResponse>())!;
    }

    private static async Task<BalancesResponse> Balances(HttpClient client, Guid eventId) =>
        (
            await client.GetFromJsonAsync<BalancesResponse>(
                $"/api/v1/events/{eventId}/expenses/balances"
            )
        )!;

    [Fact]
    public async Task CreateExpense_ResolvesSharesAndShowsInList()
    {
        var t = await CreateTrip("1", "GBP");

        var created = await AddExpenseOk(
            t.Guest,
            t.EventId,
            EqualSplit(10m, t.GuestId, t.OwnerId, t.GuestId)
        );

        Assert.Equal(10m, created.Shares.Sum(s => s.Amount));
        Assert.True(created.CanEdit);

        var list = await t.Owner.GetFromJsonAsync<ExpenseListResponse>(
            $"/api/v1/events/{t.EventId}/expenses/"
        );
        Assert.Equal("GBP", list!.Currency);
        Assert.Single(list.Expenses);
        Assert.Contains(list.Members, m => m.UserId == t.GuestId && m.IsConfirmed);
    }

    [Fact]
    public async Task Balances_ReflectExpensesAndRepayments()
    {
        var t = await CreateTrip("2");
        await AddExpenseOk(t.Owner, t.EventId, EqualSplit(30m, t.OwnerId, t.OwnerId, t.GuestId));

        var before = await Balances(t.Owner, t.EventId);
        Assert.Equal(15m, before.Balances.Single(b => b.UserId == t.OwnerId).Balance);
        var transfer = Assert.Single(before.Transfers);
        Assert.Equal(
            (t.GuestId, t.OwnerId, 15m),
            (transfer.FromUserId, transfer.ToUserId, transfer.Amount)
        );

        var paid = await t.Guest.PostAsJsonAsync(
            $"/api/v1/events/{t.EventId}/expenses/payments",
            new RecordPaymentRequest(t.GuestId, t.OwnerId, 15m, "Tikkie")
        );
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);

        var after = await Balances(t.Owner, t.EventId);
        Assert.Empty(after.Balances);
        Assert.Empty(after.Transfers);
    }

    [Fact]
    public async Task UpdateAndDelete_AllowedForCreatorAndOrganiser_NotOtherAttendees()
    {
        var t = await CreateTrip("3");
        var byOwner = await AddExpenseOk(
            t.Owner,
            t.EventId,
            EqualSplit(20m, t.OwnerId, t.OwnerId, t.GuestId)
        );
        var byGuest = await AddExpenseOk(
            t.Guest,
            t.EventId,
            EqualSplit(20m, t.GuestId, t.OwnerId, t.GuestId)
        );

        var forbidden = await t.Guest.PutAsJsonAsync(
            $"/api/v1/events/{t.EventId}/expenses/{byOwner.Id}",
            EqualSplit(25m, t.OwnerId, t.OwnerId, t.GuestId)
        );
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await t.Guest.DeleteAsync($"/api/v1/events/{t.EventId}/expenses/{byOwner.Id}")
            ).StatusCode
        );

        var ownUpdate = await t.Guest.PutAsJsonAsync(
            $"/api/v1/events/{t.EventId}/expenses/{byGuest.Id}",
            new UpsertExpenseRequest(
                "Taxi",
                25m,
                t.GuestId,
                "Exact",
                [new(t.OwnerId, 20m), new(t.GuestId, 5m)]
            )
        );
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
        var updated = (await ownUpdate.Content.ReadFromJsonAsync<ExpenseResponse>())!;
        Assert.Equal("Exact", updated.SplitMode);
        Assert.Equal(20m, updated.Shares.Single(s => s.UserId == t.OwnerId).Amount);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await t.Owner.DeleteAsync($"/api/v1/events/{t.EventId}/expenses/{byGuest.Id}")
            ).StatusCode
        );
    }

    [Fact]
    public async Task CreateExpense_RejectsNonMembersAndBadSplits()
    {
        var t = await CreateTrip("4");
        var outsiderId = await MyId(api.CreateClientWithUser("discord|exp-outsider-4"));

        var outsider = await AddExpense(
            t.Owner,
            t.EventId,
            EqualSplit(10m, t.OwnerId, t.OwnerId, outsiderId)
        );
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);

        var badPercent = await AddExpense(
            t.Owner,
            t.EventId,
            new UpsertExpenseRequest(
                "Dinner",
                10m,
                t.OwnerId,
                "Percentage",
                [new(t.OwnerId, 60m), new(t.GuestId, 50m)]
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, badPercent.StatusCode);
    }

    [Fact]
    public async Task UnassignedRemainder_IsSaved_OwedToPayer_AndDoesNotBlockArchiving()
    {
        var t = await CreateTrip("10");
        var created = await AddExpenseOk(
            t.Owner,
            t.EventId,
            new UpsertExpenseRequest("Receipt", 50m, t.OwnerId, "Exact", [new(t.GuestId, 20m)])
        );
        Assert.Equal(30m, created.Unassigned);

        var balances = await Balances(t.Guest, t.EventId);
        Assert.Equal(30m, balances.Unassigned);
        var owner = balances.Balances.Single(b => b.UserId == t.OwnerId);
        Assert.Equal((50m, 30m), (owner.Balance, owner.Unassigned));
        // Only the assigned part is suggested as a repayment.
        var transfer = Assert.Single(balances.Transfers);
        Assert.Equal(20m, transfer.Amount);

        (
            await t.Guest.PostAsJsonAsync(
                $"/api/v1/events/{t.EventId}/expenses/payments",
                new RecordPaymentRequest(t.GuestId, t.OwnerId, 20m, null)
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.OK,
            (await t.Owner.TransitionAsync(t.EventId, "Archived")).StatusCode
        );
    }

    [Fact]
    public async Task Expenses_AreListedByDate_NewestFirst_DefaultingToToday()
    {
        var t = await CreateTrip("11");
        UpsertExpenseRequest On(string title, DateOnly? date) =>
            EqualSplit(10m, t.OwnerId, t.OwnerId) with
            {
                Title = title,
                Date = date,
            };

        await AddExpenseOk(t.Owner, t.EventId, On("Middle", new DateOnly(2030, 7, 3)));
        await AddExpenseOk(t.Owner, t.EventId, On("Earliest", new DateOnly(2030, 7, 1)));
        var undated = await AddExpenseOk(t.Owner, t.EventId, On("Undated", null));
        await AddExpenseOk(t.Owner, t.EventId, On("Latest", new DateOnly(2030, 7, 5)));

        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), undated.Date);
        var list = await t.Owner.GetFromJsonAsync<ExpenseListResponse>(
            $"/api/v1/events/{t.EventId}/expenses/"
        );
        // Today is before the 2030 trip dates, so the undated one comes last.
        Assert.Equal(
            ["Latest", "Middle", "Earliest", "Undated"],
            list!.Expenses.Select(x => x.Title)
        );
    }

    [Fact]
    public async Task Expenses_HiddenFromPendingAndNonMembers()
    {
        var t = await CreateTrip("5");
        var pending = api.CreateClientWithUser("discord|exp-pending-5");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|exp-stranger-5");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.GetAsync($"/api/v1/events/{t.EventId}/expenses/")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/events/{t.EventId}/expenses/")).StatusCode
        );
    }

    [Fact]
    public async Task RecordPayment_OnlyBetweenSelfUnlessOrganiser()
    {
        var t = await CreateTrip("6");
        var third = api.CreateClientWithUser("discord|exp-third-6");
        (await third.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var thirdId = await MyId(third);
        (
            await t.Owner.PostAsync($"/api/v1/events/{t.EventId}/attendees/{thirdId}/confirm", null)
        ).EnsureSuccessStatusCode();

        var forOthers = await t.Guest.PostAsJsonAsync(
            $"/api/v1/events/{t.EventId}/expenses/payments",
            new RecordPaymentRequest(thirdId, t.OwnerId, 5m, null)
        );
        Assert.Equal(HttpStatusCode.Forbidden, forOthers.StatusCode);

        var byOrganiser = await t.Owner.PostAsJsonAsync(
            $"/api/v1/events/{t.EventId}/expenses/payments",
            new RecordPaymentRequest(thirdId, t.GuestId, 5m, null)
        );
        Assert.Equal(HttpStatusCode.Created, byOrganiser.StatusCode);
    }

    [Fact]
    public async Task Expenses_WriteTimelineEntries()
    {
        var t = await CreateTrip("7");
        var expense = await AddExpenseOk(
            t.Owner,
            t.EventId,
            EqualSplit(12m, t.OwnerId, t.OwnerId, t.GuestId)
        );
        await t.Owner.DeleteAsync($"/api/v1/events/{t.EventId}/expenses/{expense.Id}");

        var entries = await t.Guest.GetFromJsonAsync<List<TimelineEntryResponse>>(
            $"/api/v1/events/{t.EventId}/timeline"
        );

        Assert.Equal(["ExpenseDeleted", "ExpenseAdded"], entries!.Take(2).Select(e => e.Type));
        Assert.Equal("Groceries", entries![1].Data!.Value.GetProperty("title").GetString());
        Assert.Equal(12m, entries[1].Data!.Value.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Archive_BlockedWhileBalancesAreOpen()
    {
        var t = await CreateTrip("8");
        await AddExpenseOk(t.Owner, t.EventId, EqualSplit(10m, t.OwnerId, t.OwnerId, t.GuestId));

        var blocked = await t.Owner.TransitionAsync(t.EventId, "Archived");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        (
            await t.Guest.PostAsJsonAsync(
                $"/api/v1/events/{t.EventId}/expenses/payments",
                new RecordPaymentRequest(t.GuestId, t.OwnerId, 5m, null)
            )
        ).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.OK,
            (await t.Owner.TransitionAsync(t.EventId, "Archived")).StatusCode
        );

        // Archived events are read-only.
        var late = await AddExpense(t.Owner, t.EventId, EqualSplit(10m, t.OwnerId, t.OwnerId));
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task Currency_DefaultsToEur_AndIsLockedOnceExpensesExist()
    {
        var t = await CreateTrip("9");
        var ev = await t.Owner.GetFromJsonAsync<EventResponse>($"/api/v1/events/{t.EventId}");
        Assert.Equal("EUR", ev!.Currency);

        UpdateEventRequest Update(string currency) =>
            new(ev.Name, ev.Description, ev.StartDate, ev.EndDate, ev.Location, currency);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await t.Owner.PutAsJsonAsync($"/api/v1/events/{t.EventId}", Update("XYZ"))).StatusCode
        );

        var changed = await t.Owner.PutAsJsonAsync($"/api/v1/events/{t.EventId}", Update("CHF"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("CHF", (await changed.Content.ReadFromJsonAsync<EventResponse>())!.Currency);

        await AddExpenseOk(t.Owner, t.EventId, EqualSplit(10m, t.OwnerId, t.OwnerId));

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.PutAsJsonAsync($"/api/v1/events/{t.EventId}", Update("EUR"))).StatusCode
        );
        // Leaving the currency out keeps it, so other edits still work.
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await t.Owner.PutAsJsonAsync(
                    $"/api/v1/events/{t.EventId}",
                    new UpdateEventRequest(
                        "Renamed",
                        ev.Description,
                        ev.StartDate,
                        ev.EndDate,
                        ev.Location
                    )
                )
            ).StatusCode
        );
    }
}
