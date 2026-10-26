# Canonical roster and mobile player-picker contract

## Source baseline and slice boundary

Read-only GitHub audit on 2026-10-26 confirmed original repository main through
the [branch API](https://api.github.com/repos/MikeCy-com/Lefteria-FC/branches/main)
at **`7833daceceac1611c368e7bc4df2a82c64b45e1a`**. All source links below pin
that revision. Its merge of PR #42 changed 15 frontend layout/navigation/test
files; the backend remains unchanged from the earlier `5111f06` audit. GitHub
main does not establish the deployed revision or the condition of live data.
No production records, credentials, providers or exports were accessed.

This slice covers canonical Team/AcademyGroup/Player persistence, an explicit
owner-operated initial snapshot import of those three collections, and the
authenticated favourite-player picker. Staff import and canonical coach
workflows remain separate work.
It does not implement the full anonymous roster, private parent/coach/management
rosters, player details, operational dashboards, roster administration or a
complete production data/account cutover. Those destinations keep their actual
implementation status; sample players, children, assignments, counts and
statistics are not substitutes for missing data.

Sections headed **Observed** describe source code. Sections headed **Target**
define the bounded implementation and acceptance requirements; they are not
claims that an import, deployment or cutover has occurred. See also the
[feature queue](feature-queue.md), [role dashboard contract](role-dashboard-contract.md),
[profile contract](profile-contract.md), and
[legacy identity migration contract](legacy-identity-migration.md).

## Observed: entities and identifiers

| Source entity | Identity and roster fields | Status and important distinctions |
| --- | --- | --- |
| `teams` / `Team` | UUID-string `id`; `name`, `level`, `description`, `banner_url`, `created_at`. | The model has no `team_type`, organization, active or archive field. Team writes add `organization_id` separately and permit first-team club context only. Academy groups are not rows in this collection. |
| `academy_groups` / `AcademyGroup` | UUID-string `id`; `name`, `age_range`, nullable `city`/`location_name`, nullable `coach_id`/`coach_name`, `training_schedule`, `description`, `max_players`, `season`, `banner_url`, `display_order`, `created_at`. | `coach_id` refers to a staff record; `coach_name` is display data. The model has no `team_id`, active or archive flag. Group writes are Academy-only and add the organization tag outside the model. |
| `players` / `Player` | UUID-string `id`; `name`, jersey `number`, `position`, `nationality`, DOB/age, `team_type`, singular `team_id`, primary `academy_group_id`/cached group name, plural `academy_group_ids`, `image_url`; own `phone` is separate from `parent_name`, `parent_phone`, `parent_email`. | `registration_number` is a separate admin membership identifier, not the UUID or jersey. `is_active`, `is_archived`, `is_transferred` are separate flags. Archive and transfer provenance, private/emergency contacts, injuries, deeper profiles and other history must not be flattened into a picker row. |
| `staff` / `Staff` | UUID-string `id`; `name`, staff job `role`, nationality/DOB, image/bio, own phone/email, `team_type`, `team_ids`, primary/plural Academy groups, qualifications and public-profile fields. | Separate `is_active`/`is_archived`, archive provenance and role-change provenance. Nullable `admin_user_id` and login summary fields describe another account; they do not grant mobile/CMS permissions. There is no staff transfer flag in this model. |

Sources: [TeamType and StaffRole](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L79),
[group and team models](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L145),
[player model](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L456),
[staff model and public model](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L632),
[organization-tagged player writes](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L1400),
[Academy writes](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L1600),
[team writes](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L1732).

`TeamType` values are exactly `First Team` and `Academy`. Player positions are
`Goalkeeper`, `Defender`, `Midfielder`, `Forward`. Staff job titles include head,
assistant, goalkeeper, fitness and youth coach, physiotherapist, team manager
and scout. A staff job title, mobile `coach`, and CMS `coach` are different
concepts. Do not create new mobile permissions from a title or an optional UI
field such as `birth_year` or availability status.

UUID defaults in source models are not evidence that every historical record
has a valid unique UUID. Likewise, source does not establish global uniqueness
for player/staff phones, jersey numbers or membership numbers. Store provenance
and resolve conflicts explicitly rather than identifying records by display
name, array position, Mongo `_id`, jersey or a first phone match.

## Observed: FC and Academy classification

Canonical organization IDs are `lefteria_fc` and `lefteria_fc_academy`.
`organization_owned_query` treats an explicit `organization_id` as authoritative.
Only missing/null tags use a legacy classifier; an empty or unknown explicit
tag does not acquire a default context.

Legacy player Academy markers are a nonempty primary/plural group assignment,
or a `team_type` matching `academy|ακαδημ` case-insensitively. Records without
those markers belong to the first-team legacy classifier. Staff classification
uses Academy-group markers and `team_type`; a team assignment alone is not
proof of an Academy assignment. New player writes separately validate that
Academy markers agree with the selected organization and that newly supplied
group IDs resolve in Academy context. Singular/plural assignment drift and
tag/marker contradictions are possible historical inputs, not permission to
choose whichever interpretation gives broader access.

Sources: [organization ownership precedence and player scope](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L145),
[staff organization classifier](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L251),
[team/group marker rules](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/team_scope.py#L6),
[group and player-write validation](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L162).

## Observed: exact mobile picker and caller

`GET /api/mobile/players-list` requires `get_mobile_user`; it is **not an
anonymous public endpoint** and has no extra fan-only role check. Its exact
source query is `is_active: true` and `team_type: "First Team"`, sorted by
`number` ascending and capped at 50. Response:

```json
{ "players": [{ "id": "source-player-id", "name": "name", "number": 1, "position": "Defender", "image_url": null }] }
```

Those five fields are the complete projection. The example only describes the
wire shape; it is not application seed data. No phone, DOB, age, registration
number, guardian information, injury, medical, finance or private profile field
belongs in this DTO.

The confirmed source caller is `FanProfileEditModal`. It fetches on mount or
header changes, renders image/name/number, and uses the player `id` as
`favourite_player_id`. Returned `position` is not displayed there. The source
silently catches list failures and begins with `[]`; that does not prove a
verified empty roster. An existing favourite absent from the list remains
stored. The source None choice becomes null in its profile POST and is then
ignored by the source profile allowlist; the rewrite's already implemented
empty-string clear avoids reproducing that bug.

Sources: [picker handler](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L1732),
[caller fetch and save](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/frontend/src/mobile/components/FanProfileEditModal.jsx#L17),
[source choices](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/frontend/src/mobile/components/FanProfileEditModal.jsx#L100),
[profile update null handling](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L1697).

## Observed: private roster relationships and source gaps

| Route/relationship | Actual source scope | Checks required before porting private data |
| --- | --- | --- |
| Parent dashboard | Exact `parent` role; stored `linked_player_id`/`linked_player_ids` load children, then their groups. At most 100 group peers per group. Own children receive full documents; peers receive the nine-field allowlist below. | Revalidate the current parent identity and canonical guardian-child relationship. A cached linked ID, registration, same name or same-phone fan role is not authority. Do not copy the source omission of current guardian/contact and roster-status checks. |
| Coach dashboard | Exact `coach` role; current staff assignments bound teams/groups and players. Teams/groups capped at 50 each; players at 500. | Require current active, unarchived canonical staff and verified identity linkage; validate each team/group organization and assignment. Source helper/dashboard omit some of these checks. Empty assignment must remain empty. |
| Management dashboard/detail | Exact dashboard role `management`; current active admin and allowed organization selection. Multiple grants without selection return selection-required and empty datasets. Up to 500 organization-owned players. Detail independently checks selected organization and returns 404 outside it. | Preserve the rewrite's explicit admin FK and fresh grants rather than source phone-first matching. Selected FC and Academy datasets remain isolated. Mobile management is not a CMS token or blanket finance/administration permission. |

The exact parent **peer** projection is `id,name,number,position,image_url,
team_id,team_type,academy_group_id,academy_group_ids`. It is distinct from the
five-field picker and from an authorized own-child detail. Source coach and
management player arrays contain full Mongo documents; these are not safe
response DTO definitions to copy wholesale.

`coach_assignments` first reads `linked_staff_id` or legacy `linked_id`. If that
record exists, its full normalized phone must agree. If no record resolves, a
fallback requires exactly one matching staff phone among a bounded scan. Scope
unions staff `team_ids`, legacy `team_id`, singular/plural Academy assignments,
and groups whose `coach_id` equals the staff ID. `staff.team_id` is a legacy
reader, not a current Staff model field. Team-only matching excludes records
with Academy-group markers. These aliases require import reconciliation; they
are not interchangeable new grants.

Own player phone and guardian phone have separate authority. Source role
detection scans players by `parent_phone` for parents and by `phone` for players,
using first-match/bounded scans and omitting active/archive/transfer filtering.
Coach upsert does not persist a canonical staff FK. Staff login summaries are
explicitly non-authoritative mirrors of `admin_users`; contradictory reverse
links must not be resolved by granting both accounts access.

Sources: [parent scope and peer projection](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L643),
[coach dashboard](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L802),
[coach assignment helper](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/mobile_scope.py#L5),
[management context](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L310),
[management roster](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L914),
[management player detail](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L1222),
[role derivation](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2301),
[staff mirror authority](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/staff_admin_sync.py#L73).

Anonymous `/api/players`, `/api/players/{id}` and
`/api/academy-groups/{id}/players` return the full `Player` response model.
That model can serialize guardian/emergency contacts, the supposedly admin-only
registration number and injury/deep-profile fields if populated. This is a
source-code privacy gap, not a claim that particular live data was exposed.
The group-player read also lacks explicit archive/transfer exclusion; detail
lookup lacks an active-roster predicate. Do not reproduce these routes or
private fields by widening the picker.
[Anonymous handlers and response models](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L546).

## Target: canonical data and deliberate security fixes

The canonical projection retains stable source entity identity, explicit
organization, typed roster fields and relationships needed for this slice.
Source IDs remain opaque, nonempty bounded values of at most 128 characters;
the model's UUID default does not justify rewriting a valid legacy ID or
rejecting it merely for not parsing as a GUID. Team and AcademyGroup stay
distinct. Nullable `team_id` and primary `academy_group_id` are retained;
Academy memberships use the deduplicated union of valid primary and plural
assignments. A valid primary group absent from the plural array is included,
not guessed or discarded. Every referenced entity must exist and agree with
the owning organization. Explicit organization tags win; tag/relationship
conflicts are rejected or quarantined, never automatically retagged. Cached
group/coach names are not relationships.

This slice does not require typed private phone or DOB columns. The protected
raw archive preserves those source fields for later explicit mapping. It also
does not populate mobile identity operational links or turn roster data into
identity permissions. Staff records, coach account binding and coach grants
remain deferred; group coach linkage is preserved in raw provenance until that
workflow can validate its staff FK. Do not silently invent an assignment, season, staff
membership, player identity, permission or primary group to satisfy a FK.

For player flags, missing fields follow the source model defaults:
`is_active=true`, `is_archived=false`, `is_transferred=false`. Explicit null or
a wrong type is rejected rather than coerced. Preserve the original absence
and values in the owner-only source archive; defaulting a canonical roster
flag does not activate a mobile identity or grant roster access.

Picker eligibility deliberately strengthens source omissions: require current
first-team FC classification, active status, and no archive or transfer state.
Require the signed mobile identity's ID/phone/role to agree with current active
state; management also needs its current active admin relationship. Source
`get_mobile_user` checks the signed mobile-token type and existing row but does
not establish all those current grants. Self-edited birthday, favourite-player
selection, a roster row or an imported contact cannot grant a formal role.
[Source token lookup](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L292).

The source's shared active fixture roster already excludes inactive, archived
and transferred players; that is evidence for preserving the distinction rather
than treating archive/transfer as cosmetic flags.
[Active roster predicate](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/team_scope.py#L27).

Return the exact five-field picker DTO in the existing envelope, bounded to 50
with deterministic ordering. A favourite reference remains preference metadata,
never permission to open a player detail, see a child or query private records.
The UI must distinguish loading, failure/retry, verified empty and available
choices. An unlisted saved reference gets a neutral label and is preserved
until the user explicitly changes or clears it; no name or profile is invented.

An **uninitialized roster returns 503 unavailable**, not a successful
zero-player list or a fake zero dashboard. Initialization is one committed
initial-snapshot state. Only an initialized snapshot with genuinely no eligible
records may return 200 with `{ "players": [] }`. Database errors likewise
remain unavailable; they are not converted to empty success data.

## Target: owner-only initial snapshot and raw preservation

The importer is an explicit operator path for an initial, bounded snapshot of
three roster collections: `teams`, `academy_groups`, `players`. Staff is not
imported until the canonical coach workflow is implemented.
It is not an API upload, ordinary application startup behavior, an ongoing
synchronization service or permission for production execution. Its scope does
not include CMS/mobile accounts, passwords, tokens, grants, finance, documents,
media binaries or provider credentials. Roster import must not activate formal
roles or create guardian/staff account permissions from phone matches.

Preserve original source records and snapshot provenance privately so fields
outside the first canonical projection are not silently lost. The source
archive is **owner-only**: the restricted API role must have no read or mutation
access to raw records, and no API payload, browser asset, log or public report
may return them. Raw preservation is not authorization, a safe public DTO, a
complete production backup or proof of source-data validity. Files containing
raw source data remain outside Git, images and frontend fixtures.

Import validation must establish bounded input, complete collection inventory,
unique stable source IDs, declared organization classification, valid required
canonical types/statuses and resolvable relationships. Reject or quarantine
unknown tags, duplicate IDs, empty/unsafe/overlong IDs, contradictory assignments,
missing referenced entities and cross-organization links. Private phone/DOB
values are preserved in the owner-only archive without adding typed identity
mapping to this import. Any later identity import must separately resolve
ambiguous phones and guardian/own-phone authority before granting access.
Preserve restricted conflict evidence without exposing private field values in error output.
Do not use source API limits or role-detector scan caps as population limits.

The initial snapshot and canonical data must commit atomically with provenance
and readiness. A failed import must not partially populate a visible roster or
mark initialization complete. An initial-only importer must reject subsequent
replacement of established data; any later refresh requires its own reviewed
reconciliation, concurrency and recovery design so it cannot overwrite verified
edits, restore revoked relationships or discard history. Existing legacy media
URLs are preserved as data; reading an image must not discover or call provider
credentials during import.

## Remaining production cutover work

No real roster export/import or deployed-revision reconciliation was performed
for this source audit. Production cutover still requires a protected complete
export, recorded source revision/time/checksums and counts, validated ID/phone/
status/link conflicts, explicit operator authorization, a restore rehearsal and
rollback or forward-recovery proof. Owner-only raw roster preservation does not
cover all Mongo collections, uploaded media, VAPID keys, queues or accounts.

Identity and credential import, verified guardian/player/staff/admin bindings,
legacy token handoff, current role revocation, original public URL/media
migration and provider restoration remain separate gates. Complete private or
public roster/dashboard contracts require their own safe DTOs and authorization
before exposure; the five-field picker is not a substitute for those modules.
See the [identity migration gates](legacy-identity-migration.md) and
[media migration requirements](media-contract.md).
