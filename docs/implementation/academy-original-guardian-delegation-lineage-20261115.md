# Academy second-parent access — original guardian lineage

## Implemented boundary (PR following #388 and #391)

The Family mobile app already has a parent-controlled **Give Permission → Parent/Guardian → phone number** workflow, with separate child selection or all selected children, outbound permissions listing, and explicit revocation. The recipient does **not** need an invitation. They still must prove possession of their own phone via the existing OTP sign-in before obtaining a Parent account or reading any children.

That feature previously rooted delegated guardians **only** in imported canonical guardian bindings, and the new post-enrollment original guardian was deliberately blocked from sharing with another parent even after a staff-reviewed original guardian proof and OTP.

The new migration extends the **same** lineage, not a separate app messaging or registration model:

1. An original Academy guardian qualifies as a *root* **only** while `identity.current_academy_guardian_claims(original_phone)` contains that exact approved child and Academy organization. This function independently rechecks the original registration placement, roster status, current mandatory consents, nonrevoked human review and successful post-review OTP claim expiration. The claimed phone must belong to an **active Parent** identity.
2. The root permission uses the original guardian's active identity ID, exact child, Academy organization and chosen second parent's phone. It remains subject to the existing consent, 20-recipient limit, idempotency request ID, phone role-conflict guard and audit event.
3. The recipient's phone grant grants **no account**. The existing mobile OTP flow alone may create or reuse an active `guardian_derived_parent` identity after verification; a disabled Parent cannot be silently reactivated. **No PIN, admin invitation, direct phone-registration authentication or guessed family name** can replace that OTP.
4. A second guardian who shares a child with another adult **must** retain `source_permission_id` identifying their upstream permission. A trigger prevents anyone who is *only delegated* from fabricating an original root, even through a direct privileged SQL insert. The recursive `identity.valid_guardian_phone_grants()` walk follows the original guardian's current authority and subsequent lineage for up to 20 hops.
5. The original guardian can revoke an issued phone grant, cascading revocation through its descendants for **that exact child**. Even without an explicit second-parent revocation, withdrawing the original staff review, the required child consent, or the first guardian's time-bounded OTP claim makes the entire chain immediately invalid on the next private read. Already issued JWTs are never treated as lasting child authorization.

The existing Family, role availability, child calendar, training schedule, HUB and mobile chat group directory functions already use `identity.current_phone_granted_children` and therefore consume this newly valid lineage. **The full mobile/private feature set is not certified**; additional messaging, uploads, evaluations, finance issuance, notifications and coach feedback still require explicit per-surface verification. As with the previously merged finance read-scope change, second-parent delegation does **not** transfer the invoice's designated payer identity or disclose another payer's financial data.

## Database verification

The disposable `ops/postgres/academy-enrollment-checks.sql` exercise now checks: proof absent before human review and OTP; first guardian only after valid OTP; issuance of one child grant to another phone; idempotent replay; no automatically created parent session; second adult independently treated as OTP-authenticated for the synthetic fixture; second adult sees only the approved child; prevention of a fabricated second root; a third recipient's permission explicitly linked to the first; revocation cascading through all descendants; explicit new regrant; no cross-child access; and immediate invalidation of all grants when the first guardian review is revoked. Every fixture write rolls back.

The new migration modifies **only** `identity.valid_guardian_phone_grants()` and the existing insertion guard function. Existing app-role function grants and RLS policies are not broadened. Its down migration restores legacy-only anchoring but refuses to drop live new guardian lineage history; such environments require a carefully reviewed forward migration instead.

## Release controls

Public Academy registration remains disabled until privacy, legal, guardian dispute and consent handling, staged OTP/SMS delivery, proof renewal and phone recycling responses are reviewed. Validate real device OTP, several siblings with different original guardians, separated parents, consent changes, disabled phone accounts, and recipient role conflicts in isolated staging. No Hostinger production deployment or public intake activation is performed by this PR.
