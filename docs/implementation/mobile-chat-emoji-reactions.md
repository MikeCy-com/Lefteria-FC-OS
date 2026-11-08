# Scoped mobile group emoji reactions

**Scope:** next bounded slice of #132, following merged PR #133. The original Lefteria-FC repository and live Hostinger production must remain untouched.

## Behavior and privacy

The existing 550 ms long-press/••• menu permits **current authorized group participants** (not just original senders) to react to live FC team and Academy messages. The author-only edit/delete actions remain restricted to the original sender.

The reaction endpoint is `POST /api/mobile/chat/groups/{organization}/{group}/messages/{messageId}/reactions` with JSON `{"emoji":"⚽","active":true}`. `active:true` adds and `active:false` removes a user's reaction; repeated identical requests are idempotent. The allowlist is exactly 👍 ❤️ 😂 ⚽ 👏 🎉 🙏 🔥. No arbitrary text, stickers, named reactors, or public API aliases are accepted.

The persisted primary key is (message ID, authenticated identity ID, emoji). The server computes a per-emoji `count` and a personalized `mine` flag for the **currently authorized requester**, and returns only those aggregates with normal room reads and confirmed mutations. It does not expose the identities or names of reacting parents, children or coaches. Deleted-message tombstones have no reactions.

Every change re-resolves the caller's current canonical room access in PostgreSQL. The message row is locked before the idempotent write, serializing changes against author deletion; the final insert/delete also checks current membership. All changes use parameterized SQL and same-transaction success audits. The restricted runtime cannot SELECT or mutate the raw reaction table and cannot use any public website endpoint. Fan, expired, transferred, archived, wrong-organization and revoked identities must be denied.

The Svelte app displays verified reaction chips, their counts and whether the current viewer reacted. Selecting a chip or emoji in the long-press menu submits an explicit desired reaction state. No optimistic counts or read receipts are fabricated.

## Release gate and recovery

Migration `20261108240000_MobileChatEmojiReactions` creates the private table and group-scoped reaction function, then adds aggregate responses to existing group messages. Verify migration apply/rollback/reapply, runtime least-privilege probe, cross-group/fan/invalid input checks, edit and delete interactions, OpenAPI generator, edge allowlist, SvelteKit build and disposable Docker smoke before merge.

The **down migration drops stored reactions**; use it only in disposable tests. A live rollback needs protected retention and an operator-reviewed forward migration. Reactions are distinct from future *seen by* receipts, private conversations, attachments and announcements; none of those features become enabled by this slice.
