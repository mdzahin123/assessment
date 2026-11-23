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

---

## Entry 5 — Stage 3: transactional outbox, Kafka, notifications

**Asked:** Commit Stage 2 as two commits. Explain dual-write/outbox, at-least-once + idempotency,
message keys + ordering, and BackgroundService in plain English; then build Stage 3 (outbox + publisher,
notification consumer, notifications endpoint, tests), verify end-to-end, update the journal, stop for review.

**AI did / suggested:**
- **Outbox hooked into `AppDbContext.SaveChanges`**: every new `ClaimHistory` row becomes an `OutboxMessages`
  row in the same save → same DB transaction. Chosen over "each endpoint publishes" because no code path can
  forget, and over separate domain-event classes because the timeline already records exactly these facts.
  EventId = history entry ID (stable ID for idempotency). Event has a `SchemaVersion` for future changes.
- **OutboxPublisher** (BackgroundService): every 1s, oldest-first, `Acks=All` + producer idempotence, ticks
  off one message at a time, **stops on the first failure** to keep order. Single-instance assumption documented.
- **NotificationConsumer** (BackgroundService on `Task.Run` because `Consume()` blocks): manual commit only
  after the notification is saved; transient error → re-read same message after 5s; unreadable message → log + skip
  (dead-letter topic named as the real-world next step).
- **Idempotency**: check "seen this event?" + UNIQUE index on `SourceEventId` as the safety net for races.
- **Key = claim ID**, topic created explicitly with 3 partitions (not auto-create); retries while Kafka is down.
- Notification rules: claimant hears about submitted / under review / question / settled / rejected;
  assigned officer hears when the claimant responds; **ReserveSet → no notification** (internal).
- Notifications module never reads Claims tables (only events) → could be split out later; shares the DB for now.
- `GET /api/me/notifications` (latest 50) + `POST …/{id}/read`; frontend will poll (browser never talks to Kafka).
- Tests use a separate DB **and a throwaway Kafka topic + consumer group per run**, deleted afterwards.
- Turned EF SQL logging down to Warning: the publisher polls every second and would flood the console.

**Problems found while verifying:**
- My own PowerShell command had broken quoting once (tooling, not code) — rerun.
- My Stage 2 review `dotnet run` was still running and locking the build; the AI checked the process before stopping it.
- No code bugs surfaced this stage, so the teeth checks below matter more.

**Verified:**
- **61/61 tests pass** (27 domain + 6 notification-rule/JSON unit + 28 integration). Includes: outbox row written
  with the claim and sharing its history ID; failed action writes no outbox row; same event twice → 1 notification;
  same event 5× concurrently → 1 notification (unique index path); users only see/mark their own notifications (404
  otherwise); **real Kafka round trip** submit → notification; **6 events of one claim on a single partition, in order**.
