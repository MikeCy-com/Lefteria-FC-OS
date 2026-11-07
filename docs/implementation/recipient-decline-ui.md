# Recipient invitation decline — fan profile

The original Lefteria-FC repository is read-only. This feature belongs only to Lefteria-FC-OS.

- Fan Profile uses existing OTP-verified, current-identity recipient inbox and a two-step decline confirmation.
- On confirmation, the frontend POSTs the exact currently listed invitation ID to the already-audited and phone-bound `/api/mobile/family/invitations/{invitationId}/decline` endpoint (merged in PR #41). The PostgreSQL function rechecks the current fan phone, active guardian link, pending slot and expiry independently. The browser's ID never grants access.
- On success, the declined record disappears from the in-memory inbox. Failure retains the item and displays an error. A changed/stale invitation clears the inbox and prompts a fresh read.
- The feature rejects unrelated IDs, non-fan roles, expired records and logout/role-revision races. It exposes no child/guardian identifiers or phone numbers.
- No SMS or invitations are delivered; acceptance and player-role activation remain unavailable.
- Tests cover a real Svelte SSR component, route role guard and controller using synthetic tokens/identities.

A full five-job CI pass is required before merge to OS main; no production deployment.
