# Mobile group chat: live text read/send slice

**Repository:** MikeCy-com/Lefteria-FC-OS only. **Status:** draft PR #131; not deployable or merged before all five Rewrite CI jobs pass.

## Data and authorization

- Existing `GET /api/mobile/chat/groups` remains the **single source of truth** for canonical FC team and Academy group membership. It checks current mobile role, phone, active identity, canonical parent/player/coach links, selected management organization, and current assignment status.
- `GET /api/mobile/chat/groups/{organization}/{group}/messages?offset=0` returns up to 50 newest persisted text messages in a *currently authorized* room; no cross-room search, private participants, or public discovery.
- `POST /api/mobile/chat/groups/{organization}/{group}/messages` accepts a JSON `{"content":"..."}` body with trimmed, single-line text no more than 10,000 characters. It returns the actual saved message and writes a same-transaction audit record; no optimistic delivery/read claim.
- Both operations re-evaluate current canonical membership **inside the SQL statement that reads or writes message data**, via the already-reviewed `club.read_mobile_chat_groups` definer. Client-supplied room IDs and any mobile profile group display state grant nothing.
- Fan accounts do not access member group chat. A management account reads only groups of its *currently selected* organization. The frontend disables these routes for fan but the DB authorization remains authoritative.
- The restricted application PostgreSQL role has **no table or column privileges** on `club.app_chat_messages`. It may execute only the reviewed scoped definer functions.
- No browser persistence of message bodies, no client-generated successful-send states, no public URLs, and private responses remain `Cache-Control: private, no-store`.

## Migration, recovery and tests

Migration `20261108220000_MobileChatMessageStore` adds `club.app_chat_messages`, an indexed scope and two guarded functions. Its down migration drops the functions and table; **rolling back after genuine messages exist discards data**, so only perform down in the disposable CI database. Staging/production rollback requires a protected chat-table backup and forward-only recovery reviewed in advance. The original live system is never migrated by this PR.

Automated checks assert denial for anonymous/fan/cross-team attempts, persistence and correct sender IDs, selected management organization changes, input size/offset limits, and restricted runtime grants. Fixture-only rows are deleted before synthetic account teardown; no production data is used. In addition to these tests, five-job Rewrite CI, an isolated migrations apply/rollback/reapply pass, OpenAPI contract generation, and a mobile smoke review are mandatory before merge.

## Deliberately not enabled

This is **group text send and read only**, not all messaging. Private chat creation, sender edit/delete, per-user history hiding, emoji reactions, 550 ms action controls, named seen-by receipts, protected uploads and announcement publication remain disabled until their own persisted server authorization checks and tests exist. The existing PR #129 controls stay reusable but are not bound to unsupported actions. The app menu remains distinct from public website news, CRM, POS and other admin areas.
