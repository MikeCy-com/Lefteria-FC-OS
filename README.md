# Lefteria FC OS — Rewrite

## Status

This repository is the parallel rewrite target. The architecture checkpoint and a minimal runnable foundation are being built on this branch. The source application and its data remain authoritative.

- Rewrite branch: rewrite/architecture-and-foundation
- Source application: [MikeCy-com/Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)
- Audit baseline: GitHub main at 5111f06af5d8b17f855ceed9637caca02db117e2, observed 2026-10-25
- Deployed revision, VPS configuration and live data are not verified.
- No club feature, account, or production data has been migrated.

## Architecture documents

- [Current state](docs/architecture/current-state.md)
- [Target architecture](docs/architecture/target-architecture.md)
- [Folder structure and ownership](docs/architecture/folder-structure.md)
- [Technology evaluation](docs/architecture/technology-evaluation.md)
- [Migration roadmap](docs/architecture/migration-roadmap.md)
- [Risk register](docs/architecture/risk-register.md)
- [Architecture decisions](docs/architecture/architecture-decisions/)

## Target stack

SvelteKit + TypeScript, ASP.NET Core + C# on .NET 10 LTS, PostgreSQL, OpenAPI with a generated TypeScript client, Docker Compose and GitHub Actions. Keep FFmpeg, Web Push/VAPID and Twilio behind an adapter; retain Python for specific AI/media workflows where it provides a concrete benefit.

These are target decisions, not completed migrations. Review the architecture documents for behavior and compatibility requirements.

## Local foundation

Requires .NET 10 SDK, Node.js 24 and Docker Compose v2.

1. Copy .env.example to .env and replace the sample PostgreSQL password with a local-only value.
2. Run docker compose up --build from the repository root.
3. Open https://localhost. Caddy may use a local certificate that the browser does not trust yet.
4. The API health routes are /api/health/live and /api/health/ready. OpenAPI is enabled only in the ASP.NET Development environment.

The frontend manifest currently has no committed npm lockfile. CI and the Docker build use npm install until a reviewed package-lock.json is added; that dependency-resolution gap is tracked as R-18. This Compose setup is an isolated rewrite environment, not the production deployment configuration.

## Safety

Do not connect this branch to production services or run migrations against production. Do not deploy, switch traffic or replace production until staging, backup/restore, compatibility and rollback evidence is reviewed and the owner explicitly approves.
