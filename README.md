# ClaimFlow

A claims-management platform for motor and property insurance claims (take-home assessment).
Claimants report incidents, track claims, answer information requests, and receive decisions.
Claims officers pick up claims, assess them, and settle or reject them.

> Status: **Stage 0 — project scaffold only.** No business features are implemented yet.
> This README is updated at the end of each stage to reflect what actually works.

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
dotnet build backend/ClaimFlow.slnx
dotnet run --project backend/src/ClaimFlow.Api
# API: http://localhost:5006
# OpenAPI contract: http://localhost:5006/openapi/v1.json

# 3. Run tests
dotnet test backend/ClaimFlow.slnx
```

Stop the infrastructure with `docker compose down` (keeps data) or `docker compose down -v` (wipes the database).

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
  src/ClaimFlow.Api/      Web API (single deployable)
  tests/ClaimFlow.Tests/  Unit tests
frontend/                 Angular app (later stage)
docs/                     Architecture notes (later stage)
```

## Known limitations (by design, for the time box)

- Authentication is a fake user switcher (`X-User-Id` header) — **not secure**, demo only.
