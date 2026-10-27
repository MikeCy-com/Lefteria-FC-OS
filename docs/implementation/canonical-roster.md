# Canonical roster and authenticated player picker

The initial roster boundary below is now extended by the
[account import](account-import.md): current imported role availability rechecks
canonical player/guardian/coach/admin links, including the final picker SQL.
Runtime grants now include the narrow relationship projections documented there.

This initial slice adds canonical storage, an explicit owner import, and
`GET /api/mobile/players-list`. It does not implement dashboards, staff/coach/
guardian grants, private player records, account import or legacy JWT bridging.
Existing auth profile link fields remain unchanged; a roster row or favourite
player preference never grants ownership or changes formal-role availability.

Source is original GitHub commit
`7833daceceac1611c368e7bc4df2a82c64b45e1a`. Its Team/AcademyGroup/Player models
are distinct; there is no AcademyGroup.team_id relationship. The player model
uses UUID defaults but string IDs, so UUID parsing is not a migration requirement.
[Models](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L146),
[player fields](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L456),
[source picker](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L1732),
[explicit organization precedence](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L181).

## HTTP and persistence boundary

The picker returns exactly `{players:[{id,name,number,position,image_url}]}`,
with image_url nullable. All five current active mobile roles may use it;
management requires its current active linked admin, without an operational
organization grant/selector. It validates signed ID/full phone/role/expiry and
current status, then rejoins that current identity/admin in the same SQL snapshot
as readiness and player selection. Browser roles, query selectors and favourite
IDs cannot broaden the result.

Only explicit `lefteria_fc` organization, exact `First Team`, active, unarchived,
untransferred players appear. The source must explicitly contain both valid
`is_active` and `team_type` fields: Mongo's exact source predicates excluded
missing fields even though the model declares defaults. An owner-derived
`has_explicit_picker_fields` flag preserves that distinction; similarly named
input properties cannot override it. Results are ordered by number then ID using PostgreSQL C
collation, at most 50. The lifecycle filtering deliberately closes a source
picker omission. No roster readiness gives 503 `roster_unavailable`; a verified
empty snapshot gives 200 with an empty list. An unfinished dashboard does not
return a partial successful object containing invented zero totals.

`club.teams` belongs to FC; `club.academy_groups` belongs to Academy.
`club.players` retains explicit organization, primary group, optional team,
status flags and safe picker fields. `club.player_academy_groups` is the
deduplicated union of primary and plural group memberships. Global opaque IDs
remain case-sensitive strings. Organization FKs and alternate
`(organization_id,id)` keys/composite relationship FKs reject cross-organization
teams/groups. No group-to-team FK is invented.

`club.roster_import_state` records one successful initial snapshot's digest,
source revision, environment assertion, counts and time. Full original source
objects are stored as exact source JSON text in
`migration.roster_source_documents`, keyed by source collection/ID and linked to
that snapshot. Unknown/private source fields are retained there, never projected
through this API. No phone/DOB/medical/contact columns are needed for this slice.

The runtime role has SELECT only on picker/status/organization predicate columns
and the readiness ID. It cannot SELECT whole player entities, team/group links
or raw archives, has no migration schema USAGE, and cannot write roster/import
tables. Owner credentials are not registered in API configuration or DI.

## Import manifest and validation

The separate executable is `backend/tools/LefteriaFc.RosterImport`. Its default
mode validates an input file without reading DB credentials or creating a DB
connection. The product image does not include or invoke this executable.

The manifest has only these top-level fields: schema_version equal to 1,
source_repository exactly MikeCy-com/Lefteria-FC, a 40-hex source_commit,
complete_collections exactly `[teams,academy_groups,players]` in that order,
and arrays named teams, academy_groups, players. Each array contains whole source
objects, not API DTOs. An intentionally empty complete snapshot has all three
arrays present and empty; normal application startup never creates readiness.

Bounds: 32 MiB input, JSON depth 32, 1000 teams, 2000 groups, 20000 players,
128-character IDs, 200-character names, 2048-character image URLs and 128 plural
groups per player. Duplicate JSON keys at
every nesting level, duplicate IDs within a collection, invalid field types,
unknown/empty organizations, dangling/cross-org links and contradictory
references reject the whole manifest before DB connection. IDs/names are not
truncated, UUID-coerced, case-folded or matched by display name.

Documented source defaults/normalization:

