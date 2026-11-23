# ClaimFlow

A claims platform for motor and property insurance claims (take-home assessment, sample data only).

- **Claimants** report an incident, track their claim, answer the officer's questions and receive the decision.
- **Claims officers** pick up claims from a shared queue, set a reserve (expected payout), ask questions,
  and settle or reject.

| | |
|---|---|
| Frontend | Angular 22 (standalone components, signals, reactive forms) |
| Backend | ASP.NET Core Web API on .NET 10: one deployable, modular |
| Data / messaging | PostgreSQL 17 · Apache Kafka 4.1 (both in Docker) |
| Tests | xUnit: domain unit tests + integration tests against real PostgreSQL and Kafka |

📐 **Design decisions, trade-offs and limitations:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
🤖 **How AI was used, challenged and overridden:** [AI_JOURNAL.md](AI_JOURNAL.md)

---

## 1. Prerequisites

| Tool | Version | Check with |
|---|---|---|
| Docker Desktop | running | `docker info` |
| .NET SDK | 10.x | `dotnet --version` |
| Node.js | ^22.22.3, ^24.15.0 or >=26 (required by Angular 22) | `node --version` |

Ports used on your machine: **5433** (PostgreSQL), **9092** (Kafka), **5006** (API), **4200** (web app).
PostgreSQL uses 5433, not the usual 5432, so it can't clash with a PostgreSQL you may already have installed.

## 2. Run it (two terminals, ~2 minutes)

From the repository root:

```bash
# 1) Infrastructure: PostgreSQL + Kafka. Wait until both show "(healthy)" (about 15-30 s).
docker compose up -d
docker compose ps

# 2) API (terminal 1). On first start it creates the database schema and loads demo data.
dotnet run --project backend/src/ClaimFlow.Api
#    -> ready when it prints "Now listening on: http://localhost:5006"

# 3) Web app (terminal 2)
cd frontend
npm ci
npm start
#    -> open http://localhost:4200
```

### Try it (5 minutes)

1. Choose **Alex Tan** (claimant) → **Report an incident** → pick a policy, a recent date, a description and an
   amount → **Submit claim**. Within a few seconds **Notifications** shows "We have received your claim…"
   (this one travels through Kafka).
2. **Switch user → Sam Lee** (officer) → **Work queue** → open the claim → **Pick up this claim** →
   save a **reserve** → **Request information**.
3. **Switch user → Alex Tan** → open the claim → answer the question. Note that Alex never sees the reserve.
4. **Switch user → Sam Lee** → **Settle claim**. Back as **Alex**, the decision is in **Notifications**.
5. Optional: as **Priya Nair**, paste Alex's claim URL. You get "Claim not found." (the server hides other people's claims).

API explorer: **http://localhost:5006/swagger**. Click **Authorize** and paste a user ID from §5.
`backend/src/ClaimFlow.Api/ClaimFlow.Api.http` has the same flow as clickable requests.

### Stop / reset

```bash
# Ctrl+C in the API and web app terminals, then:
docker compose down        # stop containers, keep data
docker compose down -v     # stop and DELETE all data (demo data is recreated on the next API start)
```

## 3. Run the tests

Needs the containers from step 1 running. Tests use their **own** database (`claimflow_test`, recreated each run)
and a throwaway Kafka topic, so your dev data is never touched.

```bash
dotnet test backend/ClaimFlow.slnx
# Expected: Passed! - Failed: 0, Passed: 67
```

| Suite | Count | Covers |
|---|---|---|
| `Domain/` | 27 | Claim status rules, input rules, final states |
| `Notifications/` | 6 | Who is notified of what; event JSON |
| `Api/` | 34 | Real HTTP + PostgreSQL + Kafka: auth (401/403), ownership (404), validation (400), wrong status (409), hidden reserve, concurrent pick-up, outbox, duplicate-event handling, Kafka round trip and ordering, OpenAPI completeness |

Frontend build check: `cd frontend && npm run build`.

## 4. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `docker compose up` fails with "port is already allocated" | Something else uses 5433 or 9092. Stop it, or change the left-hand port in `docker-compose.yml` (and the connection string in `backend/src/ClaimFlow.Api/appsettings.Development.json`). |
| API log: `Kafka not reachable yet … retrying` | Kafka is still starting. Wait for `docker compose ps` to show healthy. The API keeps working meanwhile: events wait in the outbox and are sent when Kafka is up. |
| API log on the very first start: `fail: … FROM "__EFMigrationsHistory"` | Expected, once. EF Core checks for its migrations table before creating it. |
| Web app shows "Cannot reach the server. Is the API running?" | Start the API (step 2). The web app forwards `/api` to `localhost:5006`. |
| After `docker compose down -v` the web app jumps back to "Choose a demo user" | Expected: the remembered user no longer exists, so the app asks again. |
| `npm ci` complains about the Node version | Install a Node version from §1. |
| `npm ci` prints `npm warn allow-scripts … 4 packages have install scripts` | Harmless, no action needed. Newer npm holds back install scripts by default; those 4 packages (esbuild, lmdb, msgpackr-extract, @parcel/watcher) ship ready-made Windows/macOS/Linux binaries. Verified: the app runs and builds without approving them. |
| Build error "file is locked by ClaimFlow.Api" | An API instance is still running. Stop it (Ctrl+C) before building or testing. |
| Tests fail with timeouts in `EventsAndNotificationsTests` | Kafka isn't running or healthy (`docker compose ps`). |