- Teeth checks: removing the unique-violation safety net → concurrent-duplicate test failed; random message key →
  ordering test failed (it could no longer find the claim's events by key). Restored → 61/61.
- **Live, dev app:** Kafka up → notification ~1s after submit. **Kafka stopped → API still returned 201**, outbox row
  waited (attempt 1, "Message timed out"), no notification. **Kafka restarted → delivered ~4s later**, row published
  after 2 attempts. Startup not blocked by the consumer; migration auto-applied; topic created with 3 partitions.

**Not done (by plan):** dashboards (Stage 4, first to cut), frontend (Stage 5).

**Decision:** _(pending my review)_

Accepted the Stage 3 outbox, Kafka consumer, and notifications setup, and agreed to skip Stage 4 (dashboards) for now so we build the Angular frontend first and only come back to dashboards if time allows.

**My notes:**

Seeing the claim still succeed when Kafka was off and then deliver the notification after Kafka restarted made the outbox idea super clear. Skipping dashboards for now to focus on the Angular screens fits my priority plan—we can come back to dashboards if there is time left.

---

## Entry 6 — Stage 5: Angular frontend (core flow)

**Asked:** Commit Stage 3 as two commits. Explain standalone components/routing, services/HttpClient,
interceptors/guards, signals/reactive forms/dev proxy in plain English; then build the core flow (demo user
switcher, claimant report/list/detail, officer queue/actions), test it in the browser, update journal, stop.
(My decision from Entry 5: dashboards skipped for now; frontend first.)

**AI did / suggested:**
- Angular **22.3.0** via the official CLI: standalone components, zoneless, signals for all screen state,
  reactive forms, `@if/@for` templates, lazy-loaded routes. No SSR, no UI library, no NgRx, no generated spec files.
- Explicitly enabled TypeScript `strict` and Angular `strictTemplates` (they weren't written in the generated
  config; stated explicitly rather than relying on defaults).
- **Dev proxy** (`proxy.conf.json`) forwards `/api` → `localhost:5006`, so no CORS change to the backend.
- `ClaimsApi` = only place with URLs; `Session` (demo user in localStorage, wrapped in try/catch);
  functional **interceptor** adds `X-User-Id` and on 401 returns to the user picker; functional **role guard**
  (documented as UX only — the API is the real lock).
- One **claim detail screen shared by both roles**; actions shown depend on role, status and assignment,
  mirroring server rules. On 409 (e.g. colleague picked it up first) it shows the message and reloads.
- Amounts shown in each market's currency (SGD, HKD…); reserve only shown to officers (API also hides it).
- Notification bell polls every 10s (browser never talks to Kafka); SignalR named as the next step.

**Problems found while verifying (AI's own mistakes / gaps):**
- **Backend contract bug from Stage 2:** a script comparing the hand-written TS types with the OpenAPI contract
  found `PolicyDto`, `ClaimSummaryDto`, `NotificationDto` missing. Cause: declaring error responses on a
  controller stops .NET inferring the success type → **12 endpoints had no documented success response**.
  In Stage 2 the AI reported "12 endpoints in the contract" without checking they were complete.
  Fixed with explicit `[ProducesResponseType<T>(200)]`, and added `OpenApiContractTests` (fail on the old
  code: verified by stashing the fix → 4 failures listing all 12 endpoints; pass with it).
- PowerShell rewrote `angular.json` with a hidden UTF-8 BOM (Windows PowerShell 5.1 quirk) — detected and removed.
- The browser test's first run failed one check: the bell already had older unread notifications, so the test
  opened it before the new one was polled. Test timing, not an app bug — but it showed a real UX gap, so
  **opening the bell now refreshes immediately**. Test changed to wait for the specific message.
- README Node version was written from memory (20.19+); checking Angular's package showed 22 **dropped Node 20**
  (needs ^22.22.3 / ^24.15.0 / >=26). Corrected.

**Verified:**
- Contract check script: all 7 TS interfaces match their DTOs field-for-field, all 6 enums value-for-value;
  dev proxy forwards `:4200/api` to the API.
- **Backend: 67/67 tests pass** (61 + 6 new contract tests).
- **Scripted browser walkthrough in real Microsoft Edge (Playwright): 16/16 checks pass** — redirect to picker;
  claimant list; guard blocks `/staff/queue` for a claimant; 4 client-side field messages on empty submit;
  server rule message shown ("Incident date is outside the policy period."); submit → detail (Submitted);
  "received" notification arrives via Kafka; officer sees it in Unassigned; pick up → reserve → question;
  claimant does not see the reserve; claimant answers → In review; officer settles; claimant gets the decision;
  Priya opening Alex's claim URL sees "Claim not found." (server 404); stale pick-up shows the 409 message and
  refreshes to "Assigned to Sam Lee". Only browser console errors were the 3 deliberate 400/404/409 responses.
- Screenshots reviewed (queue, officer detail while awaiting info, report-form validation): layout correct.
- Production build: 297 kB initial (82 kB transferred), no budget warnings.

**Not done:** dashboards (postponed), automated frontend tests (scripted walkthrough instead, kept outside the repo),
phone layout tuning, generated API types, push notifications.

**Decision:** _(pending my review)_
Accepted the Angular core flow, shared claim detail screen, and keeping frontend test files out of scope; agreed to do the wrap-up stage before considering dashboards.

**My notes:**
Good catch finding the missing response types in the OpenAPI contract and making the notification bell refresh immediately when clicked. Sticking to our plan by keeping dashboards out of the way until core flows and setup docs are solid.

---

## Entry 7 — Wrap-up: architecture doc, README, clean-room verification

**Asked:** Commit Stage 5 as two commits. Explain the key trade-offs and limitations in plain English for the
walkthrough; make setup/run/test docs crystal clear; run a clean verification (builds, tests, startup) proving
everything works as documented; update the journal; stop. Dashboards only afterwards, if time allows.

**AI did / suggested:**
- New `docs/ARCHITECTURE.md` for the panel: system picture; 12 decisions each with why / cost / next; direct
  answers to the brief's 7 questions; testing strategy; prioritised limitations and next steps.
- For exposure (brief Q7), wrote the actual dashboard SQL into the doc (sum of reserves on open claims per
  market/type, plus a count of claims with no reserve yet) — and **ran it against the dev DB** before claiming it works.
- README rewritten from "stage log" into a run guide: prerequisites with check commands, copy-paste run steps,
  a 5-minute guided demo, test commands with expected output, a troubleshooting table built from problems we
  actually hit, then reference material. Uses `npm ci` (exact lockfile versions) instead of `npm install`.
- **Clean-room check without destroying my dev data:** paused my containers (`docker compose stop`, data kept),
  `git clone` into a temp folder, started *new empty* containers under a separate Docker project name
  (`-p claimflowfresh`), followed the README literally, then deleted those containers + volume and restarted mine.
  (Considered `docker compose down -v` on my own containers — rejected because it would wipe my data.)

**Problems found while verifying:**
- README errors in the AI's own first draft, caught on re-read: `/api/me` listed as claimant-only (it's any user);
  "3 terminals" (it's 2). Fixed.
- `npm ci` on the fresh clone printed `npm warn allow-scripts` (newer npm holds back install scripts of esbuild,
  lmdb, msgpackr-extract, @parcel/watcher). Investigated instead of ignoring: all four ship prebuilt binaries;
  the app served and built without approving them. Documented in Troubleshooting; did not approve scripts that
  aren't needed.

**Verified (fresh clone of commit `63d61a4`, empty database):**
- 87 tracked files; **no PDF/DOCX tracked** (assessment brief stays out of git).
- Containers healthy in ~7 s; empty DB → API applied all 3 migrations, seeded demo data, created the Kafka topic
  (3 partitions), published 16 demo events; only the documented first-run `fail` line appeared.
- `npm ci` 9 s; web app reachable via the dev proxy; page title "ClaimFlow".
- **Edge browser walkthrough: 16/16 checks pass** on the clean install.
- **`dotnet test`: 67/67 pass**; frontend production build clean (297 kB / 82 kB transferred).
- Clean-up: fresh containers and volume removed; my containers restarted healthy with data intact
  (10 claims, highest CLM-001005); no stray processes on ports 5006/4200.

**Not done:** dashboards (next, if time allows — the SQL is already written and tested).

**Decision:** _(pending my review)_

Accepted the wrap-up documentation, README fixes, and clean-install verification; keeping dashboards optional depending on remaining time.

**My notes:**

Running a fresh-clone check from the README without wiping my dev data and catching the two README mistakes gives me confidence the setup works cleanly for anyone reviewing it. Having the trade-offs and SQL exposure query written down clearly in ARCHITECTURE.md will make my walkthrough much easier.