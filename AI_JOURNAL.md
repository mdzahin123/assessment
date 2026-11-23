# AI Working Journal

Running log of how AI (Claude Code, Claude Opus 5.5) was used on this assessment: what was asked,
what it suggested, what was accepted, challenged, or overridden, and why. Not polished on purpose.

Format per entry: **Asked → AI suggested → Decision (accepted / challenged / overridden) → Why.**

> Note to self: the AI drafts entries for its own suggestions. My own challenges and overrides
> go under "My notes" in my own words — the AI must not invent them.

---

## Entry 1 — Planning (before any code)

**Asked:** Read the brief and produce a plan only (no code): MVP, architecture, data model, API,
REST vs Kafka split, Angular pages, out-of-scope list, staged timeline, concepts to learn, risks.
I set ground rules: explain every decision, no silent decisions, no tech for its own sake,
don't claim anything works unless it was run and tested.

**AI suggested:**
- One ASP.NET Core app (modular monolith) instead of microservices: one developer, a 3-hour box and a
  local demo don't justify independent deployments; modules kept separate so Notifications could be
  split out later.
- Transactional outbox: save the event in a DB table in the same transaction as the claim change,
  then a background job publishes to Kafka. Avoids the "saved to DB but never sent to Kafka" bug.
- Kafka only for side effects (notifications); all user actions are synchronous REST because the
  user needs an immediate yes/no. Claim history written synchronously (it is core data, not a side effect).
- Strict claim state machine: Submitted → InReview ⇄ AwaitingInfo → Settled | Rejected,
  changed only via domain methods, never by setting Status directly.
