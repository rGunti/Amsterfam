# API Reference

## Style

REST with RPC-style endpoints (`POST` + verb in path) where actions don't map cleanly to CRUD (e.g. confirming an attendee, marking a payment received, checking off a shopping item).

## Design Notes

- All event-scoped resources nested under `/api/v1/events/{id}` — event membership checked once at route group level
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

### Events
```
GET    /api/v1/events
POST   /api/v1/events
GET    /api/v1/events/{id}
PUT    /api/v1/events/{id}
DELETE /api/v1/events/{id}
POST   /api/v1/events/{id}/publish          (RPC – Draft → Open)
POST   /api/v1/events/{id}/close            (RPC – Open → Closed)
```

### Attendance
```
GET    /api/v1/events/{id}/attendees
POST   /api/v1/events/{id}/attendees/join                     (RPC – current user RSVPs)
POST   /api/v1/events/{id}/attendees/{userId}/confirm         (RPC – organiser confirms pending)
DELETE /api/v1/events/{id}/attendees/{userId}
PUT    /api/v1/events/{id}/attendees/{userId}
```

### Availability
```
GET /api/v1/events/{id}/availability
PUT /api/v1/events/{id}/availability/me     (upsert current user's entries)
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
