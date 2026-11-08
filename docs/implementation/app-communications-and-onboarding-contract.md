# App communications, attachments and onboarding — implementation contract

**Source of truth:** original `MikeCy-com/Lefteria-FC` (READ ONLY), especially
`frontend/src/mobile/pages/ChatPage.jsx`, `frontend/src/mobile/components/ChatPressMenu.jsx`,
`backend/routes/chat_moderation.py`, `backend/routes/communication_media.py`,
`backend/chat_access.py` and `backend/announcements.py`.
**Target:** `MikeCy-com/Lefteria-FC-OS` (ASP.NET Core / PostgreSQL / SvelteKit).
**Deployment:** never mutate original repository or live Hostinger production.

## Distinct ownership and navigation

- **Public website:** public page CMS, news, sponsors, media, public social.
- **App communications:** member-only conversations, read receipts, reactions,
  attachment access, team and Academy announcements, notifications.
- **CRM:** contacts, guardian registrations and leads, **not** the app message
  store or the website publishing system.
- **Admin onboarding:** role-specific operator guide; **app onboarding:**
  mobile identity and role guide. Version both flows when new features become
  safely available. A user checking off a guide never grants permissions.

Do not publish a feature as live merely because a menu, endpoint shape or
placeholder exists. A UI state of `planned` must remain visibly disabled.

## Mobile in-app message UX and authorization

1. Hold a message for **550 ms**, show a small dark/gold context menu with
   `Edit`, `Delete`, `React` and, **only for authorized staff viewing a
   message they sent**, `Seen by`. The original implementation also supports
   right-click and a visible `…` fallback. Support Escape, focus return,
   keyboard/touch and pointer-move cancellation while scrolling.
2. Allow the sender to edit/delete **their own** active message after
   rechecking the current role, organization, exact conversation participation
   and reply rights. Privileged moderation from web admin uses a separate
   audited policy; role labels in a client are never authority. Editing should
   version the content, show an edited marker and avoid mutating the attachment
   through text edits. Deletion is a tombstone visible to other participants
   and access to no-longer-linked private attachments must be revoked.
3. The participant who starts or opens a **private chat** can delete its
   entire history **for their own account**; the original implementation
   stores `deleted_before` in per-user `chat_visibility` and may display
   it again after a new message. It must **not** delete another participant's
   records. Deleting a group chat for self must not delete the team/Academy.
4. The message composer has a compact emoji picker, a 10 MiB limit for
   allowed image and document attachments, validation by file contents as
   well as extension, protected storage keys and downloadable objects only
   after fresh participant checks. Do not trust a public URL supplied by
   the client; bind uploaded IDs to the exact authorized conversation, actor,
   MIME, and file size. No direct object-store URLs for youth private files.
5. Reactions are one reaction per (message, current identity, emoji) with
   idempotent toggle/replace semantics, bounded allowed emoji choices and
   per-message aggregate counts; notification actions are idempotent.
   Removed messages must no longer accept reactions.
6. Only acknowledge a message as seen if actually displayed in a foreground
   conversation to a current participant. Never infer seen from push delivery,
   a badge or an unread counter. A manager/administrator may request the
   **named** seen-by list for **a message they sent and for which they retain
   conversation moderation/owner authority**. Ordinary participants may see
   only limited aggregate status; no read list for arbitrary third-party
   messages. Current grants and the message's scope are revalidated on every
   read. Redact revoked identities; limit and paginate names; protect minors.
7. Mobile bearer identity and admin web cookie are completely separate
   authentication realms. All mutations need ownership checks, audit events,
   current per-group membership, stable versions and safe conflict results.
   Never allow a broader admin inbox to silently read private family messages.

## Team and Academy announcements

- Announcements are **in-app** communications, not news articles or public
  CMS publishing. The composer lives under App communications in web admin.
- Target a verified FC team or Academy group, optional role-visibility set
  `parent`, `player`, `coach`, `management` and deliberately published
  `fan` where appropriate. A fan never acquires private youth membership
  solely from following a club. Clarify whether an item is member-scoped or
  intentionally public before exposing it to fan accounts.
- Use dynamically resolved, deduplicated current roster links: primary and
  plural Academy groups, team memberships, current guardian/child links,
  assigned coaches and organization-level management grants. A change in
  membership or revocation changes visibility immediately; no stale static
  recipient arrays.
- Store an optional banner image plus protected attachment IDs. Validate all
  files, cap size and pixels, enforce content-security/attachment headers,
  and require fresh announcement visibility on every download.
- Admin/manager can create, edit, publish, unpublish and **delete only in the
  web admin backend**; the mobile app renders announcements and reactions
  but has no announcement delete action. A mobile reader may react where
  policy allows.
- On explicit publish, atomically write a versioned event, audit event and
  idempotent delivery outbox. A worker retries with a dead-letter path and
  rate limits. No background sends directly from a request.
- Admin may view **Seen by: [names]** after scope reauthorization, limited to
  actual readers and minimally necessary profile display fields. Never infer
  read from push or send. Stop exposing identities immediately on grant
  revocation; return `503` instead of fictitious zero recipients if roster
  linkage is uninitialized.
- Keep fan-visible public news and member-only announcements as **different
  records, permissions and UI routes**. An admin can explicitly choose to
  issue a separate fan-visible notice, not reuse a private team post.

## Onboarding

- Mobile: guide parent, player, coach, management and fan identities separately;
  the guide begins with **own-role profile** editing. The existing authenticated
  `PUT /api/mobile/profile` supports all roles, so a parent must not switch
  to fan merely to edit their own identity. Avatar is optional; current
  profile form requires name, surname and birthday. Never ask a guardian to
  enter a child's personal details in a fan profile.
- Web admin: guide super admin, manager, editor, coach and other current roles
  using the actual assigned organizations and permissions. Navigation/help
  is not authorization, and unavailable functionality stays `planned`.
  Profile review is available; a working admin profile update endpoint is
  **not yet implemented**, and the guide must say so.
- Guide completion must distinguish a verified saved profile from the user's
  locally checked tutorial steps. An update to a relevant feature changes
  `ONBOARDING_VERSION`, so outdated completion is not silently reused.
  Local walkthrough markers are UX only; a server-side progress record
  is a future enhancement if cross-device continuation is required.
- Check Turkish/Greek/Cyprus font and mobile 44 px target sizing, dynamic
  role changes, inaccessible/revoked organizations and reduced motion.

## Delivery order & CI

1. **Foundation:** separate app admin section, versioned role onboarding,
   correct own-role profile flow. Does not claim chat/announcements published.
2. **Messaging:** scoped conversation/directory read with authorized mobile
   participation, then create/send/edit/delete/personal-history/tombstones.
3. **Message interactions:** long-press menu, emoji, uploads and reactions,
   followed by named seen-by only for authorized own sent messages.
4. **Announcements:** publish lifecycle, banner/files, mobile reads/reactions,
   named staff receipt lists and admin-only deletion.
5. **Notifications:** idempotent outbox, app unread badge and audited retries.
6. Extend both guides as real functionality moves from planned to ready.

Every slice must pass the five Rewrite jobs, including disposable PostgreSQL
HTTP authorization, attachment isolation, concurrency and rollback tests.
Only verified PRs merge to Lefteria-FC-OS `main`. No original/live writes.
