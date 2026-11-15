# Academy CRM — human guardian proof review and revocation (15 Nov 2026)

## Workflow, location and permissions

This slice keeps guardian proof review **inside CRM → Εγγραφές Ακαδημίας**, under an approved, roster-linked public Academy application. It does not add a duplicate Academy office or authorization menu.

Only an active Super Admin explicitly authorized for `lefteria_fc_academy` can open the proof panel. The API checks the admin cookie, role, organization grant and permission. The SECURITY DEFINER PostgreSQL function independently revalidates the active Academy session and exact org. A location manager, other organization, coach or unrelated admin cannot open, verify or revoke the case.

A Super Admin must first independently verify identity and legal guardianship using a real document/court order, then enter an **opaque case reference** pointing to an independently protected case file, select the proof method and explicitly attest to that review. The service checks that the normalized phone matches the **original guardian phone in the approved public registration**; it cannot attach an arbitrary third-party phone. The case must already have a verified Academy roster placement, be active and have the required latest guardian consent evidence.

A successfully attested case creates a versioned, audit-logged `academy_intake.guardian_proof_reviews` row and an immutable review event, with no raw proof documents, medical details or child private data in the response. `read` returns only masked phone suffix, method, timestamps, state and version. Lost-response retries with the same request ID and inputs are idempotent. Conflicting requests fail, a revoked case cannot silently reopen, and the review history cannot be deleted or overwritten. Revoke uses an optimistic version check and an audit event.

**Critical release boundary:** a recorded human attestation is *not* a mobile app login, a phone OTP, a parent identity, a canonical phone binding, a family grant or authorization to view a child. Every response explicitly returns `app_access_enabled:false`. No fan/parent account, billing, SMS, invitation, Payabl charge or push notification is generated. The new review function is executable **only** by the separate web-admin database role, via an explicit operational grant. No mobile runtime grant or direct table access is added.

## Why mobile access remains blocked

The existing imported-parent model uses `identity.canonical_contact_bindings` and `identity.mobile_record_links`, while newer family/calendar/chat routes also support live phone-scoped grants. Finance, protected player information and other app surfaces still need exact child-scope parity work. Automatically converting the new proof review into one of those imported identities could reveal unrelated children's information. The public form's supplied number is not OTP proof; nor does a database review independently establish control of that phone.

The next linked implementation must add a separate, fail-closed **OTP-confirmed parent-claim finalization** process and explicitly audit every private endpoint's current-child authorization before enabling broader access. It must cover two guardians, multiple children, phone recycling/changes, separated or revoked guardians, delegated lineage, minor/coach phone-role conflicts and consent revocation. Until then, the guardian review is a human case-management tool only.

## Verification

- The exact endpoint paths are approved in the fail-closed preview edge inventory. They remain excluded from the public OpenAPI contract.
- Structural Svelte, API and SQL tests: `frontend/tests/admin-academy-guardian-proof-review.test.mjs`.
- Disposable PostgreSQL tests are appended to `ops/postgres/academy-enrollment-checks.sql`: pending applications denied; manager denied; wrong phone/absent attestation denied; approved original guardian records and exact retries; conflicting requests denied; masked reads; no mobile grants; audited revocation and no reopening.
- Up/down migration safety and project build/CI must pass on the final PR SHA before merge. Existing operator grant scripts must be reviewed on isolated staging before use.

No Hostinger production deployment or activation of the public registration endpoint.
