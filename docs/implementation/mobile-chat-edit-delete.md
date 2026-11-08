# Mobile chat author edits and tombstone deletion

**Scope:** PR #133, a bounded implementation of #132. Original `Lefteria-FC` and production Hostinger are unchanged.

Existing persisted group text rooms from #131 now expose versioned author-only mutations:

- `PUT /api/mobile/chat/groups/{organization}/{group}/messages/{messageId}` with JSON `{"content":"edited text","version":1}`.
- `DELETE /api/mobile/chat/groups/{organization}/{group}/messages/{messageId}?version=1`.

Both operations are restricted to the current source-verified room member who sent the message. The PostgreSQL `UPDATE` checks identity, current canonical room eligibility (including active guardian, coach, management and roster state), exact room/organization, sender identity, current version and non-deleted status **inside the same SQL statement as the mutation**. A stale but still-authorized version returns conflict; an inaccessible or deleted message returns forbidden. A client may never select another sender.

Editing stores the replacement text, an edit timestamp, and an incremented version. Deletion replaces content with an empty string and sets `is_deleted=true`, without deleting the row or its message ID. Subsequent normal room reads yield the tombstone without the old text. A restricted revision table records the message ID, actor ID, action, version and time; no historical message text is retained in that table. A security audit event is inserted in the same transaction.

The API runtime has **no direct read/write privileges** on either message or revision table; it can execute only the reviewed scoped SQL definer functions. HTTP responses remain private/no-store. The Svelte app connects the already-reviewed 550 ms long-press/••• message menu only for the sender's own non-deleted messages. Edit/delete callbacks return success only after a validated server response. The UI does not activate unimplemented reactions, named readers, private chat controls or media.

## Release and recovery

Migration `20261108230000_MobileChatAuthorMutations` expands existing message rows and replaces the read projection. Tests must prove migration apply/rollback/reapply in a disposable empty database, synthetic sender/role/grant isolation, stale version, tombstone non-disclosure and least-privilege runtime access.

**Do not downgrade a database containing real edited or deleted messages.** A down migration cannot recreate overwritten text and should be considered only for the CI fixture. In real staging/production, preserve a protected backup and use a reviewed forward migration. The original website, POS, CRM, live announcements and production infrastructure are separate deployment concerns.
