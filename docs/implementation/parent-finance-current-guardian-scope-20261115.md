# Parent finances — exact current-child authorization (2026-11-15)

## Why

New Academy players now have an approved roster placement and can be connected to their original guardian only after an independent document review and phone OTP. Family, calendar and education services use `identity.current_phone_granted_children`, which recalculates **active** child-specific permissions. The pre-existing fee wallet and invoice PDF used only imported `canonical_contact_bindings`, so legitimate newly verified guardians were still blocked from their own bills. Keeping divergent parent authorization also risks a stale imported link outliving a child's permission.

## This change

The two existing PostgreSQL security-definer read functions are updated, **without changing their HTTP routes or database signatures**:

- `finance.read_parent_training_fees(identity,phone,role,expires)`
- `finance.read_parent_invoice_pdf(identity,phone,role,expires,invoice_ids)`

Both independently validate that the current mobile identity is an active Parent for the requested phone and nonexpired session. Both require the **immutable invoice's designated `payer_identity_id` equal the authenticated identity**. They further require an active, nontransferred Academy child in that parent's **current** child set via `identity.current_phone_granted_children`. This includes independently reviewed, OTP-confirmed original Academy guardians without synthesizing a legacy import digest, and preserves authorized imported parents.

The wallet filters by current child **before** its 100-entry limit; it uses `EXISTS` rather than a join, so overlapping guardian grants cannot duplicate amounts or manipulate the limit. Invoice PDFs retain the original strict batch rule: if even one ID is missing, belongs to another payer or lacks present child authorization, **the entire PDF request is denied**. The original invoice metadata, payer details, payment method, status and amount calculations remain unchanged.

Revoking a guardian proof, changing phone/role, disabling a child, withdrawing required consent, claim expiry or removing a family permission makes private fees disappear from the next read without waiting for JWT expiry. Historical issues and payments remain immutable. There is **no new transfer of liability** to a different parent: the payer-ID check remains mandatory.

## Exclusions and rollout

This change does **not** expand Academy finance creation, provider payments, saved-card collection, monthly automatic debits, email/SMS, child-document access or PDF publication. The existing admin issuance path still requires its original verified payer mechanism; adding a safe first-class payer allocation workflow for newly registered children is separate work and must be audited against city/location permissions. The parent app should never infer that every guardian is the payer or reveal another parent's contact and payment details.

Migration up replaces only these two read functions; down restores their previous stricter imported-only behavior, and both retain `REVOKE EXECUTE FROM PUBLIC`. No new direct table permissions or runtime grants are created.

## Verification

- `frontend/tests/parent-finance-current-guardian-scope.test.mjs` checks function signatures, role and payer guards, all-or-nothing invoice behavior, absence of payment mutations, and the reversible migration.
- Disposable `ops/postgres/academy-enrollment-checks.sql` adds two fee issues both referring to the same payer: one for a verified child, one for an unrelated Academy child; tests wallet shows only the permitted child, invoice PDF denies mixed batches and wrong phone/other parent, and revocation closes both immediately. The entire fixture rolls back.
- Existing `ops/postgres/payabl-training-fee-checks.sql` continues to reject unlinked parent invoices and verify that only actual signed payments mark a training month paid.

This is one **read-only authorization-parity slice**, not a statement of production readiness across the entire parent portal. The public Academy registration switch and live Hostinger deployment remain disabled/untouched.
