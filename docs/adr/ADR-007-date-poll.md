# ADR-007 — Date-Finding Poll

**Status:** Accepted
**Date:** 2026-08-30

## Decision

Widen `Event` with a nullable `PollRangeStart`/`PollRangeEnd` rather than introduce a separate top-level poll entity. Responses are captured in a new `DatePollEntry` (week-granularity, `Available | Unavailable | Partial`) rather than reusing `AvailabilityEntry`.

## Reasons

- Keeps one `Event` row across its whole lifecycle — no "convert poll to event" step once a date is chosen.
- Week granularity matches the "weeks or months" framing organisers actually think in, and keeps the UI/data small for ~20 people.
- A distinct entity avoids conflating the pre-decision poll (finding a date) with the post-decision attendance grid (`AvailabilityEntry`, confirming a fixed date) in one status enum — the two have different lifecycles and different meanings for the same word "availability".

## Revisit Trigger

If organisers want day-level precision, or need multiple concurrent poll rounds per event, reconsider a standalone poll entity.
