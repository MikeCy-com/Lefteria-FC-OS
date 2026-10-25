# Lefteria FC OS — Rewrite

## Status

This repository is the parallel rewrite target. The first checkpoint is the evidence-based audit and architecture design on this branch. Application code and production data have not been migrated.

- Rewrite branch: rewrite/architecture-and-foundation
- Source application: [MikeCy-com/Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)
- Audit baseline: GitHub main at 5111f06af5d8b17f855ceed9637caca02db117e2, observed 2026-10-25
- The deployed revision and VPS configuration are not verified.
- This repository is not connected to production deployment. The existing application and data remain authoritative until an approved cutover.

## Architecture documents

- [Current state](docs/architecture/current-state.md)
- [Target architecture](docs/architecture/target-architecture.md)
- [Folder structure and ownership](docs/architecture/folder-structure.md)
- [Technology evaluation](docs/architecture/technology-evaluation.md)
- [Migration roadmap](docs/architecture/migration-roadmap.md)
- [Risk register](docs/architecture/risk-register.md)
- [Architecture decisions](docs/architecture/architecture-decisions/)

## Target stack

The owner-selected direction is SvelteKit + TypeScript, ASP.NET Core + C# on .NET 10 LTS, PostgreSQL, OpenAPI with a generated TypeScript client, Docker Compose and GitHub Actions. Keep FFmpeg, Web Push/VAPID and Twilio behind an adapter; retain Python only for specific AI/media workflows where it provides a concrete benefit.

These are target decisions, not completed migrations. See the technology evaluation and ADRs for compatibility and exit gates.

## Safety

Do not connect this branch to production services or run migrations against production. Do not deploy, switch traffic or replace production until the owner explicitly approves after staging, backup/restore, compatibility and rollback evidence is reviewed.
