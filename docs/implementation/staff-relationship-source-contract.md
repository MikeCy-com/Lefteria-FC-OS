# Staff assignments and private contact source contract

## Baseline and executable slice

Read-only GitHub inspection on 2026-10-26 reconfirmed original repository main
at **`7833daceceac1611c368e7bc4df2a82c64b45e1a`** through the
[branch API](https://api.github.com/repos/MikeCy-com/Lefteria-FC/branches/main).
Source links below pin that revision. GitHub main is not proof of the deployed
revision, source-data cleanliness or a completed export. No live records,
credentials or provider endpoints were accessed.

This slice uses an owner-only import extension,
selected explicitly with `--kind staff-relationships`; the default roster import
remains unchanged. The extension imports canonical staff and actual assignment
relationships, preserves group coach references, and materializes private
contact provenance from the already archived roster snapshot. Its separate
complete staff manifest is bound to the existing `roster_sha256` and source
commit. Validation, dry run, transactional apply, repeat handling and rollback
coverage are recorded in the [implementation and verification](staff-relationships.md);
this source contract does not substitute for those checks.

This is a data foundation before account mapping. It creates no mobile/admin
accounts, operational identity links or organization grants. It does not change
`MapProfiles` to return roster links, enable a formal role through a phone/title,
or implement private/public staff and role dashboards. Full account migration
and token handoff remain separate gates. See the
[roster contract](roster-source-contract.md),
[legacy identity migration contract](legacy-identity-migration.md), and
[role dashboard contract](role-dashboard-contract.md).

## Observed: staff records, status and titles

`Staff` has its own UUID-string default `id`, distinct from `admin_users.id` and
`mobile_users.id`. Current fields include `name`, public job `role`, nationality,
DOB, image, bio, own phone/email, `country`, `team_type`, team/group assignments,
qualifications, experience, achievements, social links, active/archive state
and role-change provenance. Full source records must be preserved privately
even when only a bounded typed subset is needed now.

Exact `StaffRole` values are:

- `Head Coach`
- `Assistant Coach`
- `Goalkeeper Coach`
- `Fitness Coach`
- `Physiotherapist`
- `Team Manager`
- `Youth Coach`
- `Scout`

These are staff job titles, not mobile roles or CMS permissions. Source mobile
role detection calls the first matching staff member a mobile `coach` regardless
of title; that broad detector is not evidence that a title alone grants access.
Do not map `Team Manager` to management or CMS manager, or discard a legitimate
staff record because its title is not one of the coaching titles.

The model defaults missing `is_active` to true and missing `is_archived` to
false. Explicit null is not a valid value for those model booleans. Separately,
the raw public staff query uses `is_active != false` and `is_archived != true`,
while mobile coach assignment/dashboard reads do not enforce either flag.
These different paths must not be collapsed into a claim that every historical
staff account was explicitly active. Preserve original presence and values.
Staff has `archived_at`, `archived_season`, `previous_role`, `role_changed_at`;
there is no staff `is_transferred` field in the inspected model.

Sources: [job titles](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L83),
[Staff and PublicStaff models](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L632),
[public staff status query](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L885),
[mobile detector](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2330).

## Observed: assignments, organizations and group coach ownership

Staff is a person-level record that can carry both first-team and Academy
assignment evidence. Current model fields are `team_ids`, primary
`academy_group_id`, plural `academy_group_ids`, and `team_type`. The mobile
helper also reads legacy singular `staff.team_id`, although it is not declared
in the current Staff model. Academy groups separately carry nullable `coach_id`
and display `coach_name`.

`coach_assignments(db,user)` does the following:

1. Reads `mobile_users.linked_staff_id` or legacy `linked_id`.
2. If that staff record exists, requires its full normalized phone to agree
   with the mobile identity's full phone. A mismatched existing link returns
   empty assignments.
3. If no staff resolves, permits phone fallback only when exactly one candidate
   matches among a bounded scan of at most 500 staff records.
4. Unions staff singular/plural team/group assignments with groups whose
   `coach_id` equals that staff ID, capped at 200 group matches.

Its `assignment_query` permits assigned Academy-group records, or assigned team
records with no Academy-group markers. An empty assignment stays empty. It
does not mean all teams. It does not impose a single organization on the staff
person, validate current staff status, or independently validate every
assignment's owning organization.

`organization_staff_query` is a different path: explicit record organization
tags win, otherwise legacy Academy-group/team-type markers classify a staff
row for an organization listing. That classification is not a replacement for
the actual team/group relationships and is not a grant for the mobile role.

Sources: [assignment resolver and query](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/mobile_scope.py#L5),
[organization ownership precedence](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L181),
[staff classifier](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L251),
[AcademyGroup coach fields](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L146).

### Target relationship constraints

Use global canonical staff identities with opaque source IDs of at most 128
characters and bounded names of at most 200 characters. The input budget is
32 MiB. These are target validation bounds, not evidence that every source
record already satisfies them. Keep staff job titles and strict boolean/presence
state separate from any future account grant.

Resolve each team assignment to an existing canonical **FC team**, and each
Academy assignment to an existing canonical **Academy group**, using the
organization-aware target FK for that entity. A staff member may have both FC
team rows and Academy group rows. Do not add an invented
`staff.organization_id == every assignment.organization_id` restriction that
would erase shared staff. Source record classification and individual assignment
ownership remain distinct evidence; neither automatically creates permissions.

Retain the primary Academy assignment and the deduplicated union of singular,
plural and group-coach assignment evidence. Preserve provenance of the inputs
instead of selecting a first arbitrary row. Group coach ownership is a nullable
group-to-global-staff relationship; validate the group's Academy organization
and the referenced staff's existence. Do not require that staff to have only
Academy assignments. Never synthesize a staff record from `coach_name`, an
unknown ID, a display name or a phone match.

Derive group `coach_id` links from the exact owner-only raw group records already
bound to the roster digest, not from a client selector or a new unbound copy of
the group list. A dangling reference or contradictory source linkage is an
explicit validation issue; no placeholder coach or silently broadened scope is
created. Multiple source origins pointing to the same valid staff/group pair
are deduplicated without losing the evidence.

## Observed: contact origin and guardian versus own phone

| Source field | Meaning evidenced by source | Foundation rule |
| --- | --- | --- |
| `players.phone` | Player's own phone; the mobile detector uses it for the player role. | Preserve as an own-player contact, never a guardian link. |
| `players.parent_phone`, `parent_name`, `parent_email` | Guardian contact fields; the detector matches `parent_phone` and can collect several children. | Preserve the per-player guardian origin. A same-phone fan identity, name match or registration number does not authorize child access. |
| `players.emergency_contact_phone` / name | Separate emergency contact. | Retain in raw preservation unless separately mapped; do not silently turn it into guardian authority. |
| `staff.phone`, `staff.email`, `staff.country` | Staff's own contact and country provenance. | Preserve original field/state. Country is not authority to rewrite the stored phone or grant a mobile role. |
| `admin_users.phone` / country | Separate CMS account contact and OTP-delivery normalization input. | Requires account migration; do not overwrite it from a staff mirror or merge it with player/guardian contact records. |

Sources: [Player contact fields](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L456),
[staff contacts](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L632),
[parent and player phone matching](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2301),
[CMS country normalizer](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/admin_users.py#L135).

The source mobile comparison normalizer strips non-digits, removes one initial
`00`, and prefixes `357` when eight digits remain. It compares nonempty full
normalized strings, never suffixes. The CMS normalizer has different country
and leading-zero rules. These algorithms are not interchangeable proof that
two contacts belong to the same person.
[Mobile normalizer](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/profile_media.py#L6),
[full-phone comparison](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2491).

### Target private contact preservation

Contact records remain **owner-only** and keyed by source collection, source
ID and field, with semantic origin and source archive provenance. Distinguish
missing, explicit null, empty string and actual value. Preserve raw values;
this slice performs no inferred phone normalization, suffix matching,
deduplication by phone, ownership verification or global phone uniqueness.
Player contacts come from the already archived exact player JSON, and staff
contacts from the new complete staff archive.

No contact/raw/state table becomes an API payload or browser fixture. The API
runtime role receives no access to the private contact or full source archive.
The fact that source `PublicStaff` includes phone/email/DOB does not authorize
exposing those fields through the rewrite foundation. Later operational reads
need their own role/relationship checks and allowlisted DTOs.

## Observed: staff, admin and mobile account links

The source has three separate identities:

- **Staff profile:** public job/data record. `has_login`, `username`,
  `admin_role`, `otp_2fa_enabled` and login summaries are non-authoritative mirrors.
- **CMS account:** `admin_users` is authoritative for username, password hash,
  CMS role, active/invite state, OTP settings and organization assignments.
  Staff account creation writes `admin_users.staff_id`; staff stores
  `admin_user_id`. Conflicting reverse links must not be joined by a first match.
- **Mobile identity:** a separate `mobile_users` row keyed by exact source
  `(phone,role)`, with its own ID, profile preferences and token subject.

`_get_linked_staff_admin_account` checks the staff's explicit admin pointer,
rejects an account whose reverse `staff_id` names another person, and otherwise
can resolve the reverse account link. Staff account creation/update rejects
another staff member already linked to that admin. Disabling staff login or
archiving linked staff can deactivate the CMS account; unarchiving staff does
not itself reactivate the admin. Last-active-super-admin protection and
compare-before-restore compensation are part of those source mutations.

Sources: [mirror authority](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/staff_admin_sync.py#L73),
[reverse-link resolver](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L1811),
[account synchronization and collision checks](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L1857),
[archive and unarchive](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/server.py#L2147),
[admin mutation guard](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/admin_user_guard.py).

The mobile detector uses first-match staff/admin/player phone scans and ignores
several status flags. `_upsert_mobile_user` stores player IDs, team and group
fields but does not persist a canonical staff FK. Mobile coach assignment
resolution therefore often depends on its unique-phone fallback. A
`mobile_users.staff_id` appearing in another projection is not an additional
canonical alias accepted by `coach_assignments`; `admin_users.staff_id` is an
evidenced reverse CMS link.
[Role derivation](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2301),
[mobile upsert](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L425).

## Observed: active state and organization grants

CMS authentication accepts only `type=access`, reloads the actual admin row and
rejects explicit `is_active=false`. Mobile authentication is a different token
realm, `type=mobile_access`; the source lookup does not itself enforce all
current status/link checks. Pending admin invites have null password hashes,
`is_active=false` and `invite_pending=true`. Staff login mirrors additionally
consider pending invitations. Preserve those distinctions; do not reactivate
an invited or disabled account while importing a staff profile.
[CMS authentication](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/auth.py#L68),
[invite record](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/admin_users.py#L430),
[mobile dependency](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L292).

`organization_ids` is authoritative when present, including explicit `[]`.
Missing/null plural assignment can fall back to singular `organization_id`.
Only the documented legacy active super-admin fallback supplies both
organizations when neither assignment exists. CMS coach authentication has a
separate narrowly scoped backfill: Academy when `assigned_academy_group_id`
exists, otherwise FC when `assigned_team_id` exists, only when both organization
fields are missing/null. It does not override explicit empty assignments.
These singular admin assignment fields are distinct from staff's assignment
arrays and are not inputs to the mobile helper.

The canonical CMS role matrix is `super_admin`, `manager`, `editor`, `marketing`,
`coach`, `matchday_editor`. The organization helper also recognizes historical
`admin`; this inconsistency does not permit elevating it to a canonical
privileged role. A mobile `management` identity must later map to an explicit
active admin FK, not the source's first matching admin phone. A selected
organization must be one of that admin's current grants.
Sources: [organization helper](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/organization_scope.py#L24),
[CMS coach backfill](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/auth.py#L82),
[canonical permissions](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/admin_users.py#L45),
[source mobile management lookup](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L310).

## Target apply and acceptance boundary

Require an initialized matching base roster digest and declared source commit,
complete bounded staff input, duplicate-safe opaque IDs, strict field types and
resolvable references. Derive group coach IDs and player contact origins from
the existing owner archive inside the import boundary. Keep the initial roster
snapshot and its raw source unchanged; the extension needs its own committed
provenance/readiness state and raw staff preservation. A failed relationship,
contact, raw archive or audit write must roll the entire extension back.

Same-snapshot retry must have no duplicate effect. A different snapshot or a
nonempty conflicting target must not silently replace established staff,
contacts or assignments. Use an explicit owner connection and target/digest
confirmations. Development/staging labels and a database-name denylist alone
do not prove that a host is nonproduction; operator target verification remains
required. Do not call real SMS, payment, social, storage or backup providers.

Acceptance coverage must include shared FC/Academy staff; singular/plural
unions; group-coach-derived membership and primary provenance; empty and
dangling assignments; non-coaching titles; missing versus null status/contact
values; own versus guardian versus emergency contact origin; exact raw record
preservation; mismatched base digest/source; idempotency/concurrency; atomic
rollback and restricted runtime denial. Existing auth/profile/picker behavior
must remain unchanged, and no operational identity link or new permission may
appear solely because this import succeeded.

## Required collections and next account gate

The foundation needs new complete **`staff`** source data plus the already
archived **`teams`, `academy_groups`, `players`** bound by the roster digest.
It does not need `admin_users`, `mobile_users` or `fans` to store actual staff,
coach references and contact provenance without granting access. This makes the
staff extension useful and independently testable before a unified account
mapping exists.

True account/role migration will additionally need complete **`mobile_users`**
and **`admin_users`**, and **`fans`** to reconcile fan profile mirrors and their
distinct IDs. **`registrations`** is needed if preserving the source's
registration-only parent fallback; its current `parent_phone/assigned_group_id`
model differs from the detector's `parent_mobile/academy_group_id` reader and
cannot safely fabricate children or approved guardian relationships.
[Registration model](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/models.py#L230),
[fallback reader](https://github.com/MikeCy-com/Lefteria-FC/blob/7833daceceac1611c368e7bc4df2a82c64b45e1a/backend/routes/mobile_auth.py#L2364).

That later gate must preserve exact legacy subjects and separate same-phone
roles, verified own-player versus guardian links, current active/archive/
transfer state, admin/grant authority, pending-invite status, bcrypt hashes and
profile provenance. No title, contact, pending registration or archived record
alone can activate a formal role. A removed formal role must still permit the
separate regular fan recovery path without restoring another formal role or
copying a child's formal name into a public fan identity. Legacy JWT acceptance,
credential login, consent/privacy policy, production export/reconciliation and
restore/cutover rehearsals remain explicitly unimplemented by this foundation.
