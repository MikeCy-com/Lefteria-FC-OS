# Lefteria FC OS — Rewrite

## Status

This repository is the parallel rewrite target, using SvelteKit/TypeScript, ASP.NET Core/.NET 10 and PostgreSQL. A separate protected Hostinger preview is running with verified copies of source data. The original live application and its data remain authoritative while the migration proceeds; `lefteriafc.cy` has not been switched to the rewrite.

- Implemented slices: mobile identity, own profile settings, sanitized avatar uploads, canonical roster/staff relationships, protected initial account import with quarantine, current role bindings, an authenticated favourite-player picker, a disabled-by-default legacy session exchange, and a private scoped calendar with an explicit initial import and parent/player RSVP.
- Current public/admin source slice: Greek homepage, news list/article, first-team and Academy overviews/fixtures, club/contact pages, protected public-content import, and a separate cookie-based web-admin login/session/workspace with current roles, capabilities and organization grants. This new slice is not yet deployed; its source and verification status are recorded in the [2026-11-01 checkpoint](docs/implementation/verification-2026-11-01.md). CMS business edits and other operational modules remain queued.
- Source application: [MikeCy-com/Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)
- Audit baseline: GitHub main at 5111f06af5d8b17f855ceed9637caca02db117e2, observed 2026-10-25
- Source main rechecked on 2026-10-26: `7833daceceac1611c368e7bc4df2a82c64b45e1a` merges frontend PR #42. Its 15-file delta changes no backend contracts; the avatar handler's GitHub blob is unchanged. New dashboard layouts are now part of the source baseline.
- The protected backup import baseline is `a52d66b8e01c010cfe95ce53fa139e126bae4d84`. The public/admin contracts were inspected against later original GitHub main `380b4022f0ab0db95fbc52a8613711160a5037a5` (PR #46). Repository source and backup provenance are distinct; a GitHub revision alone does not establish deployed data compatibility.
- The separate `lefteria-os-preview` project, TLS/access gate, database/runtime permissions, source backup/restore, and roster/staff/account/calendar manifest import plus no-op replay were verified. The [preview record](docs/implementation/isolated-preview.md) identifies the deployed application images and observed production guards. Copied source records remain isolated; the live database was not replaced by these imports.
- The copied calendar retains two quarantined RSVP rows and readiness remains false. Account quarantine is retained. The verified media restore stays private. Twilio setup is explicitly paused: real SMS login is unavailable and administrator accounts requiring SMS 2FA fail closed. No provider bypass is deployed.
- Own profile details, team/player preferences, notification preferences and a public mobile avatar have implemented edit paths. The player picker requires the complete roster import; calendar access requires a clean complete calendar import bound to that roster. Operational dashboards, private roster browsing, physical attendance, notification delivery and social/media workflows remain queued.

## Architecture documents

- [Current state](docs/architecture/current-state.md)
- [Target architecture](docs/architecture/target-architecture.md)
- [Folder structure and ownership](docs/architecture/folder-structure.md)
- [Technology evaluation](docs/architecture/technology-evaluation.md)
- [Migration roadmap](docs/architecture/migration-roadmap.md)
- [Risk register](docs/architecture/risk-register.md)
- [Architecture decisions](docs/architecture/architecture-decisions/)
- [Accepted feature queue](docs/implementation/feature-queue.md)
- [Mobile identity contract](docs/implementation/mobile-identity-contract.md)
- [Observed verification](docs/implementation/verification-2026-10-25.md)
- [Mobile identity implementation](docs/implementation/mobile-identity-implementation.md)
- [PostgreSQL runtime permissions](docs/implementation/postgres-runtime-role.md)
- [Profile contract and implementation](docs/implementation/profile-contract.md)
- [Mobile avatar implementation and recovery](docs/implementation/mobile-avatar-implementation.md)
- [Authenticated browser verification](docs/implementation/browser-verification.md)
- [Legacy identity and session migration contract](docs/implementation/legacy-identity-migration.md)
- [Role dashboard source contract](docs/implementation/role-dashboard-contract.md)
- [Roster source contract](docs/implementation/roster-source-contract.md)
- [Canonical roster, explicit import and recovery](docs/implementation/canonical-roster.md)
- [Staff relationship source contract](docs/implementation/staff-relationship-source-contract.md)
- [Staff relationships, private contact provenance and import](docs/implementation/staff-relationships.md)
- [Legacy account source contract](docs/implementation/account-import-source-contract.md)
- [Account import, canonical mobile authority and recovery](docs/implementation/account-import.md)
- [Bounded legacy mobile exchange and browser handoff](docs/implementation/legacy-mobile-bridge.md)
- [Calendar source contract](docs/implementation/calendar-source-contract.md)
- [Scoped calendar, protected import and recovery](docs/implementation/scoped-calendar.md)
- [Calendar verification checkpoint](docs/implementation/verification-2026-10-31.md)
- [Separate Hostinger preview and protected source imports](docs/implementation/isolated-preview.md)
- [Public website and protected content import](docs/implementation/public-content.md)
- [Web-admin authentication and permission source contract](docs/implementation/web-admin-source-contract.md)
- [Public/admin verification checkpoint](docs/implementation/verification-2026-11-01.md)

## Target stack

SvelteKit + TypeScript, ASP.NET Core + C# on .NET 10 LTS, PostgreSQL, OpenAPI with a generated TypeScript client, Docker Compose and GitHub Actions. Keep FFmpeg, Web Push/VAPID and Twilio behind an adapter; retain Python for specific AI/media workflows where it provides a concrete benefit.

The rewrite foundation uses this stack; functional migration is partial. Review the architecture documents and verification checkpoints for implemented behavior and compatibility limits.

## Local foundation

Use the SDK pinned in global.json and Node version pinned in .nvmrc. Docker Compose v2 is needed for the full container environment.

1. Copy .env.example to .env, set distinct owner/runtime passwords and generate independent base64 signing/OTP keys.
2. Follow the [fresh-database bootstrap commands](docs/implementation/mobile-identity-implementation.md) to build, migrate and provision the restricted runtime role.
3. Open the configured PUBLIC_ORIGIN. Configure a trusted development certificate or use a loopback HTTP development address.
4. The API health routes are /api/health/live and /api/health/ready. OpenAPI is enabled only in the ASP.NET Development environment.

NuGet and npm dependencies are locked. CI uses locked restores, policy/navigation/client checks, API contract drift checks, production web smoke checks and disposable Compose health checks. The published roster checkpoint passed all five jobs both before merge and on main at `63419dd3db9ca97e748a311ec43ee2fef6777c89`, including full Compose migration/bootstrap/health. The browser verification tool also runs in the PostgreSQL CI job. This workflow builds and tests; it does not deploy.

## Developer checks

    dotnet restore backend/src/LefteriaFc.Api --locked-mode
    dotnet build backend/src/LefteriaFc.Api --configuration Release --no-restore
    dotnet run --project backend/tests/LefteriaFc.PolicyChecks --configuration Release
    npm ci --prefix tools/api-client
    cd frontend
    npm ci
    npm run api:export
    npm run api:generate
    npm test
    npm run check
    npm run build
    npm run smoke:web

The API exporter launches an isolated local host with a deliberately unavailable loopback database. It verifies liveness/readiness behavior and writes contracts/openapi.json. The generator produces the TypeScript schemas used by the shared openapi-fetch client. Commit both generated artifacts after changing the API.

SvelteKit 3 uses TypeScript 6. The generator's upstream TypeScript 5 peer is kept
in tools/api-client with its own lockfile so neither toolchain bypasses peer checks.

Mobile login persists HMAC-protected OTP challenges, shared rate limits, explicit role grants and append-only audit events. Role changes preserve the original JWT expiry; removed formal roles expose explicit fan recovery. Management context uses current FC/Academy assignments. The API never migrates schema during normal startup. The mobile shell uses real identity/context responses and labels unported functions **Under Developments**.

The SMS adapter fails closed when Twilio is unavailable or unconfigured. Tests replace it only in the test host. The legacy mobile exchange is disabled by default and requires a separately approved acceptance window and protected source key. Initial protected backup/restore and import reconciliation are recorded for the separate preview. Quarantine resolution, real provider login verification, broader recovery/cutover rehearsal and cached-PWA update verification remain prerequisites for a production switch. Imported canonical player/guardian/coach relationships govern role availability. The calendar uses those current relationships, frozen member training publications and selected management organization; RSVP never marks physical attendance. Other private operational modules remain queued.

## Safety

Do not run rewrite migrations against production or switch live traffic without explicit cutover approval. The requested Hostinger preview must be a separate project with its own environment, database and secrets. Keep `lefteriafc.cy` and the live application unchanged. A protected source backup may supply an isolated import; production data must never be restored into, overwritten or removed by the rewrite. Staging, backup/restore, compatibility and recovery evidence remain prerequisites for any later production switch.
