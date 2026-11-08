# App-only club and Academy announcements

This slice extends the already verified **web-admin Communications / Announcements draft editor**, without merging its features into either public website news or mobile group/private messaging.

## Management workflow

1. A manager/super-admin with an active web-admin cookie, permission (`teams:write` / `academy:write`) and explicit organization grant opens **Admin → Communications → Announcements**.
2. They save a title and body against a **canonical team or Academy group ID**. Drafts remain invisible to the app.
3. They select **Publish to app** and explicitly confirm the action. The back end repeats admin cookie, grant, role, draft version, organization, and *current real roster group* checks **within a single PostgreSQL transaction**. The published copy becomes visible only to current app members of that exact group. An audit event must commit, otherwise no notice is published.
4. A manager may remove a published notice from the **web admin backend only**, with a current version and confirmation. Removed notices disappear from app results while retaining audited database provenance. No mobile deletion endpoint is exposed.

No email, SMS, push or public website article is created by publishing in this slice.

## App reader contract

The app has a **separate Announcements tab** (`/app?tab=announcements`). It resolves the current authenticated user’s canonical FC/Academy groups and reads `GET /api/mobile/app/announcements/{organization}/{group}?offset=N` with **Mobile Bearer** authorization. Only 50 published, nonremoved announcements are returned per page. No fan access, draft access, raw roster, phone list, author identity or personal receipts.

The database repeats selected-organization, active-identity and roster membership checks for every query; a revoked guardian or removed coach cannot continue reading a previous group. Management's selected organization matters. Both `club.app_announcements` and `club.admin_announcement_drafts` are denied to the restricted mobile DB role; the admin role has no raw table access and may only call scoped definer routines.

## Verification and limitations

The development CI must confirm five jobs: ASP.NET Core API/policy, SvelteKit app and admin frontend, generated OpenAPI + typed client/startup smoke, disposable PostgreSQL migrations (up/down/up) and privilege/HTTP tests, and isolated Docker Compose preview. The edge allows only the single new Mobile GET reader operation. Admin publish/list/remove routes use the **WebAdmin** policy and cookie guard; they are not member app APIs.

**Reaction follow-on slice (migration 20261108280000):** Published app-only announcements can receive eight allowlisted emoji reactions from members with *current* canonical group access, including roster-backed players, coaches, parents and selected-organization managers. Reactions have server-confirmed per-person `mine` flags and aggregate counts; private reactor identities are never returned. The API repeats roster checks and locks the exact active notice before add/remove. Notice removal blocks future reactions; only the web-admin backend can publish/remove the underlying announcement. A separate private reaction table, idempotent writes, audit events and a limited Mobile Bearer POST endpoint do not expose edit, deletion or management readership privileges.

**Named readership follow-on slice (migration 20261108290000):** A card visible at least 75% while the app window has focus and the page is visible may send an authenticated, explicit read acknowledgement. Fetching an announcement alone never marks it seen. Receipts are idempotent and persist per verified current member in a private table. The web admin panel has a **Διαβάστηκε από** dialog that requests only currently authorized readers' names after a click. The API rechecks the management session, organization grant, published active notice and each reader's current canonical group membership; names are never returned by the mobile feed, and withdrawn notices do not expose their readership. Raw receipt table access remains denied to all runtime roles.

**Private media follow-on slice (migration 20261108300000):** Managers may attach one banner and up to three PDF files to an unpublished draft in the Communications admin panel. The banner is re-decoded using the already pinned Skia codec and stored as a metadata-stripped JPEG (maximum 1 MiB); images larger than 3 MiB raw are rejected. PDF uploads are bounded to 2 MiB with conservative framing checks and ASCII filenames. Draft media mutations are versioned and audited; they are never allowed after publication. A transaction retains media with the published notice and marks it unreachable as soon as that notice is withdrawn.

Mobile announcement listings include **metadata only** (opaque IDs, size, kind and display name); file bytes are returned separately only to the currently authorized team or Academy member by an authenticated, room-scoped Mobile Bearer endpoint. The frontend validates sizes, metadata and binary framing, uses temporary in-memory Blob URLs for banners/downloads, and revokes banner URLs on unmount. No media is published at `/api/uploads` or the public website. The direct runtime role cannot SELECT/INSERT raw bytea; all access is through an explicitly granted checked function. PDFs are **always downloaded** (not embedded or rendered inside the app). Lightweight PDF framing checks do not constitute malware scanning. Operators should not treat uploaded PDFs as antivirus-scanned.

**Not yet implemented:** push notifications, global multi-group distribution, scheduling, rich text, general-purpose file types/videos or public announcements. A managed malware scanner and a dedicated durable file/object-storage adapter are separate future work; currently small media is stored within the private PostgreSQL database and its backups. They each need separate reviewed storage/sanitization, membership authorization, and tests. Do not imply that they already work.

Rollback of this migration drops notice data and reverts draft state, so `Down` is suitable **only for disposable CI**. Production changes require approved forward migrations, backups and explicit owner approval. The original `MikeCy-com/Lefteria-FC` repository and live Hostinger remain untouched.
