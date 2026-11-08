# App-only, consented private 1:1 messaging

**Branch scope:** bounded continuation of #132 in `MikeCy-com/Lefteria-FC-OS`, independent of the public website, web-admin controls and group chat. Neither the original Lefteria-FC repository nor live Hostinger production may be modified.

## Safeguards

- **Private messaging is disabled for all `player` and `fan` identities.** The current importer does not prove a player account holder is 18+, so the app will not silently treat First Team players as adults. This is a deliberately conservative initial rollout, not a claim that eligible adult players can never use messaging.
- Only **currently eligible parents, coaches and management users** in the exact same canonical team or Academy group can discover one another. Parent-to-parent direct contact is not offered. All controls are independent of source contact/phone details and player name lists; only `id`, `name`, `role` for an eligible contact are exposed.
- One participant **requests** a private chat. The other participant must explicitly **accept** before text messages can be sent/read. A declined invitation never creates an active chat; the server enforces a 24-hour cooldown before a new request after a decline.
- Every read and write checks the active JWT-confirmed actor **and** the other participant's current canonical group membership. Cross-organization, deleted/stale messages, revoked guardians, removed coaches, unselected management organizations, guessed UUIDs and pending conversations fail closed.
- Private messages and invitation rows live in **separate PostgreSQL tables** with no direct read/write privileges for the application runtime, no public routes, and bounded pages (50 messages, 100 contacts/inbox items). Every successful invitation, response, mutation or hide writes a scoped security audit event.
- Only the original author can edit or soft-delete their message. Mutations require a current `version` and record a minimal immutable revision event. Deleted text is cleared and replaced with a tombstone.
- **Delete chat for me** advances only the requesting account's `hidden_before` cursor, not the peer's history or the shared conversation state. Older text never resurfaces for that viewer; a new post-hide message may reopen their chat with only the new messages. This is not mutual deletion.

## App API

All routes require the authenticated Mobile identity bearer policy, are no-store, and are only allowed by the explicit private edge route list:

| Method | App-only route | Purpose |
|---|---|---|
| GET | `/api/mobile/private/contacts?organization=…&group=…` | Adult-role participants in a selected currently joined group |
| GET | `/api/mobile/private/conversations` | Current invitations and consented inbox |
| POST | `/api/mobile/private/invitations` | Request from a valid contact; cannot force acceptance |
| POST | `/api/mobile/private/conversations/{id}/respond` | Recipient accept/decline |
| GET | `/api/mobile/private/conversations/{id}/messages?offset=0` | Current approved conversation page, respecting viewer hide cursor |
| POST | `/api/mobile/private/conversations/{id}/messages` | Send text only after acceptance |
| PUT | `/api/mobile/private/conversations/{id}/messages/{messageId}` | Author-only revisioned edit |
| DELETE | `/api/mobile/private/conversations/{id}/messages/{messageId}?version=N` | Author-only soft delete |
| DELETE | `/api/mobile/private/conversations/{id}/for-me` | Hide only this viewer's history |

The app places group chat and private chat under **different labeled panels** within the app's Messages tab. The original 550 ms hold menu is reused for private author edit/delete and conversation hide-for-me. Server confirmations, not optimistic frontend state, determine success. No local storage persists chat text.

## Verification / rollback

Before merge, verify all five Rewrite jobs: ASP.NET Core + policy, SvelteKit type/SSR/UI tests, generated OpenAPI + TypeScript contracts, isolated PostgreSQL migration apply/rollback/reapply with least-privilege and real HTTP requests, and Docker Compose preview security. This includes declining invitations, duplicate request guards, player/fan denial, current membership revocation, version conflicts, soft deletion and unilateral hide.

The down migration permanently drops private conversations and messages and is **only safe for disposable CI databases**. Any live rollback requires explicit operator approval, backup and reviewed forward migration.

Secure media attachments, reactions/read receipts inside private conversations, admin communications tooling, invitation reporting and Academy announcements are **future separately gated slices**. This initial feature does not grant direct private child access, notification visibility, phone lookup or new public website functionality.
