# Amsterfam

A self-hosted progressive web app for organising a recurring annual friend group trip to Amsterdam (~18–20 people). Replaces a Google Sheets spreadsheet.

## Features

- Availability grid — per-person date tracking
- Accommodation management — room and bed assignments per night
- Cost tracking — per bed-night occupied, derived from assignments
- Comfort & consent preferences — organiser-defined questions with sensible defaults
- Activity suggestions & voting
- Itinerary — day-by-day schedule
- Shopping list — collaborative, shared

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Angular (PWA) |
| Backend | .NET 10 Minimal API |
| Database | PostgreSQL |
| Auth | Authentik (self-hosted, Discord OAuth primary) |
| Infrastructure | Docker Compose + Terraform |

## Running Locally

Start the database (from `infra/`):

```bash
cd infra
docker compose up
```

The API can then be run locally with `dotnet run` from `backend/Amsterfam/Amsterfam.Api`.

To run the full containerised stack:

```bash
cd infra
docker compose --profile full up
```

Tear down (including volumes):

```bash
docker compose down -v
```

## Documentation

- [`docs/domain.md`](docs/domain.md) — domain model and entities
- [`docs/api.md`](docs/api.md) — API design and endpoint reference
- [`docs/adr/`](docs/adr/) — architecture decision records
