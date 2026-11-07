# Parent outgoing pending invitation status — secure read-only stage

Original Lefteria-FC is **reference only**. This change belongs exclusively to Lefteria-FC-OS.

## Added

- Authenticated parent `GET /api/mobile/family/outgoing-invitations`, **separate from the original recipient-inbox path** `/api/mobile/family/invitations` that must remain reserved for later OTP-verified recipient acceptance.
- Returns `items` containing a current pending reservation's ID, linked child ID, organization, status and start/expiry times. It deliberately excludes the recipient phone, guardian phone, contact tokens, historic actions and underlying source records.
- The database function runs under a dedicated SECURITY DEFINER boundary with restricted EXECUTE permission. It validates active parent identity, current phone, real canonical guardian binding, active/nonarchived/nontransferred roster state and original reservation owner at query time. Runtime SELECT/DML on underlying invitation and event tables remains prohibited.
- Expired, cancelled and superseded records are not represented as pending. Missing roster readiness fails closed; a result above 100 rows fails closed rather than silently truncating.
- HTTP integration exercises no-auth and wrong-role denial, current/updated pending state, cancellation, revoked relationship, disabled parent, archived child, organization scope and phone redaction.
- No new authorization token, SMS, child identity, payment, database write, external provider side effect or production migration is triggered by this GET.

## Parent app integration

- The Parent → Children screen displays the current outbox status with a clearly labeled reservation, expiry date and verified child display name. It does not show a target phone number or expose raw invitation IDs in visible text.
- The SvelteKit controller loads invitations through the generated authenticated client **only after** a current family read. It rejects unknown child IDs, organization mismatches, expired/duplicate IDs, invalid statuses and malformed timestamps.
- In-memory outbox state is invalidated when guardian relationships refresh, on logout, role switch or a relevant access failure. Private data is never cached in browser storage or public pages.
- The UI explicitly states that no SMS or invitation was sent. The existing server-side cancel endpoint is not yet exposed as a new UI action.

## Not included

The parent cancellation UI, recipient inbox, OTP acceptance/decline, child grant activation and active-grant revocation remain separate implementation steps. A pending record **does not send a message or grant access**.

## Verification required

Full five-job GitHub Actions PR checks, generated OpenAPI and TS client, PostgreSQL/least-privilege tests, and Docker smoke. Merge only to Lefteria-FC-OS/main. Real VPS deployment and migration require separate user approval.
