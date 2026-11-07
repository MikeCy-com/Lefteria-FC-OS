# OTP-verified family invitation acceptance: security design gate

Status: design only, no acceptance endpoint or player grant enabled. Tracks #45, #31, #26.

## Verified existing code boundaries

- `FamilyInvitationService` checks current identity, fan role and throttles recipient decline. Its repository exposes issue, cancel, outgoing read, recipient read and decline only.
- `ParentFamilyEndpoints` exposes recipient inbox and decline, but **no accept route**.
- `PostgresFamilyInvitationRepository` calls narrowly granted PostgreSQL security-definer functions and maps database faults to non-disclosing errors.
- The current recipient inbox intentionally exposes only invitation ID, organization, status and timestamps. Preserve this projection.

## Acceptance invariants (must all hold)

1. The request principal is authenticated, currently active, OTP-verified and owns the exact normalized invitation target phone. Recheck inside the database transaction; a previously valid JWT alone is insufficient.
2. The reservation is pending, unexpired, unrevoked, uniquely bound to one child, and the source guardian remains active and authorized for that child and organization.
3. The child is active, non-archived and not transferred; its canonical player identity is unambiguous. Never infer identity from phone or surname alone.
4. A target account already associated with another player must fail closed and enter reconciliation; do not overwrite identity or silently merge profiles.
5. Exactly one active, revocable player grant may result. Locks must serialize acceptance with parent cancellation, invitation reissue, player transfer and revocation.
6. Accepting cannot silently create guardian permissions or disclose other children. Revocation must remove player access on the next protected read while preserving unrelated fan identity.
7. Emit an immutable audit event containing actor IDs and transition IDs but no raw phone, OTP, child private fields or token.
8. All negative cases (wrong phone, role, stale session, guessed ID, expired/cancelled/declined invite, missing guardian, archived/transferred child, collision) must fail without side effects or information leaks.

## Proposed transaction boundary

Use one narrowly granted SECURITY DEFINER PostgreSQL function, fixed search_path and explicit schema-qualified references. Acquire locks in deterministic order for invitation slot, canonical child binding, target identity and grant; validate all preconditions after locks; perform conditional pending-to-accepted transition and grant insertion atomically. Enforce uniqueness in database constraints, not application-only checks. Audit in the same transaction. Specify replay semantics: a repeated request must not mint another grant; a different identity must never learn whether a slot exists.

## Implementation order

1. Inventory actual roster, account mapping, session/OTP and role-grant schema, including PostgreSQL grants and all affected API authorization checks.
2. Add migrations, scoped function and concurrency/negative PostgreSQL tests with no public route.
3. Add service/repository/endpoint with current identity checks, throttle and generated OpenAPI client; keep UI disabled until tests pass.
4. Add two-step accessible recipient confirmation UI and logout/role-change race tests; add revocation flow and current-grant enforcement.
5. Require five GitHub Actions checks and explicit security review before merge into **OS main only**. Run combined main checks after merge. Production deploy, real SMS and source repo changes remain prohibited.

## Mandatory test matrix

- Positive OTP target with reconciled canonical child: one grant and one audit transition.
- Wrong phone, unrelated fan, parent role, stale JWT/session, invalid ID, expiry and replay.
- Cancel vs accept, decline vs accept, two simultaneous accepts, revoke vs accept and guardian revocation vs accept.
- Child archived/transferred, guardian deactivated, wrong organization, sibling independence and conflicting existing player binding.
- No child PII in recipient inbox or error payload, no leaked raw phone in logs, no cross-account data access after revocation.
- Generated API drift, browser SSR and current-session client state invalidation.

Do not implement acceptance as a mere status update: account-to-player reconciliation, current authorization and revocable grant enforcement are required before exposure.

## Schema findings and blocker confirmed from main

The existing `identity.mobile_record_links` table enforces a canonical-contact foreign key and a check constraint requiring `role='player'`, `collection='players'`, `kind='player_own'` for a player link. It also enforces one non-guardian self link per identity and a matching `mobile_identities(id,normalized_phone,role)` key. The existing invitation issuer explicitly rejects any child with a `player_own` canonical binding and any target with a `player_own` binding, while the runtime role's RLS restricts new mobile identities to `fan`. Therefore **the current schema does not support safely turning a recipient's fan reservation directly into a canonical player identity**. Simply flipping `family_invitations.status` to `active` would not provide authorized player access.

Before implementing any acceptance transaction, choose and review one of these explicit models: (a) a separate revocable family-access grant with dedicated player authorization semantics, distinct from the canonical `player_own` link, or (b) a controlled canonical phone/identity ownership transfer with independently verified guardian consent and reconciliation. Do not silently relax existing contact-link foreign keys, RLS, or role constraints. Model (a) is preferable when the recipient is an invited family delegate rather than the child who owns the canonical player phone, but the product role semantics must be resolved first.

Required acceptance gate: prove in integration tests that the new grant is honored by every intended private player endpoint and rejected by every other endpoint, including after revocation, without creating an incorrect `player_own` contact binding. Until that is possible, leave acceptance unavailable.
