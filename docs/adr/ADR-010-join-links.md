# ADR-010 — Join links replace the open event URL

**Status:** Accepted
**Date:** 2026-09-20

## Decision

Joining an event requires a join link (issue #103). Links are `EventJoinLink` rows with a
32-byte random base64url token, optional expiry, optional max uses and a revoke timestamp.
The former `POST /events/{id}/attendees/join` and the non-member event preview are removed:
knowing an event GUID reveals nothing.

Redeeming a link creates a Pending attendance; organisers still confirm. Organiser links
(owner-created only) set `RequestedOrganiser` on the attendance; only the owner can confirm
those, which grants Organiser directly. Organiser links also work while the event is Draft.

Use counts are claimed with a conditional `UPDATE ... WHERE UseCount < MaxUses` in the same
transaction as the attendance insert, so concurrent redemptions cannot exceed the limit.
Bad, expired, revoked and full tokens all return 404 to avoid an oracle.

## Reasons

- The old link never changed and couldn't be revoked, so it could be abused once leaked.
- Approval-always keeps the existing trust model; links only control who can *ask*.

## Rejected alternatives

- Keeping the open join behind an owner toggle: extra state for no real use case.
- Auto-granting Organiser on an organiser link: bypasses the owner-approval requirement.
- Hashing tokens at rest: links must be re-copyable by organisers, so the token is stored plain.
