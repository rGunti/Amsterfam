# ADR-012 — Event timeline as an explicit, per-endpoint change log

**Status:** Accepted
**Date:** 2026-09-23

## Decision

Every change to an event is recorded as an `EventLogEntry` row (issue #127) and shown to
confirmed members on a Timeline page, newest first. Endpoints call `EventLog.Record(...)`
with a typed `EventLogType` right before their own `SaveChanges`, so an entry is saved if
and only if the change is.

Each entry carries a `Visibility` (Everyone, Organisers, Owner) fixed at write time from
its type. Organiser-only business (join requests, declines, cost overrides, join links) is
Organisers; organiser links, which only the owner manages, are Owner. The Owner tier is
checked against the current owner when reading, so it follows ownership transfers.

## Reasons

- Typed entries make readable sentences ("Alice confirmed Bob") and keep the visibility
  rules in one place; a generic row-diff log would need translating back into intent.
- Writing in the same unit of work means no missed or phantom entries.
- The table is event-scoped and generic (type + JSON data), so a later news feed (#105)
  can post into the same timeline.

## Rules for new features

- Every POST/PUT/PATCH/DELETE endpoint is marked `.LogsToTimeline()` or
  `.NotLoggedToTimeline("reason")`. `TimelineCoverageTests` fails otherwise, so a new
  endpoint can't skip the decision; it can't check that the entry itself is right.

- Log only *that* a personal answer changed, never its content. Date poll saves are
  logged without the weeks; comfort & consent answers must not be logged at all.
- Chatty personal saves go through `RecordCoalescedAsync`, which folds repeats by the same
  person within 15 minutes into one entry.

## Rejected alternatives

- EF `SaveChanges` interceptor that logs every modified entity: catches everything but
  produces low-level diffs, can't tell a decline from a leave, and would log sensitive
  columns unless every one is excluded by hand.
- Filtering visibility by type at read time only: splitting e.g. "removed a pending
  request" from "removed an attendee" still needs distinct types, and storing visibility
  lets one type (join-link changes) vary by kind.
