# Pending family invitation reservation — first controlled write slice

**Scope:** Issue #31; original `MikeCy-com/Lefteria-FC` is **read-only**. Changes only in `MikeCy-com/Lefteria-FC-OS`.

## Implemented on this branch

- Authenticated **parent** can invoke `POST /api/mobile/family/children/{playerId}/invitations` with `{ "phone": "+..." }` to reserve a pending invitation **without granting a player login**.
- Authenticated parent who still owns the canonical guardian link can invoke `POST /api/mobile/family/invitations/{invitationId}/cancel`. A repeated identical cancellation returns success without duplicating audit entries.
- A slot is unique per canonical player; pending/active normalized target phone slots have a unique partial index. Existing club player-phone ownership and formal-role phone collisions are denied. Parent cannot invite their own phone.
- A pending invitation has a maximum seven-day lifetime, version counter, immutable historical event identifiers (stored in append-only-by-runtime event rows), stable idempotency on an existing same-parent/child/phone pending reservation and transactional security audit.
- Database owner-checked `SECURITY DEFINER` functions lock the current mobile identity, guardian link, canonical contact binding and roster player. Role, phone, session expiry and active/nonarchived/nontransferred player status are revalidated **inside the final mutation statement**.
- Dedicated HMAC-keyed throttles bound to current parent identity (10/hour) and normalized target (4/hour) prevent invitation spam. No SMS, push notification or external provider is called in this step.
- The API database role receives only EXECUTE on the two functions. Direct SELECT/INSERT/UPDATE/DELETE of phone-bearing slots and events is prohibited by reviewed grants. There is no public invitation list, raw target-phone API field, URL invitation token or invitation-accept route.
- A forward-only data-recovery guard blocks rollback of a migration with invitation/event/audit data. SQL-only EF migration is intentionally not part of the tracked model snapshot.
- HTTP/PostgreSQL tests exercise role isolation, unrelated child, canonical phone conflict, idempotency, cancellation, revocation, audit count, and direct database privilege denials.

## NOT YET IMPLEMENTED — do not claim user journey complete

- Target-user inbox, verified OTP acceptance/decline, collision-free player identity activation, active grant revocation, guardian dashboard edit controls, source snapshot reconciliation, and organization-specific visual parity.
- Current parent mobile UI retains **Under Developments** for invitation actions until phone proof, pending state and lifetime/cleanup behavior are fully implemented.
- The caller should not infer that a pending record authorizes the target to access player data, even if the user has the same phone as an invitation.
- No production database, SMS sender, or original MongoDB was accessed or mutated. Migrations run only in isolated CI and future explicitly approved deployment.

## Gates

Only merge after .NET/API build, OpenAPI client contract regeneration and exact diff, disposable real PostgreSQL HTTP integration, runtime least-privilege checks, SvelteKit tests and Docker Compose smoke all pass. Main-branch combined check, hosted visual QA, and eventual production rollout are separate steps.
