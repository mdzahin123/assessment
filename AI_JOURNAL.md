# AI Working Journal

This is a running log of how I used an AI assistant (Claude Code, model Claude Opus 5.5) to build this project:
what I asked for, what it suggested, what I agreed with, what I changed, and why.

Each entry follows the same pattern:
**What I asked → What Claude suggested or did → What I decided → Problems found → What was checked → My notes.**

> The "Decision" and "My notes" parts are mine. The one exception is the note in Entry 8: at my request, Claude
> drafted it from decisions I had made in the chat. I also asked Claude to rewrite this journal in plain English
> at the end. The facts didn't change.

**A few words I can't avoid:**
- **Backend / API:** the part of the system that runs on the server and holds all the rules.
- **Frontend:** the screens in the browser (built with Angular).
- **Database:** where claims are stored (PostgreSQL).
- **Kafka:** a message system. When something happens to a claim, a message is posted, and other parts of the
  system react to it later (for example, by sending a notification).
- **Tests:** small programs that check the code does what it should. "82/82 pass" means all of them passed.
- **Commit:** a saved snapshot of the code in git, with a short message.

---

## Entry 1: Planning (before any code)

**What I asked:** Read the brief and make a plan only, with no code yet. I set some rules: explain every decision
in simple words, don't decide anything silently, don't add technology just because it's popular, and never say
something works unless it was actually run and tested.

**What Claude suggested:**
- **One backend program, not many small ones.** I'm one person with a few hours, so splitting the system up would
  only add work. The parts are still kept separate inside, so the notifications part could be split off later.
- **A safe way to send messages.** When a claim changes, a "to send" note is saved in the database at the same
  moment as the change. A background worker then posts it to Kafka. That way a message can't get lost if
  something crashes halfway.
- **Instant answers for users, messages for everything else.** Anything a user waits for (submit, settle and so
  on) gets an immediate yes or no. Things like notifications happen a moment later through Kafka.
- **Strict claim steps.** A claim can only move Submitted → In review ⇄ Awaiting information → Settled or Rejected,
  and only through set actions, never by editing the status directly.
- **A "reserve" separate from the claimed amount.** The reserve is the officer's estimate of what will really be
  paid. Total exposure = all reserves on claims that are still open.
- **A fake login for the demo.** You pick a user from a list. A real login would take about an hour and adds
  nothing to the claims work.
- Claude pointed out itself that one program sending Kafka messages to itself is a bit artificial, and suggested
  explaining it as the place where the system would be split later.
- Keep the assessment brief out of git, because it is marked confidential and carries company branding.

**What I decided:**
- Agreed: one backend program, the safe message sending, the fake login, the strict claim steps, the name
  "ClaimFlow", and keeping the brief out of git.
- **Changed by me:** Claude had listed the workload and exposure dashboards as must-haves. I said the core claim
  features come first, and dashboards and extra styling are the first things to cut if time runs short.

**My notes:**
I didn't know much of this stack yet, so I asked for simple explanations first and told Claude to put core claim features ahead of extras like dashboards if time runs out.

---

## Entry 2: Stage 0, setting up the project

**What I asked:** Explain Docker, ports and settings, and Kafka in plain English. Then set up the empty project,
start the database and Kafka, and stop so I could check.

**What Claude did:**
- Found that I already had a PostgreSQL database on my computer using port 5432. Instead of stopping mine, it put
  the project's database on **port 5433**, so the app can't connect to the wrong one by accident.
- Used fixed versions of the database and Kafka (not "latest"), so anyone running the project gets the same setup.
- Turned off HTTPS for the local demo, to avoid certificate setup.
- Deleted the template's sample code, so nothing unexplained is left in.
- **Checked instead of assuming:**
  - both containers started healthy
  - the database answered a query
  - a test message went through Kafka and back
  - the project built with 0 warnings
  - the API started

**Not checked yet:** our own code talking to Kafka (done in Stage 3).

**Decision:**
Checked that the containers and API actually ran. Good call using port 5433 so it doesn't clash with my existing Postgres on 5432, and skipping HTTPS for local setup. Since the brief is a .docx file, I'm making sure *.docx is ignored in git too.

**My notes:**
Some of the Docker and Kafka settings were new to me, so I asked for plain-English explanations first and verified the setup myself before committing Stage 0.

---

## Entry 3: Stage 1, claim rules and the database

**What I asked:** First, make sure Word files (.docx) can never be added to git. Then explain the main ideas
simply and build the claim rules, the database tables, sample data and tests.

**What Claude did:**
- **The claim rules live in one place.** A claim's status can only change through actions like "pick up",
  "settle" or "reject". Each action checks three things: is the claim in the right status, is this the right
  person, and is the input valid. Only then does it change the claim, and it writes a line on the claim's
  timeline at the same time.
