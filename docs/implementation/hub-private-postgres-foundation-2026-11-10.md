# HUB PostgreSQL storage foundation — not yet activated

**Date:** 2026-11-10. **Branch:** `feat/hub-private-postgres-foundation-2026-11-10`. Stacked after PR #159, PR #158 and PR #157.

## Implemented schema

Migration `20261110180000_HubPrivateFoundation` provisions private `hub` tables:

- `content`: organization-owned article/drill/video/course metadata, audience and age ranges, draft/review/approval/publish state, optimistic version and explicit approval metadata.
- `revisions`: per-content body text and independent text/visual review status.
- `media_assets`: private storage references, SHA-256, byte length, alt text, generated-by-AI flag and quarantine/review status; **no binary image payload stored in the database**.
- `review_events`: immutable-intent chronology with explicit action, content/revision and admin actor reference; a future protected writer must enforce append-only audit.
- `fan_subscriptions`: contact identity reference, exact organization, audience-interest flags, consent timestamp, Payabl reference, provider-verification timestamp, paid period end and cancellation state.

The schema restricts supported states and ensures an `active` subscription has verified provider evidence and a future expiry relative to the verified timestamp. A later backend must check the **current clock**, fan identity role, revoked grants and provider reconciliation at each read; a SQL constraint alone never authorizes access.

## Security posture

- Creates **zero API endpoints**, zero payment calls and zero AI provider jobs.
- All HUB tables have PostgreSQL RLS enabled and all PUBLIC schema/table privileges revoked. There are intentionally **no application-role grants and no RLS policies** yet. Default behavior is no access.
- No automatic database migration on normal API startup. Migration requires the OS's existing explicit `--migrate` operator command with dedicated migration credentials in an isolated environment.
- No insert of example users, player records, minors' images, media or financial records.
- No publishing of AI-generated text or visuals without independently verified authenticated administrator permissions, moderation and revision locks.
- The draft TypeScript editorial policy from PR #159 is a prototype for future C# application and PostgreSQL functions. It is **not** a server-authoritative implementation.

## Next integration steps

1. Verify the new migration **compiles, applies and rolls back** in disposable PostgreSQL using the exact PR head. Check schema diff against EF snapshots; no production data/migration commands.
2. Implement role-scoped C# Hub domain services and restricted, audited PostgreSQL functions for create draft, attach sanitized visual, text/visual review, approve, publish and reject. Enforce current admin session and organization grant under lock.
3. Implement AI generation job submission and quarantined asset storage. Never give the AI admin privileges, customer addresses or personal records. Admin reviews final actual text and images.
4. Implement fan subscription issuance/verification via Payabl **sandbox**, and fail-closed public reader with independent fan identity, organization, audience and unexpired paid-entitlement checks. No UI Subscribe action until this is real.
5. Replace static HUB article feed with published-only authenticated API, including age-appropriate scope, player/guardian privacy and fan paywall tests; add editor UI, scheduled revocation and rate limiting.

**Validation completed here:** source-level migration structural tests committed, but **not executed**. No .NET compilation, PostgreSQL migration, CI or E2E test has passed in this turn. Keep draft and do not merge or deploy.