## 5. Reference

### Demo users (fictional)

| Name | Role | ID (for `X-User-Id` / Swagger) |
|---|---|---|
| Alex Tan | Claimant | `11111111-0000-0000-0000-000000000001` |
| Priya Nair | Claimant | `11111111-0000-0000-0000-000000000002` |
| Sam Lee | Officer | `22222222-0000-0000-0000-000000000001` |
| Jordan Ng | Officer | `22222222-0000-0000-0000-000000000002` |
| Morgan Davis | Officer | `22222222-0000-0000-0000-000000000003` |

Six demo claims cover every status. The "login" is a **demo**: picking a user just sends their ID in a header.
**It is not secure**, but the server still enforces roles and ownership.

### Claim status rules

```
Submitted ──pick up──► InReview ──request info──► AwaitingInfo ──claimant responds──► InReview
InReview ──settle(amount ≤ claimed)──► Settled (final)
InReview / AwaitingInfo ──reject(reason)──► Rejected (final)
```
Only the assigned officer can act on a claim. Every change is recorded on the claim's timeline.

### API

All endpoints except `GET /api/users` need an `X-User-Id` header. Full contract: `/openapi/v1.json`.

| Method | Path | Who | Purpose |
|---|---|---|---|
| GET | `/api/users` | anyone | Demo users for the login picker |
| GET | `/api/me` | any user | Who am I |
| GET | `/api/me/policies` · `/api/me/claims` | claimant | My policies, my claims (newest first) |
| GET | `/api/me/notifications` · POST `/api/me/notifications/{id}/read` | any user | My latest 50 notifications; mark read |
| POST | `/api/claims` | claimant | Report an incident → 201 + `Location` |
| GET | `/api/claims/{id}` | both | Claim detail + timeline (claimants: own claims only) |
| GET | `/api/claims?assignee=all\|me\|unassigned&status=&market=` | officer | Work queue: open claims, oldest first |
| POST | `/api/claims/{id}/assign` | officer | Pick up |
| PUT | `/api/claims/{id}/reserve` | assigned officer | Set expected payout |
| POST | `/api/claims/{id}/information-requests` | assigned officer | Ask the claimant |
| POST | `/api/claims/{id}/information-requests/{requestId}/response` | claimant | Answer |
| POST | `/api/claims/{id}/settle` · `/reject` | assigned officer | Decide |

Errors are ProblemDetails JSON: **400** invalid input or broken rule · **401** no/unknown user · **403** wrong role or
not the assigned officer · **404** not found *or someone else's claim* · **409** wrong status or concurrent change.

### Kafka events

Every claim change emits an event on topic **`claim-events`** (3 partitions, key = claim ID) via a transactional
outbox. Watch them live:
```bash
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 \
  --topic claim-events --from-beginning --property print.key=true
```

### Repository layout

```
docker-compose.yml           PostgreSQL + Kafka for local development
docs/ARCHITECTURE.md         Decisions, trade-offs, answers to the brief's questions, limitations
AI_JOURNAL.md                AI working journal
backend/
  ClaimFlow.slnx             Solution
  dotnet-tools.json          Pinned EF Core CLI (dotnet tool restore)
  src/ClaimFlow.Api/
    Domain/                  Entities + business rules (no database code)
    Features/Claims/         Endpoints, service, request/response shapes
    Features/Notifications/  Kafka consumer, duplicate-safe handler, rules, endpoint
    Features/Users/          Demo user list, /api/me
    Infrastructure/          Auth (demo), Messaging (outbox, Kafka), Persistence (EF Core, migrations, seed), Web (errors, OpenAPI)
  tests/ClaimFlow.Tests/     Domain/, Notifications/, Api/
frontend/                    Angular app (see frontend/README.md)
```

### Developer notes

- New database migration: `cd backend && dotnet tool restore && dotnet ef migrations add <Name> --project src/ClaimFlow.Api --output-dir Infrastructure/Persistence/Migrations`
- Migrations and demo data are applied automatically on API start **in Development only**.
- Database credentials in `docker-compose.yml` are throwaway local-development values.

## 6. Scope

**Built:** claim lifecycle for both roles, end to end. **Not built (deliberately):** team-workload and
liability-exposure dashboards, real authentication, file uploads. See
[docs/ARCHITECTURE.md §5](docs/ARCHITECTURE.md#5-known-limitations-and-what-i-would-do-next-in-priority-order)
for the full list and what comes next.
