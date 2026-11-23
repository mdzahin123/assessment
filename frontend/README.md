# ClaimFlow frontend (Angular 22)

See the repository root `README.md` for how to run the whole platform.

```bash
npm install
npm start            # http://localhost:4200  (the .NET API must be running on :5006)
npm run build        # production build into dist/
```

`/api/*` calls are forwarded to `http://localhost:5006` by the dev server (`proxy.conf.json`), so no CORS setup is needed.

Structure (`src/app`):

| Folder | What |
|---|---|
| `core/` | API types, `ClaimsApi` (all HTTP calls), `Session` (demo user), interceptor, role guard, notifications polling |
| `login/` | Demo user picker |
| `claimant/` | My claims, report an incident |
| `staff/` | Officer work queue, Team workload, Liability exposure |
| `claims/` | Claim detail (shared by both roles; actions depend on role, status and assignment) |
| `shared/` | Status badge |
