# API Reference

## Style

REST with RPC-style endpoints (`POST` + verb in path) where actions don't map cleanly to CRUD (e.g. confirming an attendee, marking a payment received, checking off a shopping item).

## Design Notes

- All event-scoped resources nested under `/api/v1/events/{id}` — event membership checked once at route group level
- `{id}` in all event-scoped routes is a GUID (`Event.Id`), not a sequential integer — keeps event URLs from being guessable/enumerable. Child-entity ids not directly tied to Event stay plain integers
- `/me` shortcuts for common self-service operations; frontend doesn't need to track current user ID
- Superuser-only endpoints grouped under `/api/v1/admin/` — single policy applied to entire route group
- URL versioning: `/api/v1/...`

---

## Endpoints

### Auth & Users
```
GET  /api/v1/me
PUT  /api/v1/me
```

### Payment Methods
```
GET    /api/v1/me/payment-methods
POST   /api/v1/me/payment-methods
PUT    /api/v1/me/payment-methods/{id}
DELETE /api/v1/me/payment-methods/{id}
GET    /api/v1/users/{userId}/payment-methods    (read-only; any authenticated user, not just /me)
```

### Events
```
GET    /api/v1/events
POST   /api/v1/events
GET    /api/v1/events/{id}
PUT    /api/v1/events/{id}
DELETE /api/v1/events/{id}                 (owner only; event must be Archived or Cancelled)
POST   /api/v1/events/{id}/status          (RPC – body { "target": "<EventStatus>" })
```

`POST /status` runs the event state machine (see `domain.md` → Event lifecycle):
400 unknown status, 403 not permitted for the caller's role, 409 transition not possible from the
current state or a guard failed (`{ "error": "..." }`). `EventResponse.allowedTransitions` lists
the targets the current user may move the event to right now.

### Attendance
```
GET    /api/v1/events/{id}/attendees
POST   /api/v1/events/{id}/attendees/{userId}/confirm         (RPC – organiser confirms pending)
DELETE /api/v1/events/{id}/attendees/{userId}
PUT    /api/v1/events/{id}/attendees/{userId}
```
There is no open "join" endpoint; joining always goes through a join link. `confirm` on a
pending attendee who joined via an organiser link is owner-only and grants Organiser.

### Join Links
```
GET    /api/v1/events/{id}/join-links              (organiser; owner also sees organiser links)
POST   /api/v1/events/{id}/join-links              (organiser; kind=Organiser is owner-only; body: kind, label?, expiresAt?, maxUses?)
DELETE /api/v1/events/{id}/join-links/{linkId}     (revoke; owner, or the organiser who created an attendee link)
GET    /api/v1/join-links/{token}                  (preview: event name/dates, kind; 404 if invalid/expired/revoked/full)
POST   /api/v1/join-links/{token}/join             (RPC – current user requests to join; lands Pending)
```
- `label` is optional (max 60 chars) and defaults to "Attendee link" / "Organiser link". Attendee rows returned to organisers include `joinLinkLabel`, the label of the link they joined through.
- Tokens are 32 random bytes, base64url. The link is `/join/{token}` in the frontend.
- Invalid, revoked, expired and used-up tokens are indistinguishable (404).
- Attendee links need an event that accepts joins; organiser links also work in Draft.
- `GET /api/v1/events/{id}` returns 404 to non-members.

### Availability
```
GET /api/v1/events/{id}/availability
PUT /api/v1/events/{id}/availability/me     (upsert current user's entries)
```

### Date Poll
```
PUT  /api/v1/events/{id}/date-poll/range        (organiser sets PollRangeStart/End; Draft only)
GET  /api/v1/events/{id}/date-poll               (per-week aggregated counts, organiser summary)
GET  /api/v1/events/{id}/date-poll/me            (current user's own entries)
PUT  /api/v1/events/{id}/date-poll/me            (upsert current user's week responses)
```

### Accommodation
```
GET    /api/v1/events/{id}/accommodations
POST   /api/v1/events/{id}/accommodations
PUT    /api/v1/events/{id}/accommodations/{accommodationId}
DELETE /api/v1/events/{id}/accommodations/{accommodationId}
POST   /api/v1/events/{id}/accommodations/{accommodationId}/rooms
PUT    /api/v1/events/{id}/accommodations/{accommodationId}/rooms/{roomId}
DELETE /api/v1/events/{id}/accommodations/{accommodationId}/rooms/{roomId}
POST   /api/v1/events/{id}/bed-assignments
DELETE /api/v1/events/{id}/bed-assignments/{id}
```

### Costs
```
GET  /api/v1/events/{id}/costs
GET  /api/v1/events/{id}/costs/me
POST /api/v1/events/{id}/costs/{userId}/mark-upfront-paid     (RPC)
POST /api/v1/events/{id}/costs/{userId}/mark-final-paid       (RPC)
```

### Activities
```
GET    /api/v1/events/{id}/activities
POST   /api/v1/events/{id}/activities
DELETE /api/v1/events/{id}/activities/{activityId}
PUT    /api/v1/events/{id}/activities/{activityId}/vote        (upsert score)
DELETE /api/v1/events/{id}/activities/{activityId}/vote
```

### Itinerary
```
GET    /api/v1/events/{id}/itinerary
POST   /api/v1/events/{id}/itinerary
PUT    /api/v1/events/{id}/itinerary/{entryId}
DELETE /api/v1/events/{id}/itinerary/{entryId}
```

### Shopping List
```
GET    /api/v1/events/{id}/shopping
POST   /api/v1/events/{id}/shopping
DELETE /api/v1/events/{id}/shopping/{itemId}
POST   /api/v1/events/{id}/shopping/{itemId}/check            (RPC)
POST   /api/v1/events/{id}/shopping/{itemId}/uncheck          (RPC)
```

### Comfort Preferences
```
GET    /api/v1/events/{id}/comfort/questions
POST   /api/v1/events/{id}/comfort/questions
DELETE /api/v1/events/{id}/comfort/questions/{questionId}
PUT    /api/v1/events/{id}/comfort/questions/reorder          (RPC)
GET    /api/v1/events/{id}/comfort/answers                    (organiser sees all)
GET    /api/v1/events/{id}/comfort/answers/me
PUT    /api/v1/events/{id}/comfort/answers/me                 (upsert current user's answers)
```

### Admin (Superuser only)
```
GET    /api/v1/admin/users
GET    /api/v1/admin/users/{id}
DELETE /api/v1/admin/users/{id}
GET    /api/v1/admin/comfort/templates
POST   /api/v1/admin/comfort/templates
PUT    /api/v1/admin/comfort/templates/{id}
DELETE /api/v1/admin/comfort/templates/{id}
```
