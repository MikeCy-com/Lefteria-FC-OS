# ADR 0006: Lock current administrator authority for club edits

Status: integrated after PublicSections in migration `20261103094153_PublicClubProfileEditor`; pending hosted release verification.

The original `PUT /api/admin/club` requires `settings:write` and one current
authorized organization. Its `ClubProfile` model also contains provider secrets,
banking and unrelated settings. The rewrite editor accepts only safe public club
fields and preserves the imported original privately. Source:
[original handler at 380b402](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/server.py#L1533).

An authentication middleware read is not sufficient for a business write: a
role, credential, session or organization grant can change before that write.
The write must hold current authority stable while validating and committing
its safe edit and audit. PostgreSQL row-locking SELECT requires UPDATE privilege
on at least one column; granting direct updates to administrator account/grant
columns would broaden the restricted administrator role unnecessarily.
[PostgreSQL SELECT privileges](https://www.postgresql.org/docs/18/sql-select.html).

Use one static, owner-created SECURITY DEFINER function,
`identity.lock_admin_club_authority(text,text)`. It validates the token digest
shape and known organization, then takes shared locks in account/session/grant
order. It returns void, exposes no hashes/data and performs no data writes.
Its fixed search path is `pg_catalog, pg_temp`, all application relations are
schema-qualified, and no dynamic SQL or caller-controlled identifier is used.
Each actual row-locking query must find its row; a missing or concurrently
deleted/replaced tuple raises a fixed closed denial. A later grant insertion
cannot retroactively satisfy a locking query that acquired no grant row lock.
Migration creation requires the owner of the account table. PUBLIC and mobile
runtime execution are revoked. The dedicated administrator connection receives
execution of this exact signature only.

The grant/probe scripts check the function owner, language, return/argument
types, volatility, fixed search path and exact normalized body SHA-256. Only
Windows/Linux line endings are normalized. They deny every other callable
SECURITY DEFINER routine; this is a specific reviewed exception, not a general
definer capability. The administrator still cannot update accounts or grants,
read owner-only migration evidence, or use owner credentials through the API.

Inside the same transaction, the repository reloads locked session/account/grant
rows, checks expiry/revocation, active/invite/2FA state, credential HMAC and exact
current role capabilities. The final versioned write rechecks session expiry;
the held locks prevent intervening account/grant/session mutations. Edit and
audit commit together. Lock timeout is three seconds, statement timeout eight
seconds and idle-in-transaction timeout ten seconds. Deadlock, lock timeout and
serialization cancellation roll back and return a controlled 503; clients reload
explicitly instead of automatically replaying the write.

Safe edits are separate from the immutable public import base. Public runtime
can SELECT edit identity/payload columns but cannot read editor version, mutate
edits or call the lock function. Public responses retain the existing DTO.
Populated edit migration rollback refuses; forward recovery preserves both
initial evidence and later editorial content.