- Missing team_type becomes First Team; supported values are exactly First Team
  and Academy. Missing plural group list becomes []; explicit null/wrong list
  type fails. Primary group joins the plural membership union without duplicates.
- Missing is_active is true; missing archive/transfer flags are false. Explicit
  null or nonboolean flags fail. Number is an Int32 JSON integer; position is one
  of Goalkeeper/Defender/Midfielder/Forward.
- Missing is_active/team_type defaults remain in canonical rows, but those
  rows are excluded from the picker because their explicit-field provenance is
  false. The flag is derived from validated source fields, never imported from
  a supplied flag property.
- Missing/null organization classifies teams as FC, groups as Academy, players
  as Academy when their type/groups mark Academy, otherwise FC. Explicit known
  organization wins even when team_type differs. Contradictory cross-org FKs
  still fail rather than being silently repaired.
- Optional empty/null team/group/image strings mean unassigned/null in typed
  rows. Their exact original source representation remains in the archive.
- Images may use known public upload-image folders or public HTTP(S) URLs.
  Protocol-relative/data/javascript/credential-bearing URLs fail. Decoded and
  normalized API/upload paths cannot bypass the public whitelist using case,
  percent encoding or dot segments; private attachment/social/document delivery
  paths are not picker image URLs. The importer does not fetch URLs or prove
  that a legacy public file currently exists.

Failure output contains only fixed error codes, collection names/row indices
and counts; success output adds the whole-input SHA-256. It never prints names,
contact details, source JSON, passwords or connection strings. Invalid rows are
not silently skipped: the protected source file remains for operator review;
no partial DB quarantine/import is created by this initial tool.

## Explicit apply and recovery

Dry run:

    dotnet run --project backend/tools/LefteriaFc.RosterImport -- --input <manifest-path>

Apply additionally requires `--apply`, `--expected-sha256`, `--expected-database`,
`--environment development|staging`, and the separate server-only
`LEFTERIA_ROSTER_IMPORT_CONNECTION`. No runtime connection fallback is used.
The actual connected database name and owner/superuser authority are checked;
system databases and common production-name words are refused.

**Environment is an operator assertion.** An expected database name and name
denylist cannot prove a host is nonproduction. Before apply, the operator must
independently verify host, port, server, database and intended environment; a
production database with an ordinary name must never be mislabeled staging.
All current development verification uses the separately guarded exact
loopback fixture; this work does not authorize any external or production
connection/export.

Apply obtains a transaction-scoped advisory lock, requires empty canonical/
archive/state tables, writes typed rows, full raw objects, readiness and one
append-only audit atomically. Exact-digest replay is a no-op with count
reconciliation; a different digest or uninitialized existing data is refused.
There is no delete, merge, replacement, automatic update or role provisioning
mode. A later reconciliation/update tool requires its own reviewed contract.

Migration `20261026071352_CanonicalRoster` Down removes canonical rows, readiness
and owner-only source archives. It cannot recover those records automatically;
retain an approved protected DB backup and original manifest/checksum before
rollback. Identity/profile/preferences and their opaque favourite IDs are not
rewritten; picker reads become unavailable until a trusted snapshot is restored.

Tests cover strict/private validation and no-DB CLI dry-run; missing versus
verified-empty readiness; initial apply, archive-failure rollback, exact replay,
concurrent imports and replacement refusal; lifecycle/org filtering, all active
roles including management without org grants, final-query revocation races,
five-field DTOs; composite FKs and runtime archive/write/import denials. Test
snapshots use generated IDs and exact digest/row cleanup only, with audits kept.

Verification on 2026-10-26: the final Release executable passed all 31 HTTP/
PostgreSQL identity groups against the guarded disposable loopback fixture.
Post-run SQL confirmed zero canonical roster rows, readiness/source archives,
stored avatars and temporary roster trigger/functions; audit history remains
append-only. This does not establish any production import or cutover.

The production frontend build also passed all 63 Node tests, Svelte/type checks
and production package/route smoke. At 390 × 844, the real browser/API/restricted
database flow showed unavailable before import, then only the eligible fictional
first-team player after explicit import. Cancel preserved the unlisted previous
preference and restored focus; saving and clearing both survived full reloads.
The preview shut down cleanly, and exact-digest cleanup left zero roster/source
archive/readiness rows and no preview identities. All test data was fictional.
