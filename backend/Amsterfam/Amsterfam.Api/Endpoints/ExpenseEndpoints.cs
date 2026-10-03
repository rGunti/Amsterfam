using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Core.Expenses;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// Itemised expenses (ADR-013): confirmed members add expenses and record repayments; the
/// creator (or recorder) and organisers can change or remove them.
/// </summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/expenses").RequireAuthorization();

        group.MapGet("/", GetExpenses);
        group.MapPost("/", CreateExpense).LogsToTimeline();
        group.MapPut("/{expenseId:int}", UpdateExpense).LogsToTimeline();
        group.MapDelete("/{expenseId:int}", DeleteExpense).LogsToTimeline();
        group.MapGet("/balances", GetBalances);
        group.MapPost("/payments", RecordPayment).LogsToTimeline();
        group.MapDelete("/payments/{paymentId:int}", DeletePayment).LogsToTimeline();

        return app;
    }

    private record Caller(int UserId, bool IsOrganiser, string Currency);

    /// <summary>
    /// Non-members get 404 (as for the event itself), pending members 403. Writes also need
    /// the event not to be read-only.
    /// </summary>
    private static async Task<(Caller? Caller, IResult? Error)> AuthorizeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        ICurrentUserService currentUser,
        bool write
    )
    {
        var currency = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => e.Currency)
            .FirstOrDefaultAsync();
        if (currency is null)
            return (null, TypedResults.NotFound());

        var user = await currentUser.GetOrCreateAsync();
        var role = await EventGuards.RoleOf(db, eventId, user.Id);
        if (role is null)
            return (null, TypedResults.NotFound());
        if (!EventGuards.IsConfirmed(role))
            return (null, TypedResults.Forbid());
        if (await EventGuards.EnsureVisibleAsync(db, eventId, user.Id) is { } hidden)
            return (null, hidden);
        if (write && await EventGuards.EnsureWritableAsync(db, eventId) is { } readOnly)
            return (null, readOnly);

        return (new Caller(user.Id, role == AttendanceRole.Organiser, currency), null);
    }

    private static async Task<IResult> GetExpenses(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: false);
        if (caller is null)
            return error!;

        var expenses = await LoadExpenses(db, eventId);
        var payments = await LoadPayments(db, eventId);
        var canEdit = await EventGuards.EnsureWritableAsync(db, eventId) is null;

        return TypedResults.Ok(
            new ExpenseListResponse(
                caller.Currency,
                canEdit,
                await LoadMembers(db, eventId, expenses, payments),
                expenses.Select(x => ToResponse(x, caller, canEdit)).ToList(),
                payments.Select(p => ToResponse(p, caller, canEdit)).ToList()
            )
        );
    }

    private static async Task<IResult> GetBalances(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: false);
        if (caller is null)
            return error!;

        var balances = BalanceCalculator.NetBalances(
            await LoadExpenses(db, eventId),
            await LoadPayments(db, eventId)
        );

        return TypedResults.Ok(
            new BalancesResponse(
                caller.Currency,
                balances
                    .Where(b => b.Value != 0)
                    .OrderByDescending(b => b.Value)
                    .Select(b => new BalanceResponse(b.Key, b.Value))
                    .ToList(),
                BalanceCalculator
                    .SuggestTransfers(balances)
                    .Select(t => new TransferResponse(t.FromUserId, t.ToUserId, t.Amount))
                    .ToList()
            )
        );
    }

    private static async Task<IResult> CreateExpense(
        Guid eventId,
        [FromBody] UpsertExpenseRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
        if (caller is null)
            return error!;

        var (split, mode, invalid) = await ValidateAsync(db, eventId, request, existing: null);
        if (invalid is not null)
            return invalid;

        var expense = new Expense
        {
            EventId = eventId,
            Title = request.Title.Trim(),
            Amount = request.Amount,
            PaidById = request.PaidById,
            SplitMode = mode,
            CreatedById = caller.UserId,
            CreatedAt = time.GetUtcNow(),
            Shares = ToShares(split!),
        };
        db.Expenses.Add(expense);

        log.Record(
            eventId,
            EventLogType.ExpenseAdded,
            caller.UserId,
            data: LogData(expense, caller.Currency)
        );
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/v1/events/{eventId}/expenses/{expense.Id}",
            ToResponse(expense, caller, canEdit: true)
        );
    }

    private static async Task<IResult> UpdateExpense(
        Guid eventId,
        int expenseId,
        [FromBody] UpsertExpenseRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
        if (caller is null)
            return error!;

        var expense = await db
            .Expenses.Include(x => x.Shares)
            .FirstOrDefaultAsync(x => x.Id == expenseId && x.EventId == eventId);
        if (expense is null)
            return TypedResults.NotFound();
        if (!CanChange(expense.CreatedById, caller))
            return TypedResults.Forbid();

        var (split, mode, invalid) = await ValidateAsync(db, eventId, request, expense);
        if (invalid is not null)
            return invalid;

        expense.Title = request.Title.Trim();
        expense.Amount = request.Amount;
        expense.PaidById = request.PaidById;
        expense.SplitMode = mode;
        expense.UpdatedAt = time.GetUtcNow();
        expense.Shares.Clear();
        foreach (var share in ToShares(split!))
            expense.Shares.Add(share);

        log.Record(
            eventId,
            EventLogType.ExpenseUpdated,
            caller.UserId,
            data: LogData(expense, caller.Currency)
        );
        await db.SaveChangesAsync();

        return TypedResults.Ok(ToResponse(expense, caller, canEdit: true));
    }

    private static async Task<IResult> DeleteExpense(
        Guid eventId,
        int expenseId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
        if (caller is null)
            return error!;

        var expense = await db.Expenses.FirstOrDefaultAsync(x =>
            x.Id == expenseId && x.EventId == eventId
        );
        if (expense is null)
            return TypedResults.NotFound();
        if (!CanChange(expense.CreatedById, caller))
            return TypedResults.Forbid();

        db.Expenses.Remove(expense);
        log.Record(
            eventId,
            EventLogType.ExpenseDeleted,
            caller.UserId,
            data: LogData(expense, caller.Currency)
        );
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RecordPayment(
        Guid eventId,
        [FromBody] RecordPaymentRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
        if (caller is null)
            return error!;

        if (
            !caller.IsOrganiser
            && caller.UserId != request.FromUserId
            && caller.UserId != request.ToUserId
        )
            return TypedResults.Forbid();

        if (request.FromUserId == request.ToUserId)
            return BadRequest("A repayment needs two different people.");
        if (request.Amount <= 0 || request.Amount > ExpenseSplitter.MaxAmount)
            return BadRequest("The amount must be greater than zero.");
        if (!ExpenseSplitter.IsWholeCents(request.Amount))
            return BadRequest("The amount can have at most two decimal places.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > ExpensePayment.MaxNoteLength)
            return BadRequest(
                $"The note can be at most {ExpensePayment.MaxNoteLength} characters."
            );

        // Former members can still settle what they owe or are owed.
        var balances = BalanceCalculator.NetBalances(
            await LoadExpenses(db, eventId),
            await LoadPayments(db, eventId)
        );
        var confirmed = await ConfirmedMemberIds(db, eventId);
        bool CanTakePart(int userId) =>
            confirmed.Contains(userId) || balances.GetValueOrDefault(userId) != 0;
        if (!CanTakePart(request.FromUserId) || !CanTakePart(request.ToUserId))
            return BadRequest("Both people must be part of this event.");

        var payment = new ExpensePayment
        {
            EventId = eventId,
            FromUserId = request.FromUserId,
            ToUserId = request.ToUserId,
            Amount = request.Amount,
            Note = note,
            RecordedById = caller.UserId,
            CreatedAt = time.GetUtcNow(),
        };
        db.ExpensePayments.Add(payment);

        log.Record(
            eventId,
            EventLogType.PaymentRecorded,
            caller.UserId,
            data: await PaymentLogData(db, payment, caller.Currency)
        );
        await db.SaveChangesAsync();

        return TypedResults.Created(
            $"/api/v1/events/{eventId}/expenses/payments/{payment.Id}",
            ToResponse(payment, caller, canEdit: true)
        );
    }

    private static async Task<IResult> DeletePayment(
        Guid eventId,
        int paymentId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
        if (caller is null)
            return error!;

        var payment = await db.ExpensePayments.FirstOrDefaultAsync(p =>
            p.Id == paymentId && p.EventId == eventId
        );
        if (payment is null)
            return TypedResults.NotFound();
        if (!CanChange(payment.RecordedById, caller))
            return TypedResults.Forbid();

        db.ExpensePayments.Remove(payment);
        log.Record(
            eventId,
            EventLogType.PaymentDeleted,
            caller.UserId,
            data: await PaymentLogData(db, payment, caller.Currency)
        );
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Checks the request and resolves its split. Payer and participants must be confirmed
    /// members, except people already on <paramref name="existing"/>, who may have left since.
    /// </summary>
    private static async Task<(
        SplitResult? Split,
        ExpenseSplitMode Mode,
        IResult? Error
    )> ValidateAsync(
        AmsterfamDbContext db,
        Guid eventId,
        UpsertExpenseRequest request,
        Expense? existing
    )
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, default, BadRequest("Give the expense a title."));
        if (request.Title.Trim().Length > Expense.MaxTitleLength)
            return (
                null,
                default,
                BadRequest($"The title can be at most {Expense.MaxTitleLength} characters.")
            );

        // Enum.TryParse would also accept numeric strings, so match on names only.
        if (!Enum.GetNames<ExpenseSplitMode>().Contains(request.SplitMode))
            return (null, default, BadRequest($"Unknown split mode '{request.SplitMode}'."));
        var mode = Enum.Parse<ExpenseSplitMode>(request.SplitMode);

        var allowed = await ConfirmedMemberIds(db, eventId);
        if (existing is not null)
        {
            allowed.Add(existing.PaidById);
            allowed.UnionWith(existing.Shares.Select(s => s.UserId));
        }

        if (!allowed.Contains(request.PaidById))
            return (null, mode, BadRequest("The payer must be part of this event."));
        if (request.Shares.Any(s => !allowed.Contains(s.UserId)))
            return (null, mode, BadRequest("Every participant must be part of this event."));

        var split = ExpenseSplitter.Split(
            request.Amount,
            mode,
            request.Shares.Select(s => new SplitParticipant(s.UserId, s.Value)).ToList()
        );
        if (split.Error is not null)
            return (null, mode, BadRequest(split.Error));

        return (split, mode, null);
    }

    private static bool CanChange(int ownerId, Caller caller) =>
        caller.IsOrganiser || ownerId == caller.UserId;

    private static IResult BadRequest(string error) => TypedResults.BadRequest(new { error });

    private static List<ExpenseShare> ToShares(SplitResult split) =>
        split
            .Shares.Select(s => new ExpenseShare
            {
                UserId = s.UserId,
                Amount = s.Amount,
                Percentage = s.Percentage,
            })
            .ToList();

    private static Task<List<Expense>> LoadExpenses(AmsterfamDbContext db, Guid eventId) =>
        db
            .Expenses.Where(x => x.EventId == eventId)
            .Include(x => x.Shares)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

    private static Task<List<ExpensePayment>> LoadPayments(AmsterfamDbContext db, Guid eventId) =>
        db
            .ExpensePayments.Where(p => p.EventId == eventId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync();

    private static async Task<HashSet<int>> ConfirmedMemberIds(
        AmsterfamDbContext db,
        Guid eventId
    ) =>
        (
            await db
                .EventAttendances.Where(a =>
                    a.EventId == eventId
                    && (a.Role == AttendanceRole.Attendee || a.Role == AttendanceRole.Organiser)
                )
                .Select(a => a.UserId)
                .ToListAsync()
        ).ToHashSet();

    /// <summary>Confirmed members, plus anyone who appears in an expense or repayment.</summary>
    private static async Task<List<ExpenseMember>> LoadMembers(
        AmsterfamDbContext db,
        Guid eventId,
        List<Expense> expenses,
        List<ExpensePayment> payments
    )
    {
        var confirmed = await ConfirmedMemberIds(db, eventId);
        var ids = confirmed
            .Concat(expenses.Select(x => x.PaidById))
            .Concat(expenses.SelectMany(x => x.Shares.Select(s => s.UserId)))
            .Concat(payments.SelectMany(p => new[] { p.FromUserId, p.ToUserId }))
            .ToHashSet();

        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        return users
            .Select(u => new ExpenseMember(
                u.Id,
                u.DisplayName ?? u.Handle,
                u.AvatarUrl,
                confirmed.Contains(u.Id)
            ))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static object LogData(Expense expense, string currency) =>
        new
        {
            title = expense.Title,
            amount = expense.Amount,
            currency,
        };

    private static async Task<object> PaymentLogData(
        AmsterfamDbContext db,
        ExpensePayment payment,
        string currency
    )
    {
        var names = await db
            .Users.Where(u => u.Id == payment.FromUserId || u.Id == payment.ToUserId)
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Handle);
        return new
        {
            fromId = payment.FromUserId,
            from = names.GetValueOrDefault(payment.FromUserId),
            toId = payment.ToUserId,
            to = names.GetValueOrDefault(payment.ToUserId),
            amount = payment.Amount,
            currency,
        };
    }

    private static ExpenseResponse ToResponse(Expense x, Caller caller, bool canEdit) =>
        new(
            x.Id,
            x.Title,
            x.Amount,
            x.PaidById,
            x.SplitMode.ToString(),
            x.Shares.OrderBy(s => s.UserId)
                .Select(s => new ExpenseShareResponse(s.UserId, s.Amount, s.Percentage))
                .ToList(),
            x.CreatedById,
            x.CreatedAt,
            x.UpdatedAt,
            canEdit && CanChange(x.CreatedById, caller)
        );

    private static ExpensePaymentResponse ToResponse(
        ExpensePayment p,
        Caller caller,
        bool canEdit
    ) =>
        new(
            p.Id,
            p.FromUserId,
            p.ToUserId,
            p.Amount,
            p.Note,
            p.RecordedById,
            p.CreatedAt,
            canEdit && CanChange(p.RecordedById, caller)
        );
}
