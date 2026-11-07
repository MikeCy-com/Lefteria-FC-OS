# Recipient invitation inbox — phone-verified read-only slice

**Repository isolation:** Original `MikeCy-com/Lefteria-FC` is read-only. The feature is implemented only in `MikeCy-com/Lefteria-FC-OS`. No VPS migration, cutover, SMS delivery or production change.

## Implemented

- `GET /api/mobile/family/invitations` is authenticated with a real mobile session and **only the active `fan` role**. A club role (parent/player/coach/management) cannot substitute for a recipient session; a user with multiple roles must explicitly switch to the independently verified fan role.
- A dedicated PostgreSQL `SECURITY DEFINER` function validates the active fan identity's matching normalized phone and unexpired session **before** returning rows. It also rechecks that the original parent is active and still has a canonical guardian contact binding to the exact active, nonarchived, nontransferred child.
- The recipient sees only the opaque invitation ID, FC/Academy organization, `pending` status and creation/expiry timestamps. Child identity, child name, guardian identity, source records, guardian and target phones, history and audit events remain hidden. A maximum one-reservation response is enforced; a second result fails closed.
- Cancelled, expired, transferred, archived, guardian-revoked or disabled-parent reservations are not visible.
- Database app role has only EXECUTE on the read function, not SELECT/DML on family invitations. Exact mobile bearer edge routing is extended for this GET path; unrelated methods and path suffixes remain blocked.
- Real HTTP tests log in the intended fan and a different fan through the existing OTP flow. They verify denial for unauthenticated and all formal-role callers, address isolation, no personally identifying response fields, cancellation and reissue, revoked guardian link, inactive parent and archived child.

## Not yet implemented

This is a **read-only inbox**, not invitation acceptance. No player profile or child name is disclosed in the recipient response. No notification or SMS is sent; existing pending reservations are not delivery confirmations. A separate security-reviewed phase must establish target OTP proof, reconcile existing formal identities and canonical player ownership, persist an active link and auditable revocation before a recipient can accept or decline a real player access invitation.

Parent outgoing reservations and cancellation remain unchanged.

## Validation / release

All five GitHub PR checks (SvelteKit, .NET, OpenAPI contract, real PostgreSQL integration and Docker) must pass before merging exclusively into OS main. Hosted device verification and production cutover are separate, explicit gates.
