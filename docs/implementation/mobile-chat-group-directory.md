# Mobile group chat directory: canonical read-only foundation

**Status:** Implemented in this PR only after the complete five-job Rewrite CI
(including real PostgreSQL and HTTP authorization) passes. This is **not** a
chat send, edit, delete or message-history implementation.

New authenticated route: `GET /api/mobile/chat/groups`.

Allowed roles: parent, player, coach, management. Fan denied. The response has
`ready: true` and an array of allowlisted `id,name,kind,organization_id`
group rows. It has no player names, phone numbers, guardian identities,
participant lists, message bodies, read receipts or upload links.

No owner/group IDs are accepted from the client. The server binds the mobile
OTP/legacy-approved bearer session and current role to a security-definer SQL
function. The function repeats the identity's phone, role, active, expiration,
and live canonical relationship checks inside the query used to select groups:

- Current player/guardian bonds require matching verified canonical contact
  bindings and active/non-archived/non-transferred players.
- Academy membership includes **primary and plural** group relationships.
- Coaches require current bound active staff with real team/Academy
  assignments (including designated coach link validation).
- Management uses only the selected organization and its active explicit
  admin grant, not a browser-provided organization.
- Uninitialized roster returns a service error, never a fabricated empty
  list. A valid but unassigned identity may return a true empty list.

Database runtime can execute only `club.read_mobile_chat_groups(...)`; it
does not receive additional direct raw-roster, private contact or message
permissions. A fan or a revoked relationship cannot turn a group ID into a
conversation participant.

The existing `/app?tab=chat` still reports that live messaging is under
development. The directory prepares the dynamic group selection needed for
future participant-only message reads/writes, long-press actions and media;
it must not be presented as a functioning chat until those workflows pass
isolated authorization and rollback tests.

Original `MikeCy-com/Lefteria-FC` and live Hostinger production stay
unchanged. Only verified commits go to OS `main`.
