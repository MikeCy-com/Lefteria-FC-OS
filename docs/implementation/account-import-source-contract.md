# Legacy account import source contract

## Audited revision and scope

The account audit pins original GitHub repository `MikeCy-com/Lefteria-FC` at
`2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30`. On 2026-10-27, the
[branch API](https://api.github.com/repos/MikeCy-com/Lefteria-FC/branches/main)
resolved main to `a52d66b8e01c010cfe95ce53fa139e126bae4d84`. The
[7833-to-2986 comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/7833daceceac1611c368e7bc4df2a82c64b45e1a...2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30)
changes only public CSS, and the
[2986-to-c83 comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30...c83fd6dfa798cfaada8ae3483f7c0148b245b89c)
changes only public frontend branding, as does the subsequent
[c83-to-a52 comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/c83fd6dfa798cfaada8ae3483f7c0148b245b89c...a52d66b8e01c010cfe95ce53fa139e126bae4d84).
Account-related backend files are
unchanged. Pinned code is not proof of deployment, clean source data or a shared
export boundary. No production records, credentials or providers were accessed.

This slice imports protected offline account evidence into the canonical roster
and staff foundation. It does not implement CMS password login, invite
acceptance, legacy JWT acceptance/exchange, social features or a production
cutover. See the [legacy token audit](legacy-identity-migration.md),
[roster contract](roster-source-contract.md) and
[staff relationship contract](staff-relationship-source-contract.md).

## Source records and field authority

These are evidenced writer/reader fields, not a claim that every historical row
has a clean schema. Several source profile routes accept dictionaries rather
than strongly typed models; importer validation must not assume those stored
values already meet the rewrite's constraints.

| Collection | Observed fields | Authority and preservation boundary |
| --- | --- | --- |
| `mobile_users` | `id`, `phone`, `role`, `name`, `linked_player_id`, `linked_player_ids`, `team_id`, `academy_group_ids`, `avatar_url`, `platform`, `created_at`, `last_login`; profile writers add `favorite_team`, `first_name`, `last_name`, `birthday`, `email`, `favourite_player_id`, `notification_prefs`, `updated_at`. | Actual rows are role-specific mobile subjects. The upsert key is exact stored `(phone, role)`, not a person ID. Preserve legacy subject identity and raw provenance; validate operational links independently. |
| `fans` | Independent `id`, `phone`, `first_name`, `last_name`, `name`, `birthday`, `created_at`, `updated_at`; profile mirrors can add favourite player/preferences, avatar and city. | Registration evidence is created before OTP completion. A fan row is not a mobile subject or proof of verified ownership. Its ID must remain distinct from the corresponding mobile ID. |
| `admin_users` | `id`, `username`, `password_hash`, `name`, `email`, `phone`, `country`, `role`, `is_active`, `otp_2fa_enabled`, `assigned_team_id`, `assigned_academy_group_id`, `organization_ids`, legacy `organization_id`, `active_organization_id`, `staff_id`, created metadata; invitation rows add `invite_pending`, token/expiry, then `accepted_at`. | Authoritative CMS credentials, role and account state. Import supported encoded credential strings and invitation/2FA state through the owner boundary, without creating an authentication endpoint or activating an invitation. |

Sources: [mobile upsert](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L425),
[common profile writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L1701),
[registration writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L2408),
[admin creation](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L257),
[invite creation and acceptance](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L430).

Admin `name` is optional on create/invite and official writers store an empty
string when omitted. Import must preserve that valid empty display value rather
than quarantine an otherwise valid account or invitation. Username remains a
distinct required credential identifier.

### Fan mirrors and social evidence

`PUT /mobile/profile` mirrors only first/last/name/birthday/favourite-player/
notification preferences for the current fan. Email and favorite-team are not
part of that mirror. A second writer, `PUT /mobile/fan/profile`, mirrors `name`,
`birthday`, `avatar_url`, `city`, `updated_at` to both collections.
[Common mirror](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L1724),
[second fan writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/fan_engagement.py#L569).

`GET /mobile/auth/me` uses a narrow fallback. When fan birthday or first name is
falsey, it reads the fan with the exact stored phone. Only truthy first/last/
birthday/favourite-player/preferences fill falsey mobile fields. This is not
an unconditional newest-row or fan-over-mobile precedence rule. Conflicting
values retain their source origin; a deterministic target resolution must not
overwrite a later verified user edit or copy another role's profile.
[Current-user fallback](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L599).

The social resolver persists `social_principal_id`, choosing existing principal
evidence or the earliest exact-phone mobile account, then writing it to role
rows with that exact phone. Full raw rows must preserve this and untyped future
metadata privately. It does not replace a role-specific subject, authorize a
person merge or grant social access. Self-reported birthday remains profile
data, not a new verified-age or consent grant.
[Social principal resolver](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/social_policy.py#L490).

## Phone, role and status semantics

The source has distinct normalizers. `profile_media.normalized_phone` strips
Python non-digits, removes one leading `00`, and prefixes `357` for eight digits.
It compares nonempty complete normalized strings, never suffixes. The mobile
request/register writer separately preserves `+`, converts initial `00` to `+`,
or prefixes `+357` after stripping leading zeros. CMS SMS normalization retains
digits/plus and uses a country dial code for local numbers. These algorithms do
not establish interchangeable aliases or a verified country for every value.
[Mobile helper](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/profile_media.py#L6),
[full comparison](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L2491),
[CMS normalizer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L135).

The target canonical phone policy is ASCII digits, bounded international form
and full comparison, with raw phone/country origin retained privately. Reject
unsafe inputs, normalization collisions and unresolved country ambiguities;
do not guess a new prefix merely to connect two accounts. Multiple mobile roles
at one canonical phone are expected; conflicting duplicates within one role
quarantine all competing rows rather than selecting the first or newest.

Admin create/update/invite/profile writers trim the phone but do not normalize
it before persistence; staff account sync likewise copies a raw phone. They
permit bare digits to be stored without proving whether a value longer than
eight digits is country-local or already international. The conservative target
requires explicit international `+`/`00`, or an unambiguous eight-digit Cyprus
local number, for admin binding. Quarantine of other bare admin numbers is a
declared target ambiguity safeguard, not a claim the source forbids that format.
[Admin writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L269),
[self-profile writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/server.py#L455).

The detector emits `parent`, `coach`, `management`, `player`, `fan`. It scans
bounded first matches and omits active/archive/transfer predicates. Any staff
title can become `coach` in that detector; that is not evidence that a title
alone should create a role. Compatibility media readers also mention mobile
`admin`, but the detector does not establish an `admin` to `management` alias.
[Detector](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L2301),
[media compatibility reader](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/profile_media.py#L13).

CMS rejects explicit `is_active=false`; social also rejects case-folded
`account_status` disabled/inactive/suspended. Missing/null is not explicit false
in those source readers. The social helper does not trim the status and has no
positive active-status allowlist: unknown values pass that denylist. No emitting
account-status writer was found in the audited account paths or GitHub code
search. Some community predicates instead compare the raw status case
sensitively. Any broader trimming/unknown-status denial in the target is a
deliberate tightening, not a source guarantee. Preserve presence and raw state.
Imported mobile account activity and current lifecycle availability are separate: a
currently archived player or staff member must lose formal-role availability
without rewriting history or reactivating an account. Fresh canonical status,
phone and relationship checks remain required at runtime.
[CMS state](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/auth.py#L68),
[social state](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/social_policy.py#L226).

The approved target policy deliberately tightens mobile/fan status handling:
missing, null or exactly empty status follows `is_active`; normalized `active`
also follows that flag; trimmed/case-normalized disabled/inactive/suspended
stays inactive. Every other nonempty value, including whitespace-only status,
is quarantined with `account_status_unknown`. This explicit target allowlist
must not be described as behavior guaranteed by the source writers.

## Canonical link authority

| Mobile relationship | Source evidence | Target acceptance rule |
| --- | --- | --- |
| Own player | `linked_player_id`; legacy media `linked_id`; detector matches player's own `phone`. | Resolve the actual canonical player and validate full own-phone agreement. Jersey, registration number, display name and guardian phone cannot replace that identity. |
| Guardian children | `linked_player_ids` and primary `linked_player_id`; detector matches `players.parent_phone`. | Multiple children are legitimate, but every child link must resolve and agree with guardian contact. Own-player and emergency contact fields are different origins. |
| Coach staff | `linked_staff_id` or `linked_id`; helper checks phone, otherwise permits one unique full-phone match. | A consistent explicit staff link is stronger than phone-only fallback. Validate canonical staff status and phone; never grant from job title or an ambiguous phone scan. |
| Management admin | Media reads `linked_admin_id`, `admin_user_id`, `linked_id`; source management operations instead first-match admin phone. | Bind a validated explicit target admin FK and recheck its activity/invite/role/grants. Contradictory aliases cannot fall through to another phone match. |

Sources: [assignment helper](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/mobile_scope.py#L5),
[media link readers](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/profile_media.py#L13),
[source management recheck](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L310).

Consistent one-sided staff/admin pointers are supported: `staff.admin_user_id`
or reverse `admin_users.staff_id`. Explicit reverse links to another staff
member and competing rows are conflicts. Staff `has_login`, username, admin
role and OTP summaries are mirrors of the admin account and cannot reactivate
or alter it. The source can deactivate linked login on staff archive; unarchive
does not itself reactivate that login. Preserve the separate account and staff
states.
[Reverse resolver](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/server.py#L1811),
[mirror authority](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/staff_admin_sync.py#L73),
[archive/unarchive](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/server.py#L2147).

Team/group availability must come from canonical roster/staff relationships,
including staff singular/plural assignments and group-coach evidence. Empty
scope stays empty. The account import cannot turn raw cached mobile group/team
arrays into independent grants. Registration-only parent fallback reads
`parent_mobile/academy_group_id`, unlike the current registration model's
`parent_phone/assigned_group_id`; no automatic alias or fabricated child is
authorized by that mismatch.
[Parent fallback](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L2364).

## CMS organization and credential rules

Canonical CMS roles are exactly `super_admin`, `manager`, `editor`, `marketing`,
`coach`, `matchday_editor`. The org reader accepts historical `admin` and
strips/lowercases roles, while the permission matrix uses exact canonical role
keys. Preserve/quarantine noncanonical values instead of elevating them.
[Role matrix](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L45).

Only organization IDs `lefteria_fc` and `lefteria_fc_academy` exist in this
contract. Non-null `organization_ids` is authoritative, including an empty list
or empty string; the reader accepts a string as one assignment. Missing/null
plural falls back to singular. Only when both are missing/null can legacy
super-admin receive both, or the separate coach backfill derive one from an
Academy assignment, otherwise team assignment. Academy takes precedence when
both operational assignments are truthy. Preserve backfill provenance and
validate the referenced actual group/team. Explicit empty scope cannot be
replaced by backfill. Only a current grant can become active context.
[Org helper](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/organization_scope.py#L38),
[coach backfill](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/auth.py#L82).

Source passwords are UTF-8 bcrypt encoded strings, not ASP.NET Identity hashes.
Preserve supported full hashes without double hashing, logging or replacing
them with a common password. Pending invites begin inactive with null hash and
`invite_pending=true`; acceptance creates the hash and activates the account.
Import pending/2FA state and full protected raw evidence without executing old
invite tokens, importing OTP challenges, sending SMS or implementing a new
credential/bridge endpoint. An active/password/invite contradiction must not
silently activate access.

The target permits an explicitly `is_active=false` admin to retain a null hash
as inactive historical evidence, including a nonpending account. Current source
writers do not prove that nonpending credentialless history was intentionally
created: ordinary create/staff-login writes bcrypt, invite creation is pending,
and deactivation preserves the existing hash. This is an explicit fail-closed
preservation decision. Missing/null/default-active status with a null hash is
quarantined; pending null-hash accounts remain inactive. No such imported row
can activate CMS or mobile management access merely because it was preserved.
[Hash helpers](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/auth.py#L29),
[invite lifecycle](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/admin_users.py#L430).

## Target manifest, quarantine and execution boundary

The account operation is explicitly `--kind accounts` on the owner import tool.
Its exact manifest keys are `schema_version` equal to 1, `source_repository`
equal to `MikeCy-com/Lefteria-FC`, `source_commit`, `roster_sha256`, `staff_sha256`,
`exported_at`, `complete_collections`, `mobile_users`, `fans`, `admin_users`.
Complete collections are exactly `["mobile_users","fans","admin_users"]`, with
whole source objects in the corresponding three arrays. `exported_at` declares
one UTC `Z` export boundary. The source commit must exactly equal both bound
base snapshots; unchanged code is not permission to mix revisions or data
snapshots. Export-boundary declaration does not itself prove an operator
captured a consistent source export.

Default execution validates bounded offline shape only and reports
`bound_snapshots_checked=false`. Explicit `--review-target` uses an owner
connection to read and reconcile bases without writes. Explicit `--apply` is
an initial import, committing typed accepted accounts, canonical FK links,
quarantine, private mappings/raw rows and full original manifest atomically.
Neither normal API startup nor a default dry run may discover credentials,
connect to a provider or apply data. Target confirmation and nonproduction
operator verification remain required; an environment label or database name
does not independently prove a safe host.

Semantic conflicts quarantine all competing rows with stable safe reason codes;
there is no first-row winner, merged privilege set or random retry identity.
Mappings are scoped by source collection and legacy subject. Only actual mobile
rows can become mapped mobile subjects. Fans-only evidence remains dormant until
the normal verified-OTP path establishes ownership. Same-phone roles remain
separate; removal of a formal grant does not restore another formal role or
copy a child's private name into a public fan identity.

Full raw source, invite evidence, legacy mappings and conflict details stay
owner-only. Typed canonical phone bindings and relationship columns may receive
only the narrow server-side privileges needed for fresh authentication checks;
they do not become a contact-directory or profile API payload. Credential
strings remain protected server data. Public tool output uses counts/digests/reason codes,
never phones, names, hashes, tokens or whole rows. Retry must compare the actual
derived stored values, not counts alone; it must not overwrite later verified
edits or silently repair a partial restore. Failures roll back all account
changes, quarantine/readiness/raw and audit effects together. Published roster
and staff snapshots remain the required recovery base.

Required isolated checks include ambiguous canonical phones/subjects/usernames;
consistent explicit links versus ambiguous phone fallback; conflicting reverse
links; guardian versus own-player origin; current status/phone changes; shared
FC/Academy staff; missing/null/empty organization evidence; pending invites and
2FA retention; fans-only unverified evidence; exact snapshot/revision binding;
exact raw preservation; same-digest drift/replay; atomic rollback; and runtime
denial of owner-only records. These are implementation acceptance requirements,
not claims that production export, credentials, bridge or cutover are complete.
