# Verified-recipient decline — safe write-only transition

**Source** `MikeCy-com/Lefteria-FC` remains read-only. This change is to `MikeCy-com/Lefteria-FC-OS` only; no production execution.

- Protected `POST /api/mobile/family/invitations/{invitationId}/decline` is restricted to an active, OTP-verified **fan** session of the exact normalized target phone.
- A PostgreSQL `SECURITY DEFINER` function revalidates current fan identity, session expiry, original active parent, live canonical guardian relationship, current nonarchived/nontransferred child and pending/unexpired exact invitation ID. It holds share locks on source authority and an update lock on the reserved slot, preventing a racing parent cancellation/reissue from being silently declined.
- The only state transition is `pending → declined` with a version increment. A matching append-only family invitation event and security audit row are created transactionally. On replay/foreign phone/other role, an unauthorized or generic not-found response is returned and **no audit success or player grant is created**.
- The application DB role can invoke only the guarded function; direct SELECT/UPDATE/DELETE of the phone-bearing invitation table and events remains forbidden.
- Actual HTTP integration uses synthetic OTP-verified target sessions and checks wrong roles, other phone, nonexistent and replayed invitation IDs, completed removal from parent and fan pending lists, exact durable status, single event/audit row and unchanged absence of a target player identity.
- No notification or SMS is sent by this mutation.

**Not implemented:** acceptance, player identity provisioning, parent/customer delivery, active-grant revocation, visual fan decline controls (next PR), or any route that exposes child PII.

Full five-job CI is required before merge exclusively to OS main. Production cutover or VPS migration requires separate approval.
