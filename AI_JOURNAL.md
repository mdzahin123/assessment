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