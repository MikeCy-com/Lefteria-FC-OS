# Public club profile editor

This slice adds the first CMS business write after the web-admin session slice.
It is not a production switch or a claim of hosted release. Source behavior is
original GitHub `380b4022f0ab0db95fbc52a8613711160a5037a5`,
[PUT admin club](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/server.py#L1533)
and [ClubProfile](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/models.py#L988).

Integrated after PublicSections on 2026-11-03 with new migration
`20261103094153_PublicClubProfileEditor`. The older unmerged worktree's migration
is not copied into this migration chain. Both public/site and public/sections
read common edited branding fields; sections retain imported stadium and
organization-specific social fields that this editor cannot change. Immutable
source, snapshot and evidence rows remain unchanged.

The account workspace and editor share a persistent admin navigation shell.
The editor groups identity, contact, images, history and social fields, labels
its current organization, confirms abandoning dirty edits, preserves drafts on
failed/uncertain saves and requires explicit reload after version conflicts.
Same-form section navigation preserves the draft. Only current assigned
organizations appear; API authorization remains authoritative.

Observed integration checks on 2026-11-03: Release API/tool builds had zero
warnings/errors; 45 core HTTP/PostgreSQL checks and all eight web-admin groups
passed, including real concurrent writes and authority revocation. All 31 policy
checks, both SQL privilege probes and EF model-drift checks passed. The guarded
real-cookie proxy smoke passed. The corrected OpenAPI export validates required
organization selectors, cookie security and strict safe-field schemas. The 138
frontend tests passed, Svelte reported zero errors/warnings and the production-only
package smoke passed. Browser verification on the guarded disposable database
confirmed real cookie login, account→editor navigation, a successful persisted
save, same-form dirty navigation and organization isolation; its 1280px view had
no horizontal overflow. Hosted release and a real user's login remain unverified
for this slice until the separate deployment checks are recorded.

`GET /api/admin/club?organization_id=...` requires current `settings:read` and
the explicit known organization grant. `PUT` requires `settings:write`, the
same grant and the existing same-origin/custom-header/JSON boundary. The query
accepts FC or Academy only; it has no implicit or global organization. Current
source manager and super-admin roles qualify; editor/coach/marketing/match-day
roles do not. The bounded editor edits an existing imported club only: it never
creates a default profile as a side effect of a read.

GET/PUT return `AdminClubProfileDto {version:int64,club:PublicClub}`. The initial
version is 0. PUT accepts `AdminClubUpdateCommand {expected_version,club}` with a
strict numeric nonnegative version and only editable public fields. The command
and nested club reject unknown fields and duplicate JSON keys. ID/organization
are response-only and remain bound to the imported profile.

Editable fields are name/Greek name, founded year, description/motto/history,
logo/stadium image, city/country/address, public email/phone/website and the five
known social links. Name must be nonblank. Existing public projection bounds
apply: 200-character name/Greek name/city; 1800–2200 founded year; 20,000 description;
1,000 motto; 100,000 history; 100 country; 1,000 address; 254 email; 40 phone; 2,048 URL.
Optional null clears a field. Website/social URLs require HTTPS without userinfo;
images use the established public image policy. No HTML execution, private
uploads, provider credentials, bank details or fees enter the command/response.
PUT club alone permits a bounded 1 MiB body, including streamed requests; auth
routes retain 16 KiB. Invalid fields receive 422 `invalid_club_profile` without
echoing submitted values.

Saving immediately changes the effective public club; there is no new draft,
delete or publication state. `public_content.edits` stores the safe full payload
under a restrictive FK to its immutable imported `(kind,organization_id,id)`.
Optimistic version comparison makes concurrent saves have one winner; stale
versions receive 409 `club_version_conflict` and require an explicit reload.
Initial base/source/manifest/counts are untouched, so readiness and exact initial
import replay continue to reconcile. Public reads merge the safe override in
their existing repeatable-read snapshot; public DTOs do not expose editor version.

Current authority is locked and rechecked in the write transaction; success audit
and edit are atomic. See [ADR0006](../architecture/architecture-decisions/0006-club-editor-authority-lock.md)
for the single lock-only definer exception and restricted grants. Migration
`PublicClubEditor` permits empty Down/Up but rejects populated Down with
`club_profile_edits_require_forward_recovery`. Recovery must preserve edits and
imported evidence through a reviewed forward change.
An older application image that predates effective overrides would display the
initial base instead of later edits. Preserve the database and prefer forward
recovery or an explicitly compatible application image when recovering a release.

Other errors: 400 `invalid_organization`; 401 expired/invalid session; 403 role or
organization denied; 404 `club_profile_not_found`; 503 uninitialized/unavailable
public content or bounded database contention. Existing CSRF, content-type and
body-limit errors remain. No owner connection is registered in the API, and the
mobile/public role still cannot select administrator password hashes.

The new fictional HTTP/PostgreSQL check covers full-size Greek history, public
visibility and organization isolation, exact schema/URL validation, stale and
concurrent versions, atomic audit failure, role/grant/credential revocation,
authority changes while blocked on a real row lock, replay preservation and
runtime privilege denial. Migration and constraint checks run in rollback-only
transactions against the exact disposable loopback fixture.

Historical worktree verification on 2026-11-01:

- Release API/integration-test build: zero warnings/errors; EF reported no
  pending model changes.
- Complete disposable regression: 44 core HTTP/PostgreSQL groups and eight
  web-admin/club groups passed before the final authority-lock review fix.
- The new coordinated grant deletion/reinsertion regression failed the club
  group against the prior local function. With the same tests/API and only the
  guarded function definition corrected, all eight groups passed. The function
  refuses a grant tuple skipped after a concurrent delete, even when a new grant
  with the same key currently exists. Subsequent revocation leaves the edit
  version unchanged.
- Both actual rolled-back mobile/admin SQL privilege probes passed after the
  lock fix, including the exact definer body/owner/ACL boundary. Migration
  recovery and identity/version/FK negatives passed within the focused group.
- Isolated OpenAPI export, liveness200/unavailable-database503 assertions and
  generated TypeScript contract passed after the fix. Club version/year input
  metadata is numeric; explicit organization selectors are required known IDs.

These are fictional disposable/loopback checks, not hosted editor acceptance.
The local dependency restore used the existing locked cache with NuGet audit
disabled after its network endpoint was inaccessible; no dependency was added.
Fresh network vulnerability audit, final CI and hosted editor release evidence
belong to the coordinator's release record.
