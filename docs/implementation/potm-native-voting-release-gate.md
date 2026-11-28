# Native Player of the Month release gate

Tracking: #485. Public results and roster browsing are separate from the native ballot ledger.

## This database slice

- One current private ballot per Fan identity and Cyprus calendar month. Fan identity is the existing unique normalized-phone/role UUID; formal-role sessions cannot vote. No phone or public voter identifier is stored in this ledger.
- The owner-only period record identifies an explicitly reviewed import snapshot, active club Super Admin approval reference, and attested legacy voting stop time no later than the start of the period. There are no enabled periods seeded by migration.
- Database functions lock the exact active Fan identity, recheck expiry, use the authoritative Cyprus month, pin the approved current public snapshot and check its document/source counts. A changed snapshot or retained current-month legacy vote closes the gate. Malformed retained vote months also close it; this conservative rule requires owner reconciliation rather than silently dropping history.
- Candidate must be a currently published first-team player and active, not archived/transferred, in the canonical club roster. A stale public card cannot override canonical inactivity.
- Writes use expected revision, atomically update one ballot and append a separate immutable-by-runtime event. Withdrawal leaves a tombstone; repeating the same choice at the current revision is a no-op. A stale revision must be refreshed, not blindly replayed.
- The runtime can execute only the scoped read/write routines, not the private tables, helper functions, or opening gate. Signed mobile session claims must come from the API authentication middleware; SQL function parameters are not an authentication mechanism for arbitrary direct clients.
- History is preserved by refusing destructive down migration if any period/ballot/event exists.

## Authenticated API slice

- GET /api/mobile/potm/ballot reads only the current signed Fan's own selection, current Cyprus month, revision and voting_open flag.
- PUT /api/mobile/potm/ballot accepts exactly month_key, expected_revision and player_id (explicit null withdraws). Unknown, duplicate, missing or wrong-type fields are rejected, and bodies are bounded to 4096 bytes including chunked input.
- Both endpoints require the existing mobile authentication policy; the store supplies signed identity/phone/session expiry to the scoped SQL procedure. No organization, voter identity, phone, role or arbitrary month selector is accepted from the client. Responses are private/no-store.
- Closed periods or stale revisions return 409; invalid candidate/month at the persistence layer returns 422; unavailable storage returns 503. No automatic retry of mutations or fake success.
- HTTP integration tests exercise closed-by-default gate, OTP login, same-phone role switch, expired/mismatched/revoked identities, malformed and oversized inputs and denied-write persistence. These complement the earlier verified database lifecycle tests; open-period HTTP and actual concurrent request testing remain separate release gates.
- OpenAPI and client type declarations are supplied and must match the CI-generated artifacts before merge.

## Mobile ballot interface

- Verified Fan accounts navigate to the personal ballot inside the app; guests retain public results and can explicitly sign in to resume the ballot tab. Other roles cannot mount it.
- Read current own ballot first. Only if the server says voting is open, fetch the canonical active first-team picker and the public club projection. The public request carries no credentials; display only public candidate names/images for IDs present in both verified responses and in the current Cyprus month. The SQL write remains authoritative if roster or publication changes.
- Selection does not mutate data until explicit confirmation. Changes and withdrawals send the prior revision. If the response is rejected or lost, remove the stale ballot controls and require a fresh GET; do not automatically retry. Display success only on exact confirmed response.
- Added parser/controller tests and an actual browser route exercise using synthetic API responses. This is not a real open-period server or concurrency test. Staging and owner cutover remain closed.

## Outstanding release gates

1. Mandatory disposable PostgreSQL lifecycle and restricted-role checks, including explicit concurrent sessions to prove same-revision conflict.
2. Authenticated API with strict Fan JWT claims, bounded payload, validation/error mapping, no-store, generated contracts; tests for expired/revoked/forged role, same phone and independent accounts. No raw identity or phone parameters exposed to client.
3. Mobile Fan ballot screen with own current selection, vote/change/withdraw confirmation, accessible error/retry and conflict refresh, correct month handling. Guest may read results only.
4. Owner review of legacy vote history, completeness and stop time; no automatic enablement and no deletion or combining of legacy aggregates with native ballots. For months already containing legacy votes, the current gate deliberately refuses writes until a separate reviewed reconciliation policy is implemented. An import that changes the pinned snapshot must be reviewed before reopening.
5. Native public tally projection with explicit source-of-truth per month; no voter rows, no double count, no winner declaration before the defined close. Until then the public page remains imported-results-only.
6. Verify real staging migration, least-privilege grants, API/mobile flows and data persistence using synthetic accounts in an isolated environment. Production activation requires explicit owner authorization.

No production migration, period opening, deployment or provider operation is authorized by this document.