- **Rules Claude proposed for me to confirm:**
  - A claim can only be settled while "In review", not while waiting for the claimant to reply.
  - A claim can be rejected while waiting for a reply (for example, if the claimant never answers).
  - The settlement can't be more than the amount claimed.
  - Any officer can pick up a free claim, but after that only the assigned officer can act on it.
  - Amounts must be above zero with at most 2 decimal places. The incident date must fall inside the policy
    period and not be in the future.
- **Money is stored exactly, times are stored in one world clock (UTC).**
- **Clashes are blocked.** If two officers pick up the same claim at the same moment, the second one is refused.
- **Sample data uses the real rules.** Fictional people, emails ending in example.com, and claims in every status.
- **Things Claude chose not to add, to keep it simple:** extra naming tools, extra code layers, extra project files.

**Problems found (Claude's own mistakes):**
- It installed version 11 of the database tools, which don't work with the .NET version we use. Fixed by pinning
  exact version-10 tools. Lesson: "latest" doesn't mean "compatible".
- The first test run crashed because it started an old copy of the program without the new database changes.
  Rebuilt, then it worked.
- Looking at the sample data showed one timeline in the wrong order: two actions had the exact same timestamp.
  Fixed the sample times.
- My own earlier test run of the app was still open and blocking the build. Claude checked what it was before
  closing it.

**What was checked:**
- The database tables were created as planned, with the correct types for money, dates and statuses.
- **27/27 tests pass.**
- To prove the tests really catch mistakes, Claude briefly broke one rule (allowed settling while waiting for a
  reply). Exactly one test failed. After putting the rule back, all passed.

**Not checked yet:** the "two officers at once" protection with the real database (done in Stage 2).

**Decision:**
Accepted the domain model, starter data, and unit tests after reviewing the four rules.

**My notes:**
I agree with the four rules for this demo, especially being able to reject a claim if the claimant never replies. Good catch fixing the package versions and the mixed-up timestamps in the sample data. Next I want to see the API endpoints and make sure claimants can't view or touch another claimant's claims.

---

## Entry 4: Stage 2, the backend API

**What I asked:** Explain how requests and answers are shaped, what the error codes mean, and how the fake login
works. Then build the API for claimants and officers. **My priority:** a claimant must never see or change
someone else's claim.

**What Claude did:**
- **Login:** the fake login uses the normal .NET login system, so swapping in a real login later replaces only
  one piece.
- **Locked by default:** every API address needs a known user, except the list of demo users.
- **Ownership:**
  - One single check decides who can see a claim.
  - If a claimant asks for someone else's claim, the answer is "not found", not "forbidden", so they can't even
    tell whether the claim exists.
  - The same goes for claiming against someone else's policy.
- **The reserve stays private:** the claimant never sees it, including the "reserve set" line on the timeline.
- **One place turns rule errors into clear messages:**
  - 400: wrong input
  - 403: not allowed
  - 404: not found
  - 409: the claim is in the wrong status, or someone else changed it at the same time
- **Claim numbers** come from the database's own counter, so two people submitting at once never get the same number.
- **A web page for trying the API** (Swagger), with a box to enter a user ID.
- **Tests run the real API against a separate test database**, so they never touch my own data.
- Officers see all claims as one shared team. The work queue shows open claims, oldest first. No pages yet.

**Problems found (Claude's own mistakes):**
- One database query was written in a way that would have broken. Caught when re-reading, before it ran.
- The API's published description said claim statuses were numbers, while the API actually sends words like
  "Submitted". All tests were green; it was only found by reading the live description. Fixed. It would have
  broken the screens later.

**What was checked:**
- **47/47 tests pass.** They cover no or unknown user, wrong role, someone else's claim, missing fields, wrong
  status, the hidden reserve, and a full claim from start to finish.
- **Two officers picking up the same claim at the same moment:** exactly one wins and the other gets a clear
  "someone else just changed this".
- Removing the ownership check made exactly the right 3 tests fail.
- My own data was untouched (the tests used the separate test database).

**Decision:**
Accepted the Stage 2 API design, ownership checks, and the three queue/team/policy decisions for this demo.

**My notes:**
Glad the tests checked that claimants can't see each other's claims or the internal reserve amount, and that returning 404 hides whether another claim ID exists. Good catch fixing the status format in the API spec before building the frontend. Next I want to see how Kafka events and notifications work in plain English before Stage 3 code is written.

---

## Entry 5: Stage 3, messages and notifications

**What I asked:** Explain in plain English how messages can get lost or doubled, how to stop that, how message
order is kept, and how background workers run. Then build it.

**What Claude did:**
- **No lost messages.** Every claim change saves a "to send" note in the same database save. A background worker
  posts the notes to Kafka every second, oldest first, and stops if one fails, so the order is never mixed up.
- **No double notifications.** A message can arrive twice (it's better to repeat one than lose one). Each message
  has its own ID, and the database refuses a second notification for the same ID.
- **Right order per claim.** All messages for one claim go into the same queue, so "received" always comes before
  "settled".
- **Who gets told what:**
  - The claimant is told when the claim is received, being reviewed, needs more information, settled or rejected.
  - The officer is told when the claimant answers.
  - Reserve changes tell nobody.
- The notifications part only learns about claims from Kafka messages, so it could be split off later.
- The browser never talks to Kafka. The screens simply ask for new notifications every few seconds.
- Tests use their own Kafka queue, which is deleted afterwards.

**Problems found:** no code bugs this stage. Claude's own command broke once because of a typo and was re-run.
My earlier test run of the app was blocking the build again, and Claude checked it before closing it.

**What was checked:**
- **61/61 tests pass**, including:
  - the same message 5 times at once → still only 1 notification
  - all 6 messages for one claim arrive in the right order
- Breaking the "no doubles" safety net or the "same queue per claim" rule made the right tests fail.
- **Live test with Kafka switched off:** submitting a claim still worked, and the message waited safely. When Kafka
  came back, the notification arrived about 4 seconds later.

**Decision:**
Accepted the Stage 3 outbox, Kafka consumer, and notifications setup, and agreed to skip Stage 4 (dashboards) for now so we build the Angular frontend first and only come back to dashboards if time allows.

**My notes:**
Seeing the claim still succeed when Kafka was off and then deliver the notification after Kafka restarted made the outbox idea super clear. Skipping dashboards for now to focus on the Angular screens fits my priority plan—we can come back to dashboards if there is time left.

---

## Entry 6: Stage 5, the screens (Angular)

_(There is no Stage 4 entry here: Stage 4 was the dashboards, which I chose to skip in Entry 5 so the screens
could be built first. They were built at the end, in Entry 8.)_

**What I asked:** Explain how Angular screens, page links, the login stamp and the "staff only" doors work, then
build the main screens for both kinds of user and test them in a real browser.

**What Claude did:**
- **Screens:** a demo login picker. Claimants get "My claims", "Report an incident" and a claim page. Officers get
  a "Work queue" and the same claim page with action buttons.
- **The user ID is added to every request automatically.** If the server says "who are you?", the app goes back
  to the login picker.
- **The "staff only" door is just a sign.** Claimants are sent away from staff screens, but the real protection is
  the server, which was already tested.
- **One claim page for both kinds of user.** The buttons shown depend on who you are and the claim's status. The
  server still checks every rule.
- **Amounts in local currency.** Each market's amounts are shown in its own currency (SGD, HKD and so on).
- **Notification bell:** checks every 10 seconds, and straight away when opened.

**Problems found (Claude's own mistakes):**
- **The API description was still incomplete (a mistake from Stage 2).** 12 addresses didn't say what they return.
  In Stage 2, Claude had said "all 12 addresses are described" without checking they were complete. Fixed, and a
  new test now fails if it happens again.
- A hidden character was accidentally added to the start of a settings file. Found and removed.
- One browser check failed the first time because of timing, not a bug. But it showed the bell could be up to 10
  seconds out of date, so opening the bell now refreshes it.
- The README listed the wrong Node.js version, written from memory. Checked against Angular's own requirements and
  corrected.

**What was checked:**
- **67/67 backend tests pass.**
- **The screen types match the backend:** a script compared every field.
- **A real browser (Edge) went through the whole flow: 16/16 checks pass.** This included:
  - a claimant reports a claim
  - an officer picks it up, sets a reserve and asks a question
  - the claimant answers without ever seeing the reserve
  - the officer settles
  - the claimant gets the decision in the bell
  - another claimant opening the link gets "Claim not found"
- The screenshots were looked at by eye.

**Left for later:** frontend test files (by agreement), a phone-friendly layout, instant notifications.

**Decision:**
Accepted the Angular core flow, shared claim detail screen, and keeping frontend test files out of scope; agreed to do the wrap-up stage before considering dashboards.

**My notes:**
Good catch finding the missing response types in the OpenAPI contract and making the notification bell refresh immediately when clicked. Sticking to our plan by keeping dashboards out of the way until core flows and setup docs are solid.

---

## Entry 7: Wrap-up, documents and a clean-install test

**What I asked:** Explain the main trade-offs and limits so I can talk about them. Make the setup instructions
crystal clear, and prove that everything works as written.

**What Claude did:**
- **Wrote `docs/ARCHITECTURE.md`:** the main decisions, what each one costs, answers to the brief's questions,
  and the known limits.
- **Wrote the exposure query into that document**, and ran it on real data first to make sure it worked.
- **Rewrote the README as a step-by-step guide:**
  - what to install
  - how to run it
  - a 5-minute demo
  - how to run the tests
  - a troubleshooting table based on problems we actually hit
- **Tested it like a stranger would, without wiping my data:**
  1. paused my own setup
  2. made a fresh copy of the project with an empty database
  3. followed the README word for word
  4. deleted the copy and restarted my setup

**Problems found (Claude's own mistakes):** two errors in its README draft: one API address was described as
claimant-only (it's for everyone), and it said "3 terminals" when it's 2. Fixed. Also, a new warning appears when
installing the screens' packages. It was checked rather than ignored (the app works fine without doing anything)
and is now explained in the README.

**What was checked on the fresh copy:**
- The assessment brief is not in git.
- Everything started from an empty database.
- **The browser walkthrough: 16/16 pass.**
- **The tests: 67/67 pass.**
- The production build is clean.
- My own data was untouched afterwards.

**Decision:**
Accepted the wrap-up documentation, README fixes, and clean-install verification; keeping dashboards optional depending on remaining time.

**My notes:**
Running a fresh-clone check from the README without wiping my dev data and catching the two README mistakes gives me confidence the setup works cleanly for anyone reviewing it. Having the trade-offs and SQL exposure query written down clearly in ARCHITECTURE.md will make my walkthrough much easier.

---

## Entry 8: Stage 4 (built last), team workload and exposure screens

**What I asked:** A check against the brief found two missing pieces: staff must be able to see their team's
workload and performance, and there should be a live picture of open claims and how much money is at stake.
My rules for this stage:
- Plan first and wait for my OK.
- Build in small steps, testing and saving each one.
- Keep all rules on the backend.
- Never show the reserve to claimants.
- **Never add money from different markets together.**
- No new libraries.

I also asked for two small fixes in this journal.

**What Claude suggested:**
- **A Team screen (officers only):**
  - for each officer: how many open claims they have, how old those claims are on average, and how many they
    settled or rejected in the last 7 days
  - how many claims nobody has picked up yet
- **An Exposure screen (officers only):** for each market, in that market's own currency, broken down by claim type:
  - how many claims are open
  - the total of their reserves
  - how many have no reserve yet (money at stake that hasn't been estimated)
- **No grand total across markets anywhere.** Adding SGD and HKD together would be meaningless.
- **The numbers are worked out fresh** from the database every time the screen is opened or refreshed.
- **"Open" is defined once** and shared by the work queue and both screens, so they can't disagree.
- **No separate Manager role for now.** The brief names two kinds of user, and says staff should see the team
  view. A Manager role would also need to read claims, which means changing the most sensitive security check
  late in the build. This is explained in ARCHITECTURE.md, with the next step: once there's a real login, limit
  exposure to managers.

**What I decided:**
- Approved the plan.
- **Average age is counted from when the claim was submitted**, the same as "age in days" in the work queue.
- **No Manager role for now**, explained in the docs.
- **Build both backend parts, with tests, before either screen**, so the important part was safe if time ran short.
- I would look at the brief's questions in ARCHITECTURE.md at the end, and decide then whether to reword them.

**Why:** both pieces were things the brief asks for. Doing the backend first protected the part that mattered
most. Both screens only *read* data, so nothing in the claim flow, the messages or the security checks changed.

**Problems found (Claude's own mistakes):**
- It added the Exposure link one step too early, before the Exposure page existed. Caught before building, and
  moved to the right step.
- The exposure query in ARCHITECTURE.md didn't exactly match the query that was actually compared (it showed
  "empty" instead of 0 when there are no reserves). Corrected.

**What was checked:**
- **82/82 tests pass.** The new ones check that:
  - claimants are refused on both screens
  - the numbers move correctly when a claim is submitted, picked up and settled
  - a 500 reserve in Australia raises only Australia's total, by exactly 500, while Singapore stays the same
  - settled claims no longer count
  - there is no total across markets
- Breaking the "last 7 days" rule, or counting closed claims, made the right tests fail.
- The Exposure screen's numbers match the hand-written query in ARCHITECTURE.md exactly.
- **A real browser: 24/24 checks pass.** Every number on both screens matches the backend, each market shows only
  its own currency, and claimants can't reach either screen. Setting a reserve and pressing Refresh changed the
  total as expected.

**Left for later:** a Manager role, automatic refresh, exposure trends over time.

**My notes:**
I wanted the two missing pieces from the brief done properly rather than rushed, so I asked for a plan first and the backend before the screens. I chose to count average age from when the claim was submitted so it matches the work queue. I agreed not to add a Manager role this late, because it would mean changing the security check for who can see claims, and I'd rather explain it than risk breaking it. Keeping each market in its own currency was a must for me.
