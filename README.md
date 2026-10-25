# Lefteria FC OS — Rewrite

## Status

This repository is the parallel rewrite target. The framework foundation and the first mobile identity/navigation policies are implemented. The source application and its data remain authoritative while the migration proceeds.

- Rewrite branch: rewrite/architecture-and-foundation
- Source application: [MikeCy-com/Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)
- Audit baseline: GitHub main at 5111f06af5d8b17f855ceed9637caca02db117e2, observed 2026-10-25
- Deployed revision, VPS configuration and live data are not verified.
- No accounts or production data have been migrated. Phone login and operational dashboards are still being ported.

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

## Target stack

SvelteKit + TypeScript, ASP.NET Core + C# on .NET 10 LTS, PostgreSQL, OpenAPI with a generated TypeScript client, Docker Compose and GitHub Actions. Keep FFmpeg, Web Push/VAPID and Twilio behind an adapter; retain Python for specific AI/media workflows where it provides a concrete benefit.

These are target decisions, not completed migrations. Review the architecture documents for behavior and compatibility requirements.

## Local foundation

Use the SDK pinned in global.json and Node version pinned in .nvmrc. Docker Compose v2 is needed for the full container environment.

1. Copy .env.example to .env and replace the sample PostgreSQL password with a local-only value.
2. Run docker compose up --build from the repository root.
3. Open https://localhost. Caddy may use a local certificate that the browser does not trust yet.
4. The API health routes are /api/health/live and /api/health/ready. OpenAPI is enabled only in the ASP.NET Development environment.

NuGet and npm dependencies are locked. CI uses locked restores, policy/navigation/client checks, API contract drift checks, production web smoke checks and disposable Compose health checks. Local API/frontend builds pass; Docker execution still requires a working Docker host or GitHub runner.

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

The mobile policy core preserves explicit roles, device expiry, five-minute OTP limits and FC/Academy isolation. The HTTP login, persisted grants, relational ownership, audit storage and real dashboard workflows remain queued; navigation visibility alone never authorizes data.

## Safety

Do not connect this branch to production services or run migrations against production. Do not deploy, switch traffic or replace production until staging, backup/restore, compatibility and rollback evidence is reviewed and the owner explicitly approves.
