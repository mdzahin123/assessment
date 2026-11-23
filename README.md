# ClaimFlow

A claims-management platform for motor and property insurance claims (take-home assessment).
Claimants report incidents, track claims, answer information requests, and receive decisions.
Claims officers pick up claims, assess them, and settle or reject them.

> Status: **Stage 1 — domain model, database schema, demo data and status-rule unit tests.**
> No API endpoints yet (Stage 2). This README is updated at the end of each stage to reflect what actually works.

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | Angular (added in a later stage) |
| Backend | ASP.NET Core Web API, .NET 10 (one app — a "modular monolith") |
| Database | PostgreSQL 17 (Docker) |
| Messaging | Apache Kafka 4.1, KRaft mode, single node (Docker) |
| Tests | xUnit |

## Prerequisites

- .NET SDK 10
- Docker Desktop (running)
- Node.js 20+ (for the frontend, later)

## Running locally

```bash
# 1. Start PostgreSQL and Kafka
docker compose up -d
docker compose ps          # both services should show "(healthy)"

# 2. Build and run the API
#    In Development it automatically applies database migrations and loads demo data on first run.
dotnet build backend/ClaimFlow.slnx
dotnet run --project backend/src/ClaimFlow.Api
# API: http://localhost:5006
# OpenAPI contract: http://localhost:5006/openapi/v1.json

# 3. Run tests
dotnet test backend/ClaimFlow.slnx
```

Stop the infrastructure with `docker compose down` (keeps data) or `docker compose down -v` (wipes the database;
demo data is re-created on the next API start).

On the very first start against an empty database, the log shows one `fail: ... FROM "__EFMigrationsHistory"` line.
This is expected: EF Core checks for its migration-tracking table before creating it.

### Database migrations (developers)

The EF Core CLI is pinned as a local tool in `backend/dotnet-tools.json`:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/ClaimFlow.Api --output-dir Infrastructure/Persistence/Migrations
```

## Demo data

Fictional users (fixed IDs, usable as `X-User-Id` once the API exists):

| Name | Role | Id |
|---|---|---|
| Alex Tan | Claimant | `11111111-0000-0000-0000-000000000001` |
| Priya Nair | Claimant | `11111111-0000-0000-0000-000000000002` |
| Sam Lee | Officer | `22222222-0000-0000-0000-000000000001` |
| Jordan Ng | Officer | `22222222-0000-0000-0000-000000000002` |
| Morgan Davis | Officer | `22222222-0000-0000-0000-000000000003` |

Six claims cover every status (Submitted ×2, InReview, AwaitingInfo, Settled, Rejected).

## Claim status rules

```
Submitted ──pick up──► InReview ──request info──► AwaitingInfo ──claimant responds──► InReview
InReview ──settle(amount)──► Settled (final)
InReview / AwaitingInfo ──reject(reason)──► Rejected (final)
```

Enforced in `Domain/Claim.cs`: `Status` has a private setter, so it can only change through these methods,
and each method also writes a timeline (`ClaimHistory`) entry. Settlement cannot exceed the claimed amount;
only the assigned officer can act on a claim.

## Local ports

| Service | Host address | Note |
|---|---|---|
| API | `localhost:5006` | |
| PostgreSQL | `localhost:5433` | 5433, not the default 5432, to avoid clashing with a locally installed PostgreSQL |
| Kafka | `localhost:9092` | |

Database credentials in `docker-compose.yml` are throwaway local-development values.

## Repository layout

```
docker-compose.yml        PostgreSQL + Kafka for local dev
AI_JOURNAL.md             Log of how AI was used, challenged and overridden
backend/
  ClaimFlow.slnx          Solution file
  dotnet-tools.json       Pinned dotnet-ef CLI version
  src/ClaimFlow.Api/
    Domain/               Entities + business rules (no database code)
    Infrastructure/Persistence/  DbContext, migrations, demo-data seeder
  tests/ClaimFlow.Tests/  Unit tests (claim status rules)
frontend/                 Angular app (later stage)
docs/                     Architecture notes (later stage)
```

## Known limitations (by design, for the time box)

- Authentication is a fake user switcher (`X-User-Id` header) — **not secure**, demo only.
- Settlement is capped at the claimed amount but not yet at the policy's coverage limit.
- Database migrations run automatically on startup in Development only.
