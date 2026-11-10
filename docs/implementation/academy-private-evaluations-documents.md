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
