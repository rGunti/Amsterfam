# ADR-006 — No SignalR (Polling Instead)

**Status:** Accepted  
**Date:** 2026-06-06

## Decision

No real-time WebSocket connections. Pages where freshness matters use polling at a 30–60s interval. The shopping list gets a manual refresh button.

## Reasons

- Most inputs (availability, preferences, votes) are personal and rarely conflict
- Organiser-only features (itinerary, room assignments) have low simultaneous-edit risk
- Polling is sufficient for a ~20-person group
- Avoids WebSocket connection management complexity

## Revisit Trigger

If real usage reveals real-time as a genuine pain point (most likely on the shopping list), SignalR can be added incrementally.
