# Academy registration — private intake foundation

Branch: `feat/academy-registration-private-intake-2026-11-09` in **Lefteria-FC-OS only**. Original Lefteria-FC and live services untouched.

## Source comparison
Original `frontend/src/pages/RegistrationPage.jsx` has five steps:
1. Athlete details
2. Guardian and emergency contacts
3. Medical disclosures
4. Guardian consent and privacy choices
5. Signature and payment selection

Original `backend/models.py` includes the pending application, group assignment, status and administrative notes. This is not a ready-made safe access model for OS; import and implementation require separate privacy/security review.

## Implemented
An additive EF/PostgreSQL migration stores staging applications, independent consent evidence with explicit media refusal, sealed medical/signature evidence as ciphertext, and an immutable-intended review history table. It enforces Academy organization scope, unique legacy import identifiers, decision actor linkage and a non-automatic roster link. All four tables have RLS enabled, default denial and no runtime grants. This migration intentionally does not enable anonymous intake or automated billing.

## Next work before a working registration
- Consent wording and required flags must have approved, versioned Greek legal copy; establish age/retention rules and key rotation/recovery for sealed data.
- Add bounded rate-limited public POST with anti-abuse, strict validation, idempotent request receipt and encrypted evidence using a reviewed encryption service. Never trust browser timestamps, chosen fees, payment state, or user-supplied role.
- Add manager-scoped private list/detail/decision functions with atomic status transitions, actual append-only database-enforced review audit, admin UI and negative authorization tests.
- Reconcile original registrations by immutable source IDs without sending historical private data to public results. Approval must not automatically create a player, guardian account, invoice, group or SMS.
- Restore the branded five-step UI only after the secure live submission path exists; until then the current public fallback remains truthful.
- Run actual .NET + PostgreSQL + SvelteKit CI on the exact PR SHA, privacy/retention tests, migration rehearsal and source parity review before merge.

**Not yet production ready.** No tests against a running database are claimed. The source-level Node assertions are smoke checks only.
