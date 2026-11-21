# MVP Academy billing: verified original guardian as fee payer

**Scope:** production-readiness development, not an active production rollout.

## The missing daily workflow

Public/CRM Academy enrollment now creates a canonical child with a known city, Academy location and group. A distinct, human-reviewed guardianship proof and later phone OTP create the **original** guardian's Parent identity. The app can see the exact child, but `finance.issue_admin_training_fee` still chose payers solely from legacy `identity.mobile_record_links` + `identity.canonical_contact_bindings`. Newly enrolled children had neither, so a Super Admin could not issue their monthly Academy training fee even though the guardian was authorized. Parent wallet/PDF reads already use current child authorization; issuing was the missing prerequisite.

## New controlled payer fallback

Migration `20261121152000_AcademyOriginalGuardianFeePayer` changes **one existing SECURITY DEFINER SQL function**, retaining the same API signature and all previous authorization:

- Requires a current Super Admin authenticated session and Academy organization grant via `identity.lock_admin_club_authority`. **No location manager billing privilege is silently added.**
- First tries the original, active and canonical **imported guardian** selection. Existing customers retain their current payer preference, invoice identity and phone snapshot.
- Only if there is **no imported payer**, searches the exact approved Academy placement and an **active Parent mobile identity** with a matching original registration OTP claim. `identity.current_academy_guardian_claims(phone)` must independently return the same registration, child and organization. That helper rechecks nonrevoked human proof, active child, current five mandatory consents, current approved placement, OTP confirmation time window and source role conflicts.
- **Zero or more than one candidate fails closed.** Another Parent who only received a phone-based second-guardian delegation never becomes the payer merely because they can see the child.
- Existing strict `(organization_id,player_id,fee_month)` uniqueness, exact EUR cents, payer snapshot, replay semantics and human-selected **one month** are unchanged. No historical fee is rewritten or reassigned. The existing parent wallet and PDF continue to require `payer_identity_id` **and current exact-child access**, so revocation disables visibility on the next read without erasing accounting history.

## Verified negative cases

The disposable PostgreSQL `academy-enrollment-checks.sql` suite confirms: a freshly enrolled original guardian can be assigned one selected monthly fee; it is attributed to that verified Parent and stored with the correct phone; replay never issues twice; the original parent can see the resulting wallet/PDF; and revoking the human proof prevents later issuance with no side effect. The original tests for wrong phone, other children, second-guardian lineage and finance privacy still run. All synthetic records are rolled back.

## Remaining launch blockers

This does **not** prove live Payabl readiness. Gateway credentials, status enquiry/settlement reconciliation, saved-card mandates, Twilio delivery, invoice tax/legal checks and real refunds need independent merchant, accountant and deployment sign-off. Neither online payment processing nor monthly automatic debits are activated. New child fees can be issued **only by an authorized Super Admin**; separate location-scoped billing issuance, office admin UX, legal payer changes and proof renewal need later scoped reviews. Children with unresolved original-guardian conflicts must be reconciled manually.

**Release:** do not activate public Academy registration or deploy against the live Hostinger database until the broader exact-SHA release, protected source-data reconciliation, confidential backup/isolated restore and provider gates are independently verified. This PR contains no deployment, payment capture or live-provider calls.