- Reserve amount (officer's estimate) separate from claimed amount; exposure = sum of reserves on open claims.
- Fake login via a seeded user switcher + `X-User-Id` header (real auth ~1h for no domain value).
- Dashboards computed by SQL on demand, not via Kafka-fed read models (over-engineering at this scale).
- AI itself flagged that a monolith producing and consuming its own Kafka events is somewhat artificial,
  and proposed defending it as the future service seam.
- Recommended not committing the brief PDF (marked confidential, has third-party branding).

**Decision:**
- Accepted: modular monolith, outbox, fake user switcher, strict status steps, name "ClaimFlow",
  PDF excluded from git.
- Changed priority (mine): the AI listed workload/exposure dashboards as backend must-haves. I said
  core claim features come first and dashboards / extra styling are the first things cut if time runs short.

**My notes:** _(fill in: anything I questioned or would push back on)_
I didn't know much of this stack yet, so I asked for simple explanations first and told Claude to put core claim features ahead of extras like dashboards if time runs out.
---

## Entry 2 — Stage 0: scaffold

**Asked:** Explain Docker / ports & env vars / Kafka concepts in plain English, then do Stage 0 only:
git, .gitignore, docker-compose (Postgres + Kafka), empty .NET solution, README, this journal,
start containers, stop for review.

**AI did / suggested:**
- Found my local PostgreSQL already listening on port 5432. Instead of stopping it, mapped the
  container to host port **5433** so the app can't accidentally connect to the wrong database.
- Chose the official `apache/kafka:4.1.0` image in KRaft mode (no ZooKeeper — one fewer container).
  Set `KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092` because the API runs on the host,
  not inside Docker (the most common Kafka-in-Docker connection bug).
- Pinned image versions (`postgres:17`, `apache/kafka:4.1.0`) instead of `latest` so the panel gets
  the same versions.
- Created the API with `--no-https` to avoid dev-certificate setup on a local-only demo.
- Deleted the template's WeatherForecast sample and placeholder test so nothing unexplained ships.
- Used the standard `dotnet new gitignore`, plus rules for `assessment/`, `*.pdf`, `.env`, Angular files.
- Verified rather than assumed: both containers healthy, `select version()` on Postgres, a Kafka
  produce→consume round trip on a throwaway topic (then deleted), host ports 5433/9092 reachable,
  solution builds with 0 warnings, API serves `/openapi/v1.json` (HTTP 200, empty paths).

**Not yet verified:** the .NET Kafka client connecting from the host (will be tested in Stage 3).

**Decision:** _(pending my review)_
Checked that the containers and API actually ran. Good call using port 5433 so it doesn't clash with my existing Postgres on 5432, and skipping HTTPS for local setup. Since the brief is a .docx file, I'm making sure *.docx is ignored in git too.
**My notes:**
Some of the Docker and Kafka settings were new to me, so I asked for plain-English explanations first and verified the setup myself before committing Stage 0.

---

## Entry 3 — Stage 1: domain model, database, status rules, tests

**Asked:** Before committing Stage 0, add `*.docx` to .gitignore (the brief is a .docx). Then explain
entities vs DTOs, EF Core/migrations, decimal + UTC, state-machine methods and optimistic concurrency
in plain English; then build Stage 1 only (entities, strict status rules, migration, seed data,
unit tests), run the tests, and stop for review.

**AI did / suggested:**
- Domain classes with **private setters**; status changes only through `Submit / AssignTo / SetReserve /
  RequestInformation / ProvideInformation / Settle / Reject`. Each checks status → who → input, then
  changes state and writes a `ClaimHistory` entry together. Three exception types (rule broken /
  wrong status / not allowed) so Stage 2 can map them to 400 / 409 / 403 without repeating rules.
- Business rules it proposed (I should confirm these are right):
  - Settle only from InReview (not while waiting for the claimant); Reject allowed from InReview or AwaitingInfo
    (e.g. claimant never replies).
  - Settlement ≤ claimed amount. Coverage-limit cap NOT enforced yet (listed as a known limitation).
  - Only the assigned officer can act; any officer can pick up an unassigned claim.
  - Amounts must be > 0 with max 2 decimal places; incident date within policy period, not in the future.
- `Guid.CreateVersion7()` IDs (time-ordered, friendlier to database indexes than random GUIDs).
- Enums stored as text, money as `numeric(18,2)`, timestamps as `timestamptz` (UTC), set once in
  `ConfigureConventions` instead of per property.
- Concurrency via PostgreSQL's hidden `xmin` column (`IsRowVersion()` on a `uint Version`).
- `ValueGeneratedNever()` on all IDs: otherwise EF may treat a new history row with a pre-set ID, added
  to an already-saved claim, as an existing row and try to UPDATE it instead of INSERT.
- Seeder builds demo claims through the real domain methods (so seed data obeys the same rules),
  runs only on an empty DB, fixed user IDs for API testing, fictional people and `example.com` emails.
- Auto-migrate + seed on startup in Development only.
- Rejected by AI (to keep it simple): snake_case naming package, a separate Domain project, a
  repository layer over EF Core, a per-claim "line number" column on history.

**Problems found while verifying (the AI's own mistakes):**
- `dotnet add package` picked EF Core **11.0.0**, which requires .NET 11 → restore failed. Also
  `dotnet-ef` 11 got installed. Fixed by pinning exact 10.x versions (Npgsql EF 10.0.4, EF Design
  10.0.15, dotnet-ef 10.0.15). Lesson: "latest" ≠ compatible.
- First API run crashed ("relation Users does not exist") because the AI ran with `--no-build` after
  adding the migration, so the running program didn't contain it. Rebuilt → migration applied.
- Inspecting the seeded data in psql showed the timeline listing "ReserveSet" before "Assigned":
  the seeder gave both the same timestamp. Fixed the seed times; did not add a sequence column
  (ties can't happen in real use, since each action is a separate request).
- A stale `dotnet run` from my Stage 0 check was locking the build output; AI checked what it was
  before stopping it.

**Verified:**
- Build: 0 warnings, 0 errors. Migration applied to Postgres; 5 tables; column types checked in psql;
  `xmin` readable and no extra column created. Seed: 5 users, 4 policies, 6 claims (all statuses), 1 info request.
- **27/27 unit tests pass.** Sanity check that the tests have teeth: temporarily allowed Settle while
  AwaitingInfo → exactly 1 test failed (`Settle_while_waiting_for_the_claimant_fails`); reverted → 27/27.

**Not yet verified (needs Stage 2 endpoints + real DB):**
- Optimistic concurrency actually returning a conflict when two officers pick up the same claim.
- Adding history/info-request rows to an already-saved claim inserts correctly (the `ValueGeneratedNever` concern).

**Decision:** _(pending my review)_
Accepted the domain model, starter data, and unit tests after reviewing the four rules.
**My notes:**
I agree with the four rules for this demo, especially being able to reject a claim if the claimant never replies. Good catch fixing the package versions and the mixed-up timestamps in the sample data. Next I want to see the API endpoints and make sure claimants can't view or touch another claimant's claims.

---

## Entry 4 — Stage 2: REST API, auth, errors, OpenAPI, integration tests

**Asked:** Commit Stage 1 as two commits. Explain DTOs, HTTP status codes, and how X-User-Id + ownership
checks work, then build Stage 2 (claimant + officer endpoints, input checks, error responses,
Swagger/OpenAPI, X-User-Id lookup), prove it works, update the journal, stop for review.
My stated priority: claimants must not be able to view or touch another claimant's claims.

**AI did / suggested:**
- Demo auth as a standard ASP.NET Core **authentication handler** (reads X-User-Id, looks up the user,
  attaches ID + role). Swapping to real JWT later replaces only this registration; `[Authorize(Roles=...)]`
  and the ownership check stay. **Fallback policy = locked by default**: only `GET /api/users` is public.
- Ownership enforced in ONE function (`ClaimService.LoadForUserAsync`) used by every read and action.
  Someone else's claim → **404 not 403**, so a claimant can't discover which claim IDs exist.
  Same for submitting against someone else's policy → "Policy not found."
- Role-shaped responses: the **reserve is hidden from claimants** (amount and the "Reserve set" timeline line).
  Concrete example of why DTOs exist: sending the DB entity would have leaked the insurer's estimate.
- One central exception → HTTP mapping (`ApiExceptionHandler`): 400 rule broken / 403 not allowed /
  404 not found / 409 wrong status or concurrent update. Controllers are one-liners; all errors are ProblemDetails JSON.
- Validation split: request DTO attributes check shape (required, max length — lengths reuse the domain
  constants); the domain checks business rules. No FluentValidation (not needed).
- Claim numbers from a PostgreSQL **sequence** starting at 1000 (no collisions with seed data, safe when
  two claimants submit at once; gaps are acceptable).
- `TimeProvider` injected as the clock; Swagger UI (`/swagger`) with an "Authorize" box for X-User-Id.
- Integration tests with `WebApplicationFactory` against a **separate `claimflow_test` database**
  (wiped each run) instead of mocks: proves real routing, auth, SQL, and concurrency.
- Officers see all claims (single team); queue defaults to open claims, oldest first; no paging yet.

**Problems found while verifying (AI's own mistakes):**
- Wrote `SqlQuery($"...nextval('{name}')")` — EF turns `{...}` into a SQL parameter, which can't go inside a
  quoted name. Caught on review before running; switched to `SqlQueryRaw` with our own constant (no user input).
- The OpenAPI contract described enums as **integers** while the API sends **strings** — .NET 10's OpenAPI
  generator reads a different JSON settings object than controllers. Only found by inspecting the live
  `/openapi/v1.json`; all tests were green. Fixed by configuring both from one function; contract now lists
  the allowed string values. (Would have broken generated Angular types later.)

**Verified:**
- **47/47 tests pass** (27 domain + 20 API). The API tests cover 401 (no/unknown user), 403 (wrong role,
  not assigned officer), 404 (other claimant's claim, unknown claim), 400 (missing fields listed per field,
  business rule message), 409 (settle a rejected claim), reserve hidden from claimants, full journey,
  queue filtering/order.
- Concurrency now proven: two simultaneous pick-ups → exactly one 200 + one 409 and a single "Assigned" entry;
  plus a deterministic test where both officers loaded the claim before either saved → second save rejected by
  the `xmin` version check (DbUpdateConcurrencyException → 409).
- Stage 1 open question closed: new history/info-request rows on an existing claim are INSERTed correctly
  (full-journey test re-reads from the DB).
- Tests have teeth: removing the ownership check + reserve hiding made exactly the 3 related tests fail; restored → 47/47.
  (With the ownership check removed, Priya still couldn't answer Alex's question — the Stage 1 domain rule returned
  403 — so the layers back each other up; the test failed because 403 reveals the claim exists.)
- Dev database untouched by tests (still 6 claims); test DB had 13 new claims numbered from CLM-001000.
- Live dev API: Swagger UI 200, 12 endpoints in the contract, X-User-Id scheme present, 401/200/404 behave as above,
  new migration auto-applied.

**Not done (by plan):** Kafka events + notifications (Stage 3), dashboards (Stage 4, first to cut), frontend.

**Decision:** _(pending my review)_
Accepted the Stage 2 API design, ownership checks, and the three queue/team/policy decisions for this demo.
**My notes:**
Glad the tests checked that claimants can't see each other's claims or the internal reserve amount, and that returning 404 hides whether another claim ID exists. Good catch fixing the status format in the API spec before building the frontend. Next I want to see how Kafka events and notifications work in plain English before Stage 3 code is written.