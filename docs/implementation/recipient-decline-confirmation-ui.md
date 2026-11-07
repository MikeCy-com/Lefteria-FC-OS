# Recipient decline confirmation — Fan Profile

This SvelteKit change follows [PR #41](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/41). It changes **only Lefteria-FC-OS**. The original Lefteria-FC repo and production VPS are unchanged.

- Verified Fan → Profile offers an explicit two-step "Απόρριψη καταχώρισης" then "Ναι, απόρριψη" for an existing **current pending** reservation.
- The session-scoped controller confirms the current fan role and exact invitation ID appeared in its own authenticated inbox before posting to `/api/mobile/family/invitations/{invitationId}/decline`. The .NET backend and PostgreSQL function independently recheck phone ownership, expiry, parent guardian authority and current player status.
- UI respects processing state and server confirmation. On success the declined reservation is removed from the live pending list; on 429 or unsuccessful response it remains and an error is shown. If the reservation has changed (404/403), the private list is cleared and refresh is required. Responses arriving after logout/role change cannot restore data.
- There is no accept/approve button, no player identity activation, no SMS notification, and no child/guardian PII in the fan component. A declined reservation is audit-logged server-side but its historical events are not returned to the client.
- Synthetic controller and real Svelte SSR tests cover invalid IDs, foreign role, replay, success, 429/404, logged-out races, accessible two-step confirmation, and absence of acceptance controls.

Remaining: OTP-confirmed player-role acceptance and independent provisioning/revocation with consent safeguards, delivery and original UI parity. This PR is **not** production deployment. Full five-job GitHub CI is required to merge only into OS main.
