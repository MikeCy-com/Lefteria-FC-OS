# Legacy identity import and mobile-session bridge contract

Source audit only, through the GitHub connector on 2026-10-25. Original
`MikeCy-com/Lefteria-FC` main was rechecked and resolved to
`5111f06af5d8b17f855ceed9637caca02db117e2`. Links pin that source unless a
historical commit is explicitly named. GitHub main does not prove the deployed
revision. No production data, credentials, tokens, provider or export endpoint
was accessed. This document adds no importer, bridge, schema or production change.

The GitHub audit was refreshed on 2026-10-27. Main first advanced to
`2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30` from `7833dace` through public-site
CSS changes only, then to `c83fd6dfa798cfaada8ae3483f7c0148b245b89c` through
frontend public-branding changes only. The account-related backend files remain
unchanged. Existing links retain their original immutable audit baseline;
additions below pin `2986c570`. Equal code does not prove equal exported data or
permit silently replacing a bound snapshot's source revision.
[CSS comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/7833daceceac1611c368e7bc4df2a82c64b45e1a...2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30),
[public-branding comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30...c83fd6dfa798cfaada8ae3483f7c0148b245b89c).

The later same-day [c83-to-a52 comparison](https://github.com/MikeCy-com/Lefteria-FC/compare/c83fd6dfa798cfaada8ae3483f7c0148b245b89c...a52d66b8e01c010cfe95ce53fa139e126bae4d84)
also changes public branding only. The implemented initial account import and
its current verification are described in [account import](account-import.md).
The subsequent [bounded legacy exchange](legacy-mobile-bridge.md) implements the
explicit exchange option below, disabled by default. Enabling it, validating real
exports, cached-client updates and staging cutover remain separate gates.

Sections marked **observed** describe repository code. Sections marked
**proposed requirements** specify the validation needed before implementing or
enabling an import/bridge; they do not claim source data is clean or migrated.

## Observed records and authority

| Source collection/model | Identity and linkage evidence | Import implication |
|---|---|---|
| `mobile_users` | `_upsert_mobile_user` keys by exact `(phone,role)` and creates a UUID-string `id`. Writes `name`, `linked_player_id`, `linked_player_ids`, `team_id`, `academy_group_ids`, avatar/platform/created/login fields. Profile writes add first/last/birthday/email/favourite-player/preferences/updated timestamp. | Preserve each role identity and its old token subject independently. A person/phone match is not permission to copy another role's profile or links. |
| `fans` | Registration upserts exact `phone`; creates its own UUID `id`, first/last/name/birthday/timestamps before OTP completion. Later fan profile writes mirror selected fields by phone; `/auth/me` can fill missing fan fields from this row. | Fan ID is not the mobile ID. Preserve both mappings and field provenance. A fan row alone is not evidence that an OTP was completed. Conflicting mirror values require a declared resolution rule. |
| `admin_users` | UUID-string `id`, username/password hash, name/contact/country, canonical CMS role, active/2FA/invite fields; explicit organization and coach assignment fields. Staff-created accounts add `staff_id`. | This is authoritative for CMS credentials/permissions. Mobile `management` is a separate identity linked to this admin, not the CMS role itself. |
| `players` | UUID-string `id`, own `phone`, `parent_name/parent_phone/parent_email`, team/group fields, DOB and active/archive/transfer flags. Both first team and academy use this collection. `registration_number` is an admin-only membership identifier, not the UUID or jersey number. | Player-own-phone and guardian-phone relationships have different authority. Do not merge a parent with their child or use membership/jersey/display name as an account ID. |
| `academy_groups` | UUID-string `id`; `coach_id` references staff; group/team context is separate from mobile role. | Validate group and coach FKs; preserve group assignment history and organization scope. There is no separate academy-login collection in the inspected login path. |
| `staff` | UUID-string `id`, public staff role/title, contact/country, `team_id` compatibility reader plus `team_ids`, `academy_group_id/academy_group_ids`, active/archive state. Optional `admin_user_id` links CMS account. | Public staff role, mobile coach role and CMS admin role are distinct. Staff `has_login/username/admin_role/otp_2fa_enabled` are non-authoritative mirrors. |
| `registrations` | Model stores `parent_phone`, `assigned_group_id`, pending/approved/rejected state and registration/player information. Approval creates an Academy player with copied parent contact/group when no same-name Academy player is found. | Registration is not an authenticated parent account. Name-based approval deduplication is not sufficient identity proof or a reliable registration-to-player FK. |

Sources: [mobile upsert](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L425),
[fan registration](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L2408),
[profile mirrors](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1701),
[admin creation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/admin_users.py#L260),
[player model](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/models.py#L456),
[academy group](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/models.py#L146),
[staff model](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/models.py#L632),
[staff mirror authority](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/staff_admin_sync.py#L73),
[registration approval](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L4004).

There is a second fan profile writer, `PUT /mobile/fan/profile`, which mirrors
`name`, `birthday`, `avatar_url`, `city` and `updated_at` to both `mobile_users`
and `fans`. It is separate from the common profile route's mirror allowlist.
`GET /mobile/auth/me` has a narrower fallback: only when the fan's birthday or
first name is falsey does it read the exact-phone fan row, and only truthy
`first_name`, `last_name`, `birthday`, `favourite_player_id` and
`notification_prefs` fill falsey mobile fields. It does not unconditionally
prefer the fan mirror, or fill email/favorite-team/avatar through that loop.
Preserve field origin and conflicting values instead of inventing a universal
mirror precedence rule.
[Second fan writer](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/fan_engagement.py#L569),
[narrow current-user fallback](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/routes/mobile_auth.py#L599).

`social_principal_id` is another persisted mobile field. The social resolver can
choose an existing principal or the earliest exact-phone mobile account and
write the principal to same-phone role rows. Preserve it and other untyped
source fields privately for later social migration; it is neither a replacement
for a role-specific mobile subject nor authority to merge accounts or grants.
[Social principal persistence](https://github.com/MikeCy-com/Lefteria-FC/blob/2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30/backend/social_policy.py#L490).

## Observed phone and role derivation

There are multiple source normalizers; they are not interchangeable aliases:

- `profile_media.normalized_phone`: strips non-digits with Python `\D`, removes
  one initial `00`, prefixes `357` only when eight digits remain; returns digits
  without `+`. `_phone_match` compares nonempty full normalized values exactly,
  never a last-eight-digit suffix. This helper is permissive rather than an
  E.164 validator, and `\D` does not itself enforce ASCII digits.
- Mobile request/verify/register routes trim input, preserve an initial `+`,
  turn initial `00` into `+`, otherwise prepend `+357` after removing leading
  zeros. Exact stored `phone` keys can therefore differ from comparison keys.
- CMS `normalize_phone(phone,country)` retains digits/`+`; an initial `+` is
  preserved, otherwise leading zeros are stripped and a configured country dial
  code is added (fallback CY). This is used for admin 2FA delivery, not proof that
  every stored admin phone was rewritten or that it matches the mobile helper.

Sources: [normalization helper](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/profile_media.py#L6),
[full-phone comparison](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L2491),
[mobile request normalization](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L354),
[CMS country normalizer](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/admin_users.py#L121).

The source `_detect_all_roles` uses this ordered, bounded derivation:

| Mobile role | Observed derivation |
|---|---|
| `parent` | All players matching `players.parent_phone`; first child's parent name, first `player_id`, all `player_ids`, union of singular/plural academy groups. |
| `coach` | First matching `staff.phone`, regardless of public staff title; includes name/team/avatar. This upsert path does not write a `linked_staff_id`. |
| `management` | First matching `admin_users.phone`; initial role info contains username, without a persisted admin FK. Management operations re-read the admin by phone and require active status plus allowed organizations. |
| `player` | First matching `players.phone`; player ID, name, groups, avatar. |
| registration fallback `parent` | Only if no player-derived parent: first matching `registrations.parent_mobile`, with no player IDs and optional `academy_group_id`. |
| `fan` | First matching `fans.phone`, or a synthetic fan when any formal role exists. Source synthetic name copies a formal name; this is not an independent public-profile consent signal. |

The detector scans at most 500 players, 100 staff, 20 admins, 200 registrations
and 2000 fans, and uses first-match ordering. It does **not** filter active,
archived or transferred flags in those scans. These are observed limitations,
not import rules to reproduce. In particular, registration fallback reads
`parent_mobile/academy_group_id`, while the current registration model and
approval writer use `parent_phone/assigned_group_id`. Do not silently treat
those as equivalent source aliases or fabricate player links.
[Role detector](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L2301),
[registration model](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/models.py#L230),
[management recheck](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L310).

Other readers evidence compatibility fields without proving every record uses
them: coach assignments resolve `linked_staff_id` or `linked_id`, validate full
staff-phone agreement, otherwise allow only a unique phone match; then union
`academy_group_ids/team_ids`, singular `academy_group_id/team_id`, and groups
whose `coach_id` equals that staff ID. Empty assignments remain empty. Avatar
linking reads management `linked_admin_id/admin_user_id/linked_id`, player
`linked_player_id/linked_id`, and coach `linked_staff_id/linked_id`. Bidirectional
staff/admin links reject an explicit reverse pointer to another staff member.
These are the evidenced names; no `guardian_id`, `coach_user_id` or invented
generic account aliases are established by these paths.

A consistent explicit link is stronger evidence than phone-only fallback.
Uniqueness is required when using the fallback; duplicate phone candidates do
not by themselves justify replacing an already consistent explicit link.
Contradictory explicit aliases, reverse pointers, phone agreement or status must
still be reviewed rather than silently falling through to a different record.
[Coach assignments](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/mobile_scope.py#L5),
[avatar link readers](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/profile_media.py#L13),
[staff/admin reverse-link check](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L1811).

## Observed active state and organization assignments

CMS authentication rejects `admin_users.is_active is False`. Invited admins
start inactive with `password_hash=None` and `invite_pending=True`; invite
acceptance sets a bcrypt hash, activates the account and clears the invite token.
Staff login summaries also require no pending invite. Player roster models have
separate `is_active`, `is_archived`, `is_transferred` flags; staff has active and
archive flags. Keep each flag and its provenance instead of flattening all into
a default active account. Social access additionally rejects mobile
`account_status` values disabled/inactive/suspended and `is_active=False`.
In those `is False` checks, a missing/null flag is not an explicit revocation;
record that absence separately instead of claiming the source stored true.
[CMS active check](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/auth.py#L68),
[invite creation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/admin_users.py#L416),
[invite acceptance](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/admin_users.py#L582),
[active roster predicate](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/team_scope.py#L27),
[social active predicate](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L226).

Source `get_mobile_user` validates the signature/type and looks up `sub`; unlike
CMS/social checks it does not itself reject inactive state or cross-check the
JWT phone/role against the record. `/auth/me` re-detects available roles but can
still return the current identity when that role disappeared. A bridge must
retain current-state authorization and cannot inherit these gaps simply to
accept an old token.
[Mobile dependency](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L292),
[role refresh](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L599).

Organization IDs are exactly `lefteria_fc` and `lefteria_fc_academy`.

| Admin assignment state | Observed effective scope |
|---|---|
| Inactive/unknown CMS role | No organizations. |
| `organization_ids` list or string | Authoritative when non-null; `[]` and `""` grant nothing, including for super admin. The reader accepts a string as one assignment. Known IDs are returned in canonical order. |
| `organization_ids` missing or null | Fall back to `organization_id`; its string form is accepted by the reader. |
| Both assignment fields missing/null, active `super_admin` | Both organizations, as an explicit legacy fallback. |
| Both missing/null, other role | No grant from the organization helper. CMS coach authentication has a separate backfill from `assigned_academy_group_id`, else `assigned_team_id`; it does not override an explicit empty assignment. |
| One available organization | May persist that sole active context. |
| Two available organizations | Existing active selection must be in the grant set; otherwise an explicit selection is required. |

Unknown IDs are ignored by the source reader; write validation rejects unknown
or duplicate IDs. An explicit domain-record `organization_id` is authoritative;
only missing/null legacy organization fields use roster/group classification.
An explicit non-null plural assignment prevents singular fallback, including
when plural is empty. The helper strips/lowercases the CMS role, whereas the
canonical permission matrix uses exact role strings. This difference must not
silently turn a noncanonical stored role into a privileged imported account.
The organization helper recognizes legacy CMS role `admin`, but the canonical
permission matrix and new-account role allowlist contain only super_admin,
manager, editor, marketing, coach, matchday_editor. This inconsistency does not
authorize converting a stored `admin` record to super_admin or manager.
[Organization semantics](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/organization_scope.py#L24),
[coach backfill](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/auth.py#L82),
[tagged record authority](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/organization_scope.py#L181),
[canonical role permissions](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/admin_users.py#L45).

## Observed secrets, passwords and token families

The current source takes **server-only** `JWT_SECRET` directly from the
environment and uses HS256 for mobile and CMS/customer tokens. No key value was
read. Preserve the literal legacy signing-key meaning; do not assume it is the
rewrite's base64-encoded key format. Bcrypt 4.1.3 hashes UTF-8 passwords with
`bcrypt.gensalt/hashpw` and stores the encoded string in `password_hash`;
verification uses `bcrypt.checkpw`. That string is not an ASP.NET Identity hash
and must not be double-hashed. Source does not establish a plaintext-password
export, alternate hash aliases or password-reset-on-import requirement.
[Secret/hash helpers](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/auth.py#L24),
[mobile signing key/algorithm](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L52),
[dependency versions](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/requirements.txt#L10).

| Token family and source evidence | Claims/lifetime and compatibility consequence |
|---|---|
| Current mobile factory, main `5111f06` | `sub`, `role`, `phone`, `type=mobile_access`, `exp`, `iat`, fresh UUID-string `jti`. No `sid`, `iss`, `aud`, `nbf` or custom `kid` is emitted by this factory. |
| Remember-device mobile factory at `0e114936` (2026-10-01) | Same current claim family with `jti`. Verify body omission defaults to 90 days; explicit null means 24 hours; explicit integer 30/60/90 days allowed. |
| Older mobile factory at `8cf31e87` (2026-09-29), also observed at `9a709f20` (2026-09-19) and `1b8b32e3` (2026-04-07) | `sub`, `role`, `phone`, `type=mobile_access`, `exp=now+90 days`, `iat`; **no jti and no sid**. The September variant could still be unexpired at this audit date if that revision was deployed. History alone does not prove deployment. |
| CMS/customer `create_access_token` | `sub`, `username`, `role`, `exp`, `iat`, `jti`, `type=access`; no mobile phone claim. CMS login calls this factory without the role argument, whose default is `admin`, then authorization loads the actual stored admin role. Customer tokens use role customer and a separate user collection. |

Sources: [current mobile factory](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L155),
[Oct 1 mobile factory](https://github.com/MikeCy-com/Lefteria-FC/blob/0e114936f3d925649edf0672b72280c7116be092/backend/routes/mobile_auth.py#L119),
[Sep 29 factory without jti](https://github.com/MikeCy-com/Lefteria-FC/blob/8cf31e8726980b1e489d3d9042447bc9bd6d58d1/backend/routes/mobile_auth.py#L117),
[Sep 19 factory](https://github.com/MikeCy-com/Lefteria-FC/blob/9a709f20358c3758dd68563813a07a6732855a6a/backend/routes/mobile_auth.py#L117),
[Apr 7 factory](https://github.com/MikeCy-com/Lefteria-FC/blob/1b8b32e3b9da36dcb038e2447112680799d99666/backend/routes/mobile_auth.py#L40),
[remember-body presence semantics](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/models.py#L135),
[session policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/session_policy.py#L6),
[CMS token factory](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/auth.py#L43),
[CMS login factory call](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L360).

Current role switching preserves the incoming absolute `exp`, but issues new
`iat` and `jti`. Thus jti is a **token ID**, not a stable device/session ID. The
older Sep 29 switch called the fixed-90-day factory again, renewing expiry; a
bridge cannot reconstruct an earlier device deadline that the old token no
longer contains. Preserve the presented valid token's deadline, never add a new
90-day lifetime. The inspected old/current factories emitted the known mobile
role values; no `admin`→management mobile-role alias was evidenced.
[Current switch](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L568),
[older switch renewal](https://github.com/MikeCy-com/Lefteria-FC/blob/8cf31e8726980b1e489d3d9042447bc9bd6d58d1/backend/routes/mobile_auth.py#L389).

The mobile client stores the JWT as `mobile_token`, restores through Bearer
`/api/mobile/auth/me`, and replaces it after a role switch. Logout unsubscribes
push and clears that installation's storage; no mobile server-side logout or
jti revocation registry was found in the inspected auth path. Browser
`lefteria_session_id`/`x-session-id` is anonymous view deduplication, not a signed
JWT sid. Older no-jti tokens for the same identity issued in the same second can
be identical after JWT NumericDate serialization; exact independent devices
cannot be reconstructed from those claims alone. That last limitation is an
inference from the deterministic payload, not a claim about observed live users.
[Mobile token storage/logout](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/MobileAuthContext.jsx#L13),
[anonymous browser session ID](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/utils/sessionId.js#L1).

Mobile OTP rows contain a transient raw `code`; admin 2FA OTP rows use bcrypt
`code_hash`; invite tokens are separate activation secrets. None is a password,
mobile JWT, or durable verified-identity proof. Do not import live challenges or
activate pending invites as part of a login bridge.
[Mobile OTP storage](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L365),
[admin OTP hash](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L321).

## Proposed requirements: import validation and quarantine

1. Run only an explicitly authorized, versioned offline input against a dedicated
   staging target first. Record source revision, snapshot boundary/checksum,
   collection counts, field-presence counts, import version and reconciliation
   totals. Source audit is not authorization to read production or fetch secrets.
   Keep public reports redacted; protect phone/contact/hash/conflict artifacts.
2. Inventory all records without the detector's caps. Require bounded typed IDs,
   known roles, field types and parseable timestamps/dates. Keep a durable unique
   `(source collection,legacy id)→target id` map and row provenance. Preserve a
   valid UUID where unambiguous; otherwise use an explicit stable mapping, never
   a new random ID on every run. Missing/duplicate subjects cannot be bridged by
   guessing a phone, name, jersey or collection `_id` alias.
3. Produce canonical ASCII international digits with a declared 8–15 digit
   policy and preserve raw phone provenance privately. Validate consistency
   against the actual source normalizer for each origin. Quarantine invalid,
   international-prefix/country ambiguities and normalization collisions; never
   suffix-match, guess a new country, or rewrite an already international number
   merely to make two identities match.
4. Enforce one unambiguous mobile row per canonical `(phone,role)` and a unique
   old subject mapping. Canonically equal phones across different roles are
   expected and remain separate identities. Conflicting duplicates within one
   role, repeated admin usernames, ambiguous phone-only admin/staff/player
   resolution, contradictory explicit/reverse links, or a shared phone implying
   two distinct people must be quarantined. A consistent explicit link takes
   precedence over phone-only fallback. No first/last/latest-row winner or union
   of grants.
5. Preserve actual guardian-child IDs only after validating the child's parent
   contact and roster relationship. Multiple children per parent are legitimate;
   duplicate own-phone players and registration/name-only matches are not proof
   to grant all those children. Validate every group/team/coach FK and explicit
   organization before materializing a relationship. Unknown/empty assignments
   never mean all teams. Review non-coaching staff titles produced as coach by
   the source's broad phone detector instead of silently inventing title aliases.
6. Preserve active, archive, transfer, invite and account-status evidence.
   Contradictory status/link records stay inactive/quarantined until reviewed;
   data is retained for history. Do not create a new active formal role from a
   dormant mobile row, pending registration, staff mirror or old JWT. Independent
   regular fan identity can remain available without restoring the removed club
   role; synthetic fallback must not publish a private formal name.
7. Distinguish missing/null organization fields from an explicit empty list.
   Materialize only the source helper's justified grants, including its narrowly
   documented legacy-super-admin and coach-backfill rules; flag their provenance.
   Unknown roles/IDs and conflicting plural/singular assignments require review.
   Create admin with no active organization, insert validated grants, then select
   only a granted active context. Never seed both organizations by default for
   a manager or elevate legacy `admin` to a canonical privileged role.
8. Import credentials only via a protected operator path, preserving bcrypt
   strings and null pending-invite state. Validate supported encoded hashes
   without logging them. Do not generate a common password, re-enable disabled
   accounts, copy staff mirror secrets, or invoke source startup-admin seeding.
   Any password algorithm upgrade needs verified-password rehashing or a separate
   recovery flow; the mobile-only bridge grants no CMS/customer session.
9. Make dry runs and retries deterministic and transactional. A conflict must
   not partly activate an account or insert its grants. Emit stable reason codes,
   counts and restricted provenance; retain quarantined source evidence without
   exposing raw PII in logs. Reimport cannot overwrite later verified user edits
   or silently resurrect a revoked link. Prove recovery/rollback and reconciliation
   before any cutover; unresolved conflicts remain an explicit cutover gate.

These are proposed safeguards, not assertions that the source currently has
unique indexes, clean canonical phones, complete links or validated passwords.

## Proposed requirements: bounded legacy mobile bridge

- Use a separate, explicitly enabled legacy validator and server-only legacy
  key configuration, with an operator-set cutover window. New rewrite sessions
  retain their own issuer/audience/key contract. Never relax all new-token
  validation globally, accept unsigned JWTs, discover keys from token URLs, or
  expose/reuse the legacy secret in frontend/runtime diagnostics.
- Require HS256, valid signature and unexpired bounded NumericDate `exp`, valid
  `iat`/chronology, exact `type=mobile_access`, bounded nonempty `sub`, a known
  mobile role and valid full phone. Require the fields emitted by the evidenced
  factories. `iss/aud/sid/nbf` absence is expected for those legacy families;
  do not invent issuer/audience aliases. Reject CMS/customer `type=access` even
  though the signature key is shared and role may read `admin`.
- Support both evidenced jti-bearing and no-jti mobile families. Validate an
  optional jti as the bounded token identifier it is; absence must not be treated
  as proof of forgery. Do not use jti, anonymous x-session-id, phone alone or a
  client-supplied sid as a stable cross-device identity.
- Resolve `sub` through the exact imported subject mapping, then require its
  canonical phone and role to agree with the signed claims. No fallback to a
  different same-phone identity when subject/role disagree. Re-read current
  active/link/admin/grant state and keep operational authorization server-side.
  A valid signature cannot restore a quarantined identity or revoked formal role.
- If an internal sid is needed, create a server-controlled mapping scoped to an
  authenticated legacy token fingerprint (keyed digest, not logged raw token),
  with expiry no later than its absolute exp. Treat device provenance as unknown
  where the legacy format cannot prove it. Any replacement JWT keeps that same
  deadline, and subsequent role switches cannot extend it. Do not pretend the
  fingerprint reconstructs devices when two old tokens were identical.
- Decide and document whether existing Bearer calls are directly accepted or
  exchanged once; implement race-safe/idempotent exchange, safe audit and key
  retirement. Read-only token validation must not create accounts or provider
  effects. Preserve availability only for mapped authorized users; unknown,
  ambiguous, expired or unapproved format variants must fail closed and use the
  normal phone-OTP recovery flow. Do not silently wipe working browser tokens
  because a compatibility endpoint or temporary database dependency is missing.
- Include the browser handoff: on an approved same-origin cutover, locate the
  existing `mobile_token`, validate/refresh it through the bridge, then replace
  client storage only after success. Cached `mobile_available_roles` is display
  state and grants nothing. A different origin cannot read the old origin's
  localStorage; seamless restoration there requires a separately designed
  authenticated handoff, not an assumption that the token moved automatically.

Mandatory isolated checks include both historical claim families; missing jti
and missing sid; wrong signature/algorithm/type; CMS token with role admin;
missing/ill-typed/expired/future claims; subject/phone/role mismatch; duplicate
mapping quarantine; disabled/revoked/pending accounts; explicit empty org grants;
same-phone independent roles; exact-expiry exchange/switch; repeat/concurrent
exchange; separate devices where the source distinguishes them; and no token,
secret, password hash or raw phone leakage. Use generated test keys and sanitized
synthetic source-shaped records, not production token examples.
