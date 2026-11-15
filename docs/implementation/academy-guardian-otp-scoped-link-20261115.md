# Academy CRM: original guardian OTP-bound app scope (15 Nov 2026)

## Why this is a separate identity boundary

Public registration creates a pending application, never a trusted mobile identity. A Super Admin's independent guardianship proof review (PR #381) is a second distinct check; it does not establish possession of the recorded phone. **Only successful server OTP verification of an SMS challenge issued after that independent review** can establish the first active Academy guardian-to-new-player permission. This PR supplies the third gate.

The existing import-linked phone records remain unchanged; there is **no fake legacy import digest, automatic family grant, CRM payment, fan subscription or player identity**. The mobile auth service continues to use the existing OTP HMAC comparison; SQL independently rechecks the digest and locked challenge, proof time, current case status and child, latest required consents, and phone-role conflicts inside the login transaction.

A parent identity is provisioned only after OTP succeeds using the existing guarded `guardian_derived_parent` role policy. A previously active parent receives only this additionally approved child, not all children at the location/city. Inactive parent identities are **not** reactivated. The new `identity.academy_guardian_otp_claims` ledger is immutable, has one claim per approved child case and does not store OTP plaintext or its digest. The ledger supports multiple verified children under one original guardian phone; each child's proof and current consent remain separately required.

## Scope and revocation

`identity.current_academy_guardian_claims(phone)` is an internal SECURITY DEFINER helper. It returns only roster children whose approved application, immutable placement, proof review and **latest five mandatory consents** are still valid. An OTP claim expires no later than **90 days after human proof review**, to limit indefinitely trusted recycled phones; after expiry, a fresh supported proof-review process will be required. Administrators must not assume an expired proof can be automatically renewed.

`identity.current_phone_granted_children(identity,phone,'parent',expires)` joins those exact active children through the currently active mobile Parent identity. It also retains the existing lineage-based delegated grants. Canonical profile refresh, Family read, group and training-calendar scope, and parent education eligibility already consume this current helper. They immediately stop returning the Academy child if proof is revoked, latest mandatory consent is withdrawn, player is deactivated/transferred, claim expires, identity is deactivated or phone/role signatures differ.

**The parent app's entire private scope is not yet certified.** Some billing, invoice, documents, evaluations, notifications, messaging and coach-feedback flows still have original-import-specific checks. Those surfaces must be audited and updated individually, without broad authorization changes, before describing new guardian access as feature-complete. Payment issuance and saved-card/Payabl permissions are intentionally **not** enabled in this slice. Newly admitted original guardians cannot issue secondary guardian grants until a distinct revocable lineage-root check is implemented; attempting to do so fails explicitly instead of generating a success response with no effective permissions. Existing imported guardians' delegation remains intact.

## Security cases

- The original guardian phone in the approved application must match the separately document-reviewed phone and the OTP challenge phone; no matching names or location-scoped wildcard rights are accepted.
- OTP was issued **after** human review. Correct phone with an old or mismatched OTP, wrong digest, expired/reused challenge, wrong role, disabled Parent, a Coach/Player/Manager phone or missing current required consent does not create a new Academy claim.
- The runtime role receives execute only on the minimal eligibility and OTP-verification functions. It does not receive direct reads/writes of the proof/claim tables or the internal current-claim helper.
- Database proof records and OTP claims cannot be silently rewritten. A revoked review immediately removes effective child scope (the historical OTP record is preserved).
- Only genuine canonical original-parent bindings can anchor newly issued delegated guardian permissions in this slice. Non-canonical Academy original guardian sharing remains deliberately blocked until a new documented linkage/approval flow is implemented.
- All SQL tests use synthetic child data on a disposable PostgreSQL database and roll back. The public registration endpoint remains disabled; there is no Hostinger production deployment.

## Staging gates before use

1. Reconfirm Cyprus child-privacy/guardianship evidence requirements, opt-in OTP messaging permissions and correct phone normalization; enroll only authentic privately reviewed cases.
2. Run migration apply/rollback/reapply and role boundary scripts on **isolated staging**. Confirm no PUBLIC EXECUTE and no raw tables for mobile app.
3. Exercise real OTP via sandbox SMS after review; test two siblings with different proof records, existing parent, inactive parent, phone-recycled/role-conflict, expired claim, latest-consent revocation and revocation while logged in.
4. Audit all remaining private app surfaces for exact current-child authorization. Make secondary guardian delegation and proof renewal explicit before production release.
5. Never deploy to the live Hostinger site without a separate explicit production authorization.
