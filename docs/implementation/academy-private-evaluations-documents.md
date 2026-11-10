# Academy evaluations and protected player documents — development foundation

**Repository:** Lefteria-FC-OS only. **Scope:** default-deny data model, not yet a usable production admin module.

## Original source mapped

- `backend/routes/player_evaluations.py` and `frontend/src/pages/admin/PlayerEvaluationsPanel.jsx`: per-player dated evaluation snapshots, technical/tactical/physical/character ratings, qualitative overall section; original author has a 24-hour edit window, with role overrides. Original First Team extras deliberately excluded from Academy.
- `backend/routes/player_documents.py`: 14 document categories, identity/registration/medical/compliance records, per-player expiry dates, expiry alerts and admin-only upload. Medical and consent material require stricter handling than ordinary profile files.

## Added in OS

- Migration `20261110100000_AcademyPrivateEvaluationsDocuments`: Academy-only evaluations and revision records, author identity, fixed source-compatible 24-hour edit-window metadata, source import identifier uniqueness, current player foreign keys.
- Bounded document metadata with the original category catalog, mandatory expiries for applicable types, opaque private storage IDs, content hashes, encrypted-storage key identifiers and 10 MB cap. Expiry indexes enable future manager-only reminders.
- Document audit event structure for upload/view/download/metadata update/withdraw with unique request IDs.
- Default-deny row-level security and zero application grants; no upload, download, evaluation or access-control endpoints are activated. No data is currently inserted or made visible.

## Required before granting access

1. Implement authorization at both HTTP and SQL boundaries: exact Academy owner, active admin session, permission checks and explicit coach-to-player assignment; parent/child views require separately approved consent and data minimization.
2. Validate evaluation score keys and 0–100 values server-side. Implement revision creation atomically, and enforce author-only writes within 24 hours or explicitly audited manager overrides at database level, not merely UI hints.
3. Implement a private object-store adapter with streaming size/magic-byte validation, malware scanning, encryption, key recovery/rotation and quarantined uploads. Never allow public URLs, guessable paths or direct object access.
4. Append audited reads/downloads and document edits; ensure withdrawn metadata cannot accidentally expose orphaned files. Agree on retention, deletion, data-subject access procedures and historical migration classification.
5. Add manager-only grouped UI for timeline, Greek evaluation form, protected document list and expiration alerts. Preserve existing original mobile/public UI boundaries.
6. Run actual .NET migrations, rollback-only PostgreSQL access and lifecycle tests, Node/SvelteKit validation, full CI, security review and visual comparison. No builds or DB integration checks are claimed passing here.

**Explicitly not complete:** no real file bytes stored, no active runtime endpoint, no verified score workflows or uploaded documents. Keep PR draft; do not merge into OS main or deploy until validated.

## Follow-up: evaluation revision enforcement (2026-11-10)

The next migration `20261110110000_AcademyPrivateRevisionGuards` now enforces the source's **author-owned 24-hour revision window at the database boundary**, locks the parent evaluation row, rejects stale or out-of-order version numbers, replaces client-supplied edit timestamps with database time, and appends separate revisions rather than overwriting original assessments. Technical/tactical/physical/character score keys are allowlisted and required to have integral values from 0 to 100. First Team extras remain excluded.

Revision and document-audit events are protected by update/delete rejection triggers. A disposable rollback-only PostgreSQL probe and source assertions were added; the isolated CI PostgreSQL job is configured to run that probe after migrations. These controls do **not** supply a complete write authorization service, administrator override workflow, signed upload or audit-on-download endpoint.

**Verification blocker:** GitHub Actions run `42755249081` reported all five jobs failed with **zero steps executed**, so neither the official migration execution nor runtime tests are proven. Do not mark this PR ready, merge it or deploy.

**Next operational slice:** add an authorization service with actor/session + exact organization and player assignment validation, plus a private admin evaluation timeline and controlled submit path. Protect document object bytes and audited download authorization separately before exposing any files.

## Follow-up: manager-only evaluation timeline (2026-11-10)

- Added `20261110120000_AcademyEvaluationManagerRead`, a SECURITY DEFINER, bounded, read-only player timeline function. It rechecks the active management session, exact Academy organization grant and currently active/non-transferred Academy player. No raw source or document access is granted.
- Added `GET /api/admin/academy/players/{playerId}/evaluations?organization_id=lefteria_fc_academy` with web-admin cookie auth, manager/super-admin role and Academy read permission, no-store responses, parameter binding and controlled access errors.
- Added a read-only `/admin/evaluations` view, manager-only navigation, defensive response validation and stale-session clearing. This first UI uses a player ID selected from the existing roster; it does not enumerate all children or add public/mobile access.
- Added operator-only `ops/postgres/academy-evaluation-admin-grants.sql`: only the private read function can be explicitly enabled for the dedicated web-admin database role. Do not apply to a production database without approval. No upload, write, edit override or document-view endpoint has been activated.
- Added `frontend/tests/academy-evaluations-read-endpoint.test.mjs` for source-level auth, SQL, UI and permission invariants.
- GitHub Actions run `42755822571` on this PR failed all five jobs with **zero execution steps**. No official .NET, SvelteKit, PostgreSQL or OpenAPI success is claimed. Contracts will need regeneration and exact-SHA tests when runners work. Keep this PR draft and unmerged.
