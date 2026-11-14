# Phase 1: Run Lefteria FC OS in this workspace

Version: phase-1

Goal: run the existing repo on its own stack (SvelteKit, .NET 10, PostgreSQL) on the preview URL with the repo's made-up demo data. Mobile test logins and the admin owner setup must be available. Product behaviour must not change. Context and decisions are in [../overview.md](../overview.md).

## Functional requirements

### Runtime and startup
- REQ-1 [always] The system shall run the API on the .NET SDK version pinned in `/app/global.json`.
- REQ-2 [always] The system shall store its data in PostgreSQL 18, in a data directory under `/app` that is ignored by git. (interpreted)
- REQ-3 [event] When the workspace backend service starts, the system shall start PostgreSQL and the .NET API host without manual steps.
- REQ-4 [unwanted] If the .NET SDK or PostgreSQL binaries are missing after a workspace restart, then the system shall reinstall them automatically before starting the API. (interpreted)
- REQ-5 [event] When the workspace frontend service runs `yarn start`, the system shall serve the SvelteKit web on port 3000.
- REQ-6 [always] The system shall forward every `/api/*` request on port 8001 to the .NET API host with method, path, query, body, status code, cookies and `Set-Cookie` preserved.
- REQ-7 [unwanted] If the .NET API host is not ready, then the system shall answer `/api/*` requests with HTTP 503.

### Database
- REQ-8 [event] When the database is empty, the system shall apply every migration in `backend/src/LefteriaFc.Infrastructure/Persistence/Migrations` using the repo's `--migrate` path.
- REQ-9 [always] The system shall run the API with the restricted runtime database role, provisioned by the repo's `ops/postgres` scripts.
- REQ-10 [always] The system shall run the web-admin credential reads with the separate web-admin auth role from `ops/postgres/create-web-admin-auth-role.sql`.
- REQ-11 [event] When the backend service restarts, the system shall keep every database row written before the restart. (interpreted)

### API surface
- REQ-12 [always] The system shall return HTTP 200 from `/api/health/live`.
- REQ-13 [always] The system shall return HTTP 200 from `/api/health/ready` once migrations and seeding are complete.
- REQ-14 [always] The system shall serve the committed `contracts/openapi.json` at `/api/openapi.json`.

### Demo data
- REQ-15 [event] When the database is first created, the system shall seed the repo's existing made-up fixtures: canonical roster, staff relationships, accounts, the Oct-2026 calendar and the public club profiles.
- REQ-16 [unwanted] If the demo data already exists, then the system shall leave it unchanged on restart.
- REQ-17 [always] The system shall contain no production data.

### Mobile test logins
- REQ-18 [event] When a demo phone requests a code at `/app/login`, the system shall accept code `123456` for that phone.
- REQ-19 [unwanted] If a phone that is not a demo phone requests a code, then the system shall return the app's existing safe failure without sending any SMS.
- REQ-20 [unwanted] If a wrong code is entered, then the system shall show the app's existing safe error and keep the user signed out.
- REQ-21 [always] The system shall keep the fixed-code behaviour inside the repo's preview test tooling (under `backend/tools/`), with no change to code under `backend/src/`. (interpreted)
- REQ-22 [always] The system shall keep the demo phone numbers the same across restarts. (interpreted)

### Admin owner setup
- REQ-23 [always] The system shall show the owner setup option at `/admin/login` until the first admin account exists.
- REQ-24 [event] When the user submits the correct setup code with a password of 12 or more characters, the system shall create a super-admin with grants for both `lefteria_fc` and `lefteria_fc_academy`.
- REQ-25 [unwanted] If a wrong setup code is submitted, then the system shall reject the setup without creating an account.
- REQ-26 [always] The system shall keep the setup code in a git-ignored env file only.
- REQ-27 [unwanted] If demo seeding would create an admin record that closes founding setup, then the system shall defer that part of the seed until after the owner account exists. (interpreted)

### Outside services
- REQ-28 [always] The system shall run with Twilio, Payabl, Web Push, OpenAI drafts and the legacy mobile bridge disabled.
- REQ-29 [unwanted] If a disabled provider feature is used, then the system shall show the app's existing fail-closed or unavailable state.

### Developer checks
- REQ-30 [event] When phase 1 is handed back, the system shall have the repo's documented developer checks run, with each pass or fail reported.

## Quality requirements

```
Q-1  Tag: Availability.ColdStart
     Scale: seconds from backend service start to /api/health/ready = 200, with the toolchain already installed
     Meter: builder timing over 3 restarts
     Must: 180
     Plan: 60 (estimate)
```
```
Q-2  Tag: Availability.RestartRecovery
     Scale: count of manual steps needed after `supervisorctl restart backend frontend` before the app works
     Meter: builder restart trial, 2 runs
     Must: 0
     Plan: 0
```
```
Q-3  Tag: Data.Durability
     Scale: % of demo and user-created rows still present after a backend restart
     Meter: row counts before and after restart, 1 trial
     Must: 100
     Plan: 100
```
```
Q-4  Tag: Performance.PageLoad
     Scale: ms until public homepage HTML is rendered on the preview URL
     Meter: tester browser, 3 loads, median
     Must: 4000
     Plan: 1500 (estimate)
```
```
Q-5  Tag: Security.SecretExposure
     Scale: count of secrets (setup code, DB passwords, signing keys) in git-tracked files
     Meter: git grep and git status review by builder
     Must: 0
     Plan: 0
```
```
Q-6  Tag: Security.NoProductBypass
     Scale: count of fixed-OTP or SMS-bypass code paths under backend/src
     Meter: git diff of backend/src against origin/main
     Must: 0
     Plan: 0
```
```
Q-7  Tag: Privacy.Scope
     Scale: count of FC/Academy or parent-child scope leaks seen in demo roles (for example a parent seeing a non-linked child)
     Meter: tester mobile journeys
     Must: 0
     Plan: 0
```
```
Q-8  Tag: Fidelity.NoUIChange
     Scale: count of files changed under frontend/src
     Meter: git diff against origin/main
     Must: 0, unless needed only to run here, with each change reported
     Plan: 0
```
