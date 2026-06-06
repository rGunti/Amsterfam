# Amsterfam

A self-hosted PWA for organising a recurring annual friend group trip to Amsterdam (~18–20 people). Replaces a Google Sheets spreadsheet.

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Angular (PWA) |
| Backend | .NET 10 Minimal API |
| Database | PostgreSQL |
| Auth | Authentik (self-hosted, Discord OAuth primary) |
| Infrastructure | Docker Compose + Terraform |

## Running the Dev Stack

```bash
docker compose up        # starts all services
docker compose down -v   # tear down including volumes
```

> Fill in: any env file setup, local secrets, ports for frontend/backend/Authentik.

## Project Structure

```
/
├── frontend/            # Angular PWA
├── backend/             # .NET 10 Minimal API
│   ├── Migrations/      # EF Core migrations
│   └── ...
├── infra/               # Terraform + Docker Compose files
├── docs/
│   ├── adr/             # Architecture Decision Records
│   ├── domain.md        # Domain model & entities
│   └── api.md           # API contracts / OpenAPI spec
└── .claude/
    ├── CLAUDE.md        # ← this file
    └── commands/        # Custom slash commands
```

> Fill in: actual directory names once scaffolded.

## Domain Model

### Core Entities

- **Event** — a single year's trip (dates, accommodation, status)
- **User** — a person; authenticated via Authentik/Discord
- **Attendance** — a user's participation in an event (includes comfort/consent preferences)
- **AvailabilityEntry** — per-user, per-date availability status
- **Accommodation** — a property/room used during the event
- **Bed** — a specific sleeping spot within an accommodation
- **BedAssignment** — links a user to a bed for a date range; drives cost tracking
- **Activity** — a suggested activity; has votes
- **ShoppingItem** — an item on the collaborative shopping list

### Permission Tiers

1. **Superuser** — full platform access, manages users and events
2. **Organiser** — event-scoped admin; manages accommodation, assignments, itinerary
3. **Attendee** — confirmed participant; can update their own availability, preferences, votes
4. **Pending/Guest** — limited view; cannot participate until confirmed

## Key Conventions

> Fill in: coding conventions (e.g. C# naming, Angular component structure, API endpoint patterns, EF Core approach — code-first vs db-first, etc.)

## Common Tasks

### Add a DB migration

```bash
# from the backend directory
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### Run backend tests

```bash
# fill in
```

### Run frontend tests

```bash
# fill in
```

## Notes for AI Assistants

- This is a **close-knit friend group app**, not a generic events platform. Keep suggestions appropriately scoped — no need for enterprise-scale patterns.
- The **comfort & consent preference questions** (e.g. hugs, photos, support at nights out) are a deliberate, thoughtful feature. Treat them with care.
- **Cost tracking** is per bed-night occupied, derived from BedAssignments.
- Primary login is **Discord OAuth** via Authentik. Do not suggest replacing Authentik with a simpler auth approach unless explicitly asked.
