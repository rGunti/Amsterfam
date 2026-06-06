# Domain Model

## Context

Amsterfam is a recurring annual friend group event in Amsterdam (~18–20 people), with a tail-end segment at TwitchCon Rotterdam for some attendees. Typical pattern: ~1 week Amsterdam + optional Rotterdam tail. Accommodation: Amsterdam Farm Lodge (multiple apartments).

This app replaces a Google Sheets spreadsheet covering availability, accommodation, costs, activity voting, comfort/consent preferences, and itinerary.

---

## Permission Tiers

| Tier | Scope | Capabilities |
|---|---|---|
| **Superuser** | App-wide | Manages users, comfort question templates, system-level info |
| **Organiser** | Event-scoped | Creates/manages event, rooms, bed assignments, itinerary, confirms attendees, finalises costs |
| **Attendee** | Event-scoped | Fills availability, preferences, activity votes; views own costs |
| **Pending/Guest** | Event-scoped | Sees event info, can RSVP; no access to comfort/consent data or costs until confirmed |

---

## Feature Areas

1. **Availability grid** — per-person date grid; statuses: NotComing / Maybe / Planned / Booked / DayTrip
2. **Accommodation management** — room/bed assignments per apartment per night
3. **Cost tracking** — per-person cost derived from bed-nights occupied (see Cost Model below)
4. **Comfort & consent preferences** — organiser-defined questions with seeded system defaults
5. **Activity suggestions & voting** — submit suggestions, vote 1–5, ranked results
6. **Itinerary** — day-by-day schedule with arrivals/departures and planned activities
7. **Shopping list** — collaborative list for shared supplies

Out of scope (for now): carpool coordination.

---

## Entities

### User
- `Id`, `ExternalId` (Authentik subject), `DisplayName`, `Email`, `AvatarUrl`, `CreatedAt`

### Event
- `Id`, `Name`, `Description`, `StartDate`, `EndDate`, `Location`
- `CostPerNight` (decimal)
- `Status`: Draft | Open | Closed
- `CreatedBy`, `CreatedAt`

### EventAttendance
Join between User and Event.
- `Id`, `EventId`, `UserId`, `Role` (Organiser | Attendee | Pending)
- `PlannedArrival`, `PlannedDeparture`
- `AmountPaid` (decimal)
- `CostOverride` (decimal?) — null = use calculated value; set by organiser for edge cases (complimentary stays, special arrangements)

Derived (not stored):
- `TotalAmountDue` = `CostOverride ?? (BedAssignment-nights for this user × Event.CostPerNight)`
- `OpenAmount` = `TotalAmountDue - AmountPaid`
- `PhysicalNightsStayed` = count of distinct `AvailabilityEntry` dates with status `Booked`

### Cost Model

Cost derives from `BedAssignment` records, not a multiplier on `EventAttendance`. Each bed a person occupies for a night = one billable person-night.

Examples:
- Standard attendee: 1 bed × 9 nights = 9 person-nights
- Private room (e.g. CPAP user occupying a double alone): 2 beds × 9 nights = 18 person-nights
- Partial private room: 2 beds × 3 nights + 1 bed × 6 nights = 12 person-nights

### AvailabilityEntry
Per user, per date, per event.
- `Id`, `EventId`, `UserId`, `Date`
- `Status`: NotComing | Maybe | Planned | Booked | DayTrip

### Accommodation
- `Id`, `EventId`, `Name` (e.g. "Farm Lodge Apt 1"), `Notes`

### Room
- `Id`, `AccommodationId`, `Name` (e.g. "Bedroom 2 next to living room")

### Bed
- `Id`, `RoomId`, `Name` (e.g. "Double bed 1", "Top bunk")
- `Type`: Double | Single | BunkTop | BunkBottom | Sofa

### BedAssignment
- `Id`, `BedId`, `UserId`, `DateFrom`, `DateTo`

### Activity
- `Id`, `EventId`, `SuggestedBy` (UserId), `Name`, `Type`, `Location`, `Notes`, `CreatedAt`

### ActivityVote
- `Id`, `ActivityId`, `UserId`, `Score` (1–5)

### ItineraryEntry
- `Id`, `EventId`, `Date`, `Time`, `Title`, `Notes`, `ActivityId` (optional FK), `CreatedBy`

### ShoppingItem
- `Id`, `EventId`, `Name`, `IsChecked`, `AddedBy` (UserId), `CreatedAt`

### ComfortQuestionTemplate
Reusable question definitions; system defaults seeded on startup.
- `Id`, `Label`
- `Type`: SingleChoice | MultiChoice | FreeText
- `Options` (JSON array of strings, for choice types)
- `IsDefault` (bool) — auto-added when a new event is created
- `IsRequired` (bool)
- `SortOrder`

### EventComfortQuestion
Join between `ComfortQuestionTemplate` and a specific Event. Allows organisers to add, remove, or reorder questions per event.
- `Id`, `EventId`, `TemplateId`, `SortOrder`, `IsRequired` (can override template default)

### ComfortAnswer
Per user per `EventComfortQuestion`.
- `Id`, `EventComfortQuestionId`, `UserId`, `AnswerValue` (JSON — supports single, multi, or free text)

---

## Default Comfort Questions (seeded)

| Question | Type | Options |
|---|---|---|
| Photos of you – Take | SingleChoice | Go ahead / Ask first / No |
| Photos of you – Share/Post | SingleChoice | Go ahead / Ask first / No |
| Hugs | SingleChoice | Go ahead / Ask first / No |
| Being around weed smoking | SingleChoice | Go ahead / Ask first / No |
| Support during nights out – Receiving | SingleChoice | I'm fine on my own / Check on me / Stay by my side |
| Support during nights out – Giving | SingleChoice | Happy to help / Available if needed / Not available |
| Help with public transport | SingleChoice | I'm fine on my own / Appreciate help being available / Need someone to travel with |
| Alcoholic drinks of choice | FreeText | — |
| Non-alcoholic drinks of choice | FreeText | — |
