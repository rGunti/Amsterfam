# ADR-013 — Itemised expenses with balances computed on read

**Status:** Accepted
**Date:** 2026-10-03

## Decision

Attendees track shared spending Splitwise-style (issue #57). An `Expense` has a single
payer and is split among chosen participants equally, by percentage or by exact amounts.
Each participant's part is stored as an `ExpenseShare` holding the **resolved money
amount**, whatever the split mode. The percentage is kept only so the edit form can show it.
Repayments between two people are `ExpensePayment` rows.

Balances and suggested transfers are **computed on read** from expenses and repayments
(`BalanceCalculator`), never stored.

Every event has one `Currency` (ISO 4217, default EUR). All of its amounts are in that
currency and nothing is converted. The currency can only change while the event has no
expenses or repayments.

This is separate from the bed-night cost model in #27, which isn't built yet.

## Rules

- **Amounts are whole cents** (`numeric(10,2)`). Equal and percentage splits round each
  share down and give the leftover cents, one each, to the shares that lost most to
  rounding, ties broken by user id. Shares therefore always add up to the total exactly,
  and the frontend preview (`shared/expense-split.ts`) gives the same result.
- **Permissions:**
  - Confirmed members (Attendee or Organiser) add expenses and record repayments.
  - Whoever created an expense, or recorded a repayment, can change or remove it, and so can any organiser.
  - Non-organisers can only record repayments they made or received.
  - Pending members see nothing.
- **Former members:** people who have left stay in existing expenses. They can still settle up, but can't be added to new expenses.
- **Archiving:** Closed → Archived is blocked while any balance is non-zero (`ExpenseBalanceCheck`).
- **Timeline:** expense and repayment changes are logged for everyone, with the title or the people involved and the amount.

## Reasons

- Storing resolved amounts means balances never depend on re-running rounding rules,
  and a later change to the split logic can't silently move old money.
- Computing balances on read can't go stale when an expense is edited or deleted. With
  ~20 people and a few hundred rows per trip, that's cheap.
- A single payer keeps both the model and the form simple. If two people paid, it's two expenses.
- One currency per event, without conversion, avoids exchange rates. Locking the currency
  once money is recorded stops amounts from being silently relabelled.

## Rejected alternatives

- **Storing running balances per attendee:** every edit would need a compensating update, and drift would be hard to spot.
- **A currency per expense, with conversion:** needs exchange rates and makes balances ambiguous. That's more than a friend-group trip needs.
- **Folding bed-night costs (#27) into expenses now:** deferred until #27 is designed.
