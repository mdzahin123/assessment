# ClaimFlow: architecture, decisions and trade-offs

This document explains **what was built, why, what it costs, and what comes next**.
How to run it is in the [README](../README.md). How AI was used is in the [AI journal](../AI_JOURNAL.md).

---

## 1. The system in one picture

```
 Browser (Angular 22)
   │  REST/JSON  (/api/... → dev proxy → :5006)      polls notifications every 10s
   ▼
 ASP.NET Core API (.NET 10) — ONE deployable, three modules
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │  Claims module            Notifications module            Users (demo auth)   │
 │  controller → service →   Kafka consumer → handler →     X-User-Id → identity │
 │  domain (Claim rules)     Notifications table                                 │
 │        │ SaveChanges                ▲                                         │
 │        ▼ (one DB transaction)       │                                         │
 │  claim + history + OUTBOX row       │                                         │
 │        │                            │                                         │
 │  OutboxPublisher (background) ──► Kafka "claim-events" (3 partitions, key = claim ID)
 └──────────────────────────────────────────────────────────────────────────────┘
          │
          ▼
     PostgreSQL 17
```

**Built:** the claims backend (domain rules, REST API, demo auth, ownership rules, concurrency control),
Kafka events through a transactional outbox, claimant and officer notifications, and an Angular UI for the
full claim lifecycle for both roles.
**Not built:** team workload and liability-exposure dashboards (see §5). The data they need is already stored.

---

## 2. Key decisions and their trade-offs

Each decision lists **what** was chosen, **why**, **what it costs**, and **what would change at scale**.

### 2.1 One backend service (modular monolith), not microservices
- **Why:** one developer, a few hours, a local demo. Microservices pay off when separate teams need independent
  deployments or independent scaling. Here they would only add network failure modes, distributed data and
  more deployment work.
- **How the modules stay separate anyway:** Notifications never reads Claims tables. It only learns about
  claims from Kafka events. The Claims module doesn't know Notifications exists.
- **Cost:** one deployable; the modules share a database.
- **Next:** Notifications could become its own service with its own database without changing Claims code.

### 2.2 REST for what users wait on, Kafka for reactions
| Synchronous (REST) | Asynchronous (Kafka) |
|---|---|
| Submit, pick up, reserve, request info, respond, settle, reject: the user needs an immediate yes/no | Telling people what happened (notifications). In a real system also email/SMS, fraud scoring, reporting |
| Reads (lists, detail, queue) | |
| **Claim history** is written in the same transaction. It is part of the claim's truth, not a side effect | |

- **Why:** a slow or broken side effect (e.g. an email provider) must never stop an officer settling a claim.
  New consumers can subscribe to `claim-events` without touching the Claims code.
- **Honest cost:** a monolith publishing to itself through Kafka is somewhat artificial today. It is justified
  as the seam along which the system would split, and it shows how async processing is handled.

### 2.3 Transactional outbox (instead of "save, then publish")
- **Problem (dual write):** saving to PostgreSQL and publishing to Kafka are two systems. A crash between the
  two leaves them disagreeing: a saved claim with no event, or an event for a claim that was never saved.
- **Solution:** `AppDbContext.SaveChanges` turns every new claim-history row into an `OutboxMessages` row in the
  **same transaction**. A background `OutboxPublisher` sends unpublished rows to Kafka every second and ticks them off.
  Hooking into `SaveChanges` means no endpoint can forget to publish.
- **Verified:** with Kafka stopped, the API still returned 201 and the event waited. After Kafka restarted it was
  delivered about 4 seconds later.
- **Cost:** events arrive up to about a second late, and a message can be sent twice (see 2.4). The design
  assumes a single API instance.
- **Next:** with several instances, claim rows with `SELECT … FOR UPDATE SKIP LOCKED`.

### 2.4 At-least-once delivery + idempotent consumer
- A message can be delivered twice: the publisher can crash after Kafka accepted a message but before ticking
  it off, and the consumer commits its offset only *after* saving. We prefer a **duplicate** to a **lost**
  "your claim was settled".
- **Idempotency:** each event has a unique ID (= its history-entry ID). `Notifications.SourceEventId` has a
  **UNIQUE index**, so a re-delivered event cannot create a second notification. This is tested, including
  5 concurrent deliveries.
- **Poison messages:** an unreadable message is logged and skipped so it cannot block the partition.
  **Next:** a dead-letter topic.

### 2.5 Ordering: message key = claim ID
- Kafka guarantees order only within a partition. Keying by claim ID puts all of one claim's events on one
  partition, so "Submitted" is always processed before "Settled". Different claims are still processed in parallel.
- The publisher sends oldest-first and **stops on the first failure** rather than skipping ahead.
- **Verified:** one claim's 6 events landed on one partition, in order (integration test).

### 2.6 Business rules live in the domain (strict state machine)
```
Submitted ──pick up──► InReview ──request info──► AwaitingInfo ──claimant responds──► InReview
InReview ──settle(amount ≤ claimed)──► Settled (final)
InReview / AwaitingInfo ──reject(reason)──► Rejected (final)
```
- `Claim.Status` has a **private setter**. It changes only through methods (`AssignTo`, `Settle`, …) that check
  status → who → input, then change state **and** write a history entry together.
- Errors map centrally to HTTP: rule broken → 400, not allowed → 403, wrong status → 409.
- **Why:** one place for the rules. They are unit-tested without a database (27 tests), and AI-generated or rushed
  endpoint code cannot bypass them.

### 2.7 Concurrency: optimistic, via PostgreSQL `xmin`
- Two officers picking up the same claim at the same moment: the first save wins, the second gets **409**
  ("changed by someone else"). The UI shows the message and reloads.
- **Why optimistic:** clashes are rare, so we don't lock while people read. We only check at save time.
  This uses PostgreSQL's built-in row version (`xmin`), so no extra column is needed.

### 2.8 Security model (demo)
- **Auth is a demo:** the user picks themselves and the `X-User-Id` header is trusted. This is **not secure**.
  It is still implemented as a standard ASP.NET Core authentication handler, so swapping in JWT/OIDC replaces
  one registration while role checks and ownership rules stay unchanged.
- **Locked by default:** a fallback policy requires a known user on every endpoint except the demo user list.
- **Roles:** `[Authorize(Roles = "Officer")]` etc. → 403.
- **Ownership:** one function (`ClaimService.LoadForUserAsync`) makes another claimant's claim return **404, not 403**,
  so claim IDs can't be probed.
- **Data minimisation:** the **reserve** (insurer's internal estimate) is removed from every claimant response
  and timeline, and never triggers a notification.
- The Angular route guard is **UX only**. The API is the lock.

### 2.9 The API contract: backend-owned OpenAPI
- The backend publishes `/openapi/v1.json`, and that is the source of truth.
- Enums travel as strings, and every endpoint documents its success and error types.
- **Contract tests** fail if any endpoint stops documenting a success response. This was added after a real gap was found.
- The Angular types are hand-written, and a script confirmed they match the contract field for field.
  **Next:** generate them from the contract in CI.

### 2.10 Data choices
- **PostgreSQL + EF Core with committed migrations.** The schema is reproducible. Migrations and demo data are
  applied on startup **in Development only**; in production they would be a deliberate deployment step.
- **Money:** `decimal` / `numeric(18,2)`, with at most 2 decimals enforced. **Times:** UTC (`timestamptz`).
  **Enums:** stored as text.
- **Claim numbers** come from a PostgreSQL **sequence**, which is safe under concurrency (gaps are acceptable).
- **Currencies:** each market implies a currency. Amounts are never summed across markets.

### 2.11 Frontend choices
- Angular 22: standalone components, signals (zoneless change detection), reactive forms, lazy-loaded routes,
  strict TypeScript and template checking. No UI library and no state library: the state is small.
- A **dev proxy** forwards `/api` to the API, so no CORS configuration is needed.
- **One claim screen for both roles.** The actions shown depend on role, status and assignment, mirroring the
  server rules. The server still enforces every rule.
- **Notifications are polled** every 10 s, and refreshed when the bell is opened. The browser never talks to
  Kafka. **Next:** push with SignalR.

### 2.12 How time was spent
Backend first, as the brief asks. Every stage was tested before moving on. The **dashboards were the first thing
cut** to protect the end-to-end claim flow; their data is already in place (§3, Q6–Q7).

---

## 3. Answers to the brief's questions

**Q1. Core entities and relationships?**
`User` (claimant or officer) 1─* `Policy` 1─* `Claim` 1─* `ClaimHistory` and 1─* `InformationRequest`.
A claim optionally has an assigned officer (`User`). Supporting tables: `OutboxMessages` (events waiting to be
sent) and `Notifications` (per user, unique per source event).

**Q2. One service or more?** One deployable with module boundaries (§2.1). The Kafka seam means
Notifications (or a future fraud or reporting service) could be split out without changing Claims.

**Q3. Sync vs event-driven?** Anything a user waits on is synchronous REST. Reactions are Kafka events (§2.2).

**Q4. Frontend/backend contract, and who defines it?** The backend owns an OpenAPI document. The frontend
consumes it, with types checked against it and contract tests guarding it (§2.9). Errors use one ProblemDetails shape.

**Q5. Async across both tiers?** Backend: outbox → Kafka → idempotent consumer (§2.3–2.5). Frontend: the UI
shows the synchronous result immediately; asynchronous results (notifications) arrive by polling. The UI expects
eventual consistency, and on a 409 conflict it reloads the real state.

**Q6. What does a claims officer need to manage their workload?** Built: a queue of open claims, **oldest first**,
with tabs for Unassigned / Mine / All open, filters by status and market, the claim's **age in days**, the assigned
officer, the claimed amount and the reserve. Each claim has its full timeline and Q&A, and officers are notified
when a claimant answers. Not built yet: a team view (open claims per officer, average age, settled this week),
which is a simple aggregate query over existing data.

**Q7. What must the system know about outstanding liability exposure?** The **claimed amount is not the exposure**.
The officer sets a **reserve** (the expected payout), and exposure = the sum of reserves on open claims
(`Submitted`, `InReview`, `AwaitingInfo`), **grouped by market/currency** and by type. The data is captured and
the reserve is hidden from claimants; the dashboard query would be:
```sql
SELECT "Market", "Type", count(*) AS open_claims, sum("ReserveAmount") AS reserved,
       count(*) FILTER (WHERE "ReserveAmount" IS NULL) AS not_yet_reserved
FROM "Claims" WHERE "Status" IN ('Submitted','InReview','AwaitingInfo')
GROUP BY "Market", "Type";
```
`not_yet_reserved` matters: unassessed claims are exposure that isn't quantified yet.

---

## 4. How it is tested

| Layer | What | How many |
|---|---|---|
| Domain unit tests | Every status rule, input rule, final states, full journey | 27 |
| Notification unit tests | Who is told what; event JSON round trip | 6 |
| API integration tests | Real API in memory + **real PostgreSQL + real Kafka** (no mocks): auth 401/403, ownership 404, validation 400, wrong status 409, reserve hidden, concurrency (parallel pick-ups and a stale `xmin` save), outbox written with the claim, idempotency, Kafka round trip, partition ordering, OpenAPI completeness | 34 |
| Browser walkthrough | Scripted Edge (Playwright) run of the whole flow for both roles: 16 checks. Kept outside the repo, as agreed | n/a |

Tests use a **separate database and a throwaway Kafka topic per run**, so they never touch dev data.
Several tests were checked for "teeth": each one was shown to **fail** when the rule it protects was deliberately
broken (ownership, reserve hiding, settle-while-waiting, idempotency safety net, message key, OpenAPI completeness).

---

## 5. Known limitations and what I would do next (in priority order)

1. **Real authentication** (OIDC/JWT). The demo header is not secure.
2. **Dashboards:** team workload and liability exposure (§3, Q6–Q7). The data is ready.
3. **Settlement capped at the policy coverage limit** (today it is capped at the claimed amount only).
4. **Document uploads** (photos, reports) for information requests, the most obvious missing claimant feature.
5. **Outbox for several instances** (`FOR UPDATE SKIP LOCKED`) and a **dead-letter topic**.
6. **Push notifications** (SignalR) instead of polling; **generated TypeScript types**; paging for lists.
7. **Access control by market or team** for officers (today everyone sees the shared queue).
8. **Operations:** health checks, structured logging and tracing, CI pipeline, migrations as a deployment step.
9. **Frontend:** automated component tests, a phone-friendly layout, accessibility review.
