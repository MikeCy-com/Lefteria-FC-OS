# CRM Academy registration → canonical roster (2026-11-15)

## User workflow

1. The existing five-step Academy public form persists a **pending** application with sealed medical/signature evidence and seven versioned guardian consent decisions. Public submission stays disabled by default until legal, encryption and trusted-network requirements are verified.
2. Under **CRM & επαφές → Εγγραφές Ακαδημίας**, a verified Academy Super Admin marks a website application **Υπό εξέταση**. The existing status transition does not add a player.
3. The same authorized user opens **Επιλογή πόλης, τοποθεσίας & τμήματος**. Selectors are populated from the server-authorized football territory directory; only writable locations and their canonical groups appear. The server and PostgreSQL recheck all grants and exact group location in the same transaction.
4. The administrator explicitly enters the **real** playing position and shirt number. This avoids the old Python workflow's invented Midfielder/age/number defaults.
5. **Έγκριση & ένταξη στο ρόστερ** invokes `POST /api/admin/academy/office/registrations/enroll`. The privileged SQL command creates the canonical Academy player, adds exactly one group membership, persists immutable city/location/group placement, links the application via `onboarded_player_id`, advances status/version to approved, records the review event and security audit **atomically**. A failed operation rolls back all changes.

## Security and limitations

- **No implicit cross-organization or cross-location access**. First Team groups are excluded. Super Admin has Academy scope; delegated city/location managers must hold exact write grants. Unassigned public applications continue to be visible/enrollable only by an Academy Super Admin. Managers may read their own manual entries, not someone else's or a public unassigned application.
- **Consent gate**: public application must contain sealed evidence and current required participation, emergency medical, privacy, liability and financial consents; optional media/communications consent can be false. This feature does not decrypt or display medical evidence. The owner must complete a separate legally reviewed guardian verification procedure before production activation. Existing manual entries have no complete evidence and thus **cannot** auto-enroll.
- **Duplicates**: the canonical roster lacks reliable imported DOB/guardian identity for older players. Identical names in existing canonical Academy players are therefore blocked rather than guessed or auto-linked; resolve legitimate same-name applicants through a separate verified process.
- **Concurrency**: status-version check, row and scoped grant locks, name-lock, deterministic request UUID→player ID, and exact replay handling avoid two players from a double click or a lost response. A retry after grant revocation is denied.
- **No automatic parent account, guardian permission, app registration, fee, POS order, Payabl charge, SMS, fan content or notification**. Parent app access must follow verified phone binding to the new player; adding a player alone is not authorization to view their private data.
- **Approved legacy/manual registrations** remain status-only applications until a separate approved verification/onboarding path is designed. Do not present approval as completed roster assignment for these cases.
- **Not deployed**. New SQL migration and explicit runtime-grant update must pass real isolated PostgreSQL tests (including migration up/down and release CI), and staging privacy/legal sign-off before enabling public submissions. Never apply to live production without a separate release decision.

## Verification

- `frontend/tests/admin-academy-atomic-enrollment.test.mjs`: Svelte compile and API/SQL authorization contract.
- `ops/postgres/academy-enrollment-checks.sql`: synthetic users, signed-in role checks, authorized group, missing/foreign location denials, successful atomic player + placement, idempotent exact replay, conflicting request denial, grant revocation. One transaction, always `ROLLBACK`.
- Both the GitHub Rewrite build PostgreSQL job and the separate disposable workspace integration script include the SQL test.

## Follow-on work

The next slice is **verified guardian linking** for newly created players (no automatic role promotion); handling duplicate-name cases with independently verified identity and explicit operator confirmation; capture/verification of equivalent evidence for offline paper registrations; location-limited CRM assignment and consent detail review with purpose-specific decrypt permissions; clear progress states for approved-but-unenrolled applications. Do not remove the original `/admin/academy-office` route until callers are migrated.
