# Verified mobile chat read acknowledgments and management-only readers

## Scope
A bounded, private FC/Academy group-chat extension of issue #132. Only the rewrite repository `MikeCy-com/Lefteria-FC-OS` is changed. Production Hostinger and original `Lefteria-FC` are not modified.

The user-facing **Seen by / Διαβάστηκε από** action is available only in the mobile app to the current **management sender of that exact non-deleted group message**. Other participants can never query reader names; membership of the reader remains checked at **request time**, not merely when the original receipt was recorded. Fans and public-website paths do not gain chat or receipt permissions.

## Explicit acknowledgment
An authenticated group participant uses `POST /api/mobile/chat/groups/{organization}/{group}/messages/{messageId}/seen` with JSON `{"version":1}` for the exact displayed message version. An idempotent, version-bound receipt is created for that actor and exact message content only; the client cannot submit a different reader identity. Own messages, deleted messages, nonmembers, revoked guardians and guessed rooms are forbidden. The PostgreSQL message row is locked before the write and authorization is re-evaluated within the final INSERT. This serializes against the author deleting the same message. Nothing about simply requesting a list, loading an unread group, being offline, or reacting to a message creates a receipt.

In the Svelte app, a separate version-specific acknowledgment is issued only after a non-owned message reaches **75% viewport intersection in a visible, focused document**. The callback is not triggered by a bare API GET, server rendering, an unfocused background tab or an off-screen message. The client never displays speculative read status. Expired identity or scope changes are independently rejected by the server.

## Protected reader detail
`GET /api/mobile/chat/groups/{organization}/{group}/messages/{messageId}/readers` returns only `{"readers":[{"id":"...","name":"..."}]}` to the authenticated management user who originally sent the message, is still permitted in the exact room and currently has that organization selected. The list includes only recipients who explicitly acknowledged **and currently pass the same canonical roster directory** (active identities/links/coach assignments/management org selection); withdrawn members disappear from results, even if their prior acknowledgment remains stored. A 150-result hard limit prevents uncontrolled name enumeration. Ordinary group page reads never include reader names, IDs or acknowledgment status.

The restricted runtime receives only EXECUTE privileges on two guarded definer functions. It has no direct access to `club.app_chat_read_receipts` and no access to the internal current-reader checker. An edit clears older-version receipts and stale acknowledgment requests are refused. A deletion removes all receipt rows in the same transaction as the message tombstone, while avoiding historical user/contact lists. HTTP and PostgreSQL fixture tests cover duplicate acknowledgment, author restrictions, fan/cross-room access, management access, current-membership filtering and deletion cleanup.

## Deployment, rollback, remaining work
Migration `20261108250000_MobileChatReadReceipts` creates the private receipt table and functions, extends tombstone cleanup, and must pass isolated PostgreSQL apply/rollback/reapply plus all five Rewrite CI gates. Down migrations drop recorded receipts and are only safe on disposable CI environments; production needs protected backup and reviewed forward migrations.

This does **not** implement private 1:1 conversations, photo/file uploads, announcement publishing or web admin controls. Those remain independent modules with stricter permissions for children and guardians.
