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

**Not yet implemented in this slice:** banner images, file uploads, emoji reactions, management named readership, push notifications, global multi-group distribution, scheduling, rich text or public announcements. They each need separate reviewed storage/sanitization, membership authorization, and tests. Do not imply that they already work.

Rollback of this migration drops notice data and reverts draft state, so `Down` is suitable **only for disposable CI**. Production changes require approved forward migrations, backups and explicit owner approval. The original `MikeCy-com/Lefteria-FC` repository and live Hostinger remain untouched.
