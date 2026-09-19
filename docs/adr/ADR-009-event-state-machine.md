# ADR-009 — Event lifecycle as an explicit state machine with scheduled auto-transitions

**Status:** Accepted
**Date:** 2026-09-19

## Decision

Model the event lifecycle (issue #107) as a pure, static state machine in
`Amsterfam.Core/Entities/EventStateMachine.cs`: a table of allowed `(from, to, ownerOnly)` rules
plus guards (dates required to open). The API exposes a single
`POST /api/v1/events/{id}/status` endpoint and returns `allowedTransitions` on every event
response, so the frontend renders only the actions the current user can take.

Date-driven transitions (Open → InProgress, InProgress → Closed) run in a `BackgroundService`
on a configurable cron schedule (`AutoTransitions:Schedule`, parsed with Cronos), plus once at
startup to catch up after downtime. The run is idempotent.

## Reasons

- One rule table is easier to review against the issue's diagram than seven RPC endpoints each
  re-implementing checks, and it's unit-testable without a database.
- `allowedTransitions` keeps role/state logic on the server; the UI doesn't duplicate it.
- A cron expression makes it predictable *when* events flip — useful when testing the flow and
  when explaining to the group why an event changed state.
- Transitions are date-based, so a daily run shortly after midnight is enough; no need for
  lazy evaluation on read.

## Rejected Alternatives

**Per-action endpoints** (`/publish`, `/start`, `/close`, …) — matches the old style but
spreads the rules across handlers.
**Evaluating auto-transitions lazily on read** — no persisted state change to query or audit,
and every read path would need to remember to do it.
**Fixed `PeriodicTimer` interval** — less predictable run times than a cron schedule.
