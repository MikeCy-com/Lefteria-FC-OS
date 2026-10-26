# Lefteria FC OS — Rewrite

## Status

This repository is the parallel rewrite target. The framework foundation, PostgreSQL-backed mobile authentication and authenticated mobile shell are implemented. The source application and its data remain authoritative while the migration proceeds.

- Implemented slices: mobile identity, own profile settings, sanitized avatar uploads, canonical roster/staff relationships with private contact provenance, and an authenticated favourite-player picker.
- Source application: [MikeCy-com/Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)
- Audit baseline: GitHub main at 5111f06af5d8b17f855ceed9637caca02db117e2, observed 2026-10-25
- Source main rechecked on 2026-10-26: `7833daceceac1611c368e7bc4df2a82c64b45e1a` merges frontend PR #42. Its 15-file delta changes no backend contracts; the avatar handler's GitHub blob is unchanged. New dashboard layouts are now part of the source baseline.
- Deployed revision, VPS configuration and live data are not verified.
- No accounts or production data have been migrated. Own profile details, team/player preferences, notification preferences and a public mobile avatar can be edited. The picker requires an explicitly imported complete roster snapshot; operational dashboards, private roster relationships, notification delivery and social/media workflows remain queued.

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

## Target stack

SvelteKit + TypeScript, ASP.NET Core + C# on .NET 10 LTS, PostgreSQL, OpenAPI with a generated TypeScript client, Docker Compose and GitHub Actions. Keep FFmpeg, Web Push/VAPID and Twilio behind an adapter; retain Python for specific AI/media workflows where it provides a concrete benefit.

These are target decisions, not completed migrations. Review the architecture documents for behavior and compatibility requirements.

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

The SMS adapter fails closed when Twilio is unavailable or unconfigured. Tests replace it only in the test host. Legacy tokens are not yet accepted; a reviewed session bridge and account import remain cutover prerequisites. Player/guardian/team relationships and operational modules are not represented by invented records.

## Safety

Do not connect this branch to production services or run migrations against production. Do not deploy, switch traffic or replace production until staging, backup/restore, compatibility and rollback evidence is reviewed and the owner explicitly approves.
