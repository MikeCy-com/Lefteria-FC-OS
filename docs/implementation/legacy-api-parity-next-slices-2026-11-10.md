# Legacy API parity: next protected implementation slices

Review date: 2026-11-10. Source baseline: `MikeCy-com/Lefteria-FC@91eafcc9efd01ad177b4f13a869cd5af7041e5b2`; target base: `Lefteria-FC-OS/main`. Source is read-only; production is out of scope.

This is a **focused gap list**, not a complete API diff. Confirm source contracts again if the source branch changes. Target implementation and runtime parity must not be inferred from document presence or draft PRs.

| Legacy API / source | OS current evidence | Next acceptance requirement |
| --- | --- | --- |
| `GET /api/mobile/notifications`, `POST /api/mobile/notifications/{nid}/read`, `POST /api/mobile/notifications/read-entity` in `backend/routes/notification_center.py` | OS `Program.cs` wires app announcement and chat endpoints; draft notification inbox and push PRs exist, but a full generic notification entity/badge/read API is not established on `main` | Member-only listing with canonical active-recipient checks, bounded pagination, per-entity idempotent read, deep-link validation, and strict player/guardian private-history isolation |
| `GET /api/mobile/family`, `PUT /api/mobile/family/children/{player_id}/phone`, child invitations in `backend/routes/family.py` | OS `MapParentFamily` exists, and multi-guardian direct-phone changes are **draft PR #154**, not deployed or merged | OTP-bound phone proof, guardian lineage and revoke tests for each child, secondary guardian access across all protected parent endpoints and sibling boundaries |
| Original `backend/routes/charges.py` and `stripe_checkout.py` | OS finance/Payabl work resides in **draft PRs #143/#146** | Issue selected charge types/months only, exact ledger reconciliation, authorized invoice downloads, verified Payabl sandbox settle/refund and never count redirects as payments |
| Original `backend/routes/pos.py` | OS has `MapAdminPosCatalog`, category and product endpoints; operational POS is staged in **draft PRs #152/#153** | Restricted cashier/shifts, inventory reservation/consumption, provider verification, fiscal receipts/refunds and actual transactional tests |
| Original CRM/evaluation/document routes | OS main has `MapAdminRosterPlayers` and manager group APIs; intake/evaluations are draft PRs | Age/guardian privacy, encrypted/quarantined files, per-team org and assignment authorization, revision control and audited updates |

## Change in this PR

All `/api/admin` and `/api/mobile` responses now receive `Cache-Control: private, no-store`, `Pragma: no-cache`, and `Expires: 0` at response start, including error responses. This is an **API privacy boundary**, not functional parity for missing modules. No public `/api/public` cache policy is changed.

## Verification outstanding

- Execute the new source-level Node regression and the full .NET API and SvelteKit suites.
- Verify the headers on real authenticated session responses, login, logout, 401, 403 and 500 using an isolated API instance and synthetic accounts.
- Verify public content caching remains governed by its own policy.
- Do not merge, grant new SQL permissions, issue real payment requests, or deploy until exact-head CI and security review pass.