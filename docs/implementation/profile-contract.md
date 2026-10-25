# Mobile and social profile source contract

Audited directly through the GitHub connector at source commit
`5111f06af5d8b17f855ceed9637caca02db117e2` on 2026-10-25. GitHub main is not proof
of the deployed revision. The team-preference write described below is now
implemented in the rewrite; other profile writes and social reads remain queued.

## Implemented team preference

`PUT /api/mobile/auth/me` updates only the signed-in identity's `favorite_team`
and returns `{ok:true,user}`. The repository locks current identity/admin status
before writing and appends the success audit in the same transaction. The request
cannot choose a target identity, phone or role; unknown fields are ignored, as in
the source allowlist. An empty change returns 400; revoked roles cannot write.
Preferences remain separate for parent/fan and every other identity on one phone.

New writes accept `first_team`, `academy`, `both`, or explicit null to clear.
This deliberately tightens the source's unconstrained value to the three choices
its consumers understand. Invalid values/types return 422. Existing unsupported
stored values remain readable and are not silently rewritten; the editor asks
the owner to choose an available value. No schema or data migration is needed.

The fan profile destination contains the real preference editor. Its controller
uses the generated client, keeps current profile data on failed writes, rejects
mismatched returned identities and ignores late responses after logout. Header
profile still explicitly enters fan context. Other edits use **Under Developments**.

Observed: 19/19 HTTP/PostgreSQL groups and 37/37 frontend tests pass; API/test builds
and Svelte check report zero warnings/errors; production frontend build passes.
The profile suite covers per-role persistence, forged body targets, signed
binding mismatch, explicit null, rejected inputs, revocation and audit actor.

## Private mobile identity and preferences

These routes require a mobile Bearer token and act on the authenticated identity,
never a caller-supplied account ID. Separate role identities retain separate
preferences; matching a phone does not merge their private profiles.

| Source route | Fields and behavior |
|---|---|
| `GET /api/mobile/auth/me` | Selected identity, refreshed current-role name and `available_roles`; persisted `favorite_team`, `first_name`, `last_name`, `birthday`, `favourite_player_id`, `notification_prefs`. Missing fan registration fields may fall back to the fan row by full phone. |
| `PUT /api/mobile/auth/me` | Only `favorite_team`; other keys ignored. No accepted key gives 400. Returns `{ok:true,user}`. Source does not validate its value; consumers recognize `first_team`, `academy`, `both`. |
| `PUT /api/mobile/profile` | Non-null `name`, `email`, `first_name`, `last_name`, `birthday`, `favourite_player_id`; whole-object `notification_prefs` with values cast to booleans. Derives `name` from first/last when omitted; persists `updated_at`. Phone is excluded. Returns updated own mobile identity. |
| `POST /api/mobile/profile/avatar` | Multipart `file`, maximum 5 MiB; declared MIME, when present, checked against JPEG/PNG/WebP/GIF. Shared upload storage and content-addressed filename; returns `{avatar_url}`. This does not approve a public social avatar. |
| `GET /api/mobile/players-list` | Authenticated favourite-player picker: active First Team players, sorted by number, maximum 50; `{players:[{id,name,number,position,image_url}]}`. |

Sources: [own identity and preference routes, lines 599–636](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L599),
[profile/picker/avatar routes, lines 1701–1779](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1701).

The profile-update allowlist applies to every source mobile role despite its
fan-oriented comment. Only fan writes mirror `first_name`, `last_name`, `name`,
`birthday`, `favourite_player_id`, `notification_prefs` into the fan record by
phone. A target implementation must preserve ownership and field names while
adding typed validation; weak source dictionary/value validation is not a policy
to reproduce. Neither update accepts role, grants, linked players or organizations.

## Public-facing social profiles

“Public” describes the target profile's visibility, not anonymous HTTP access.
Readers require an active `fan`, `parent`, `player`, `coach` or `management`
identity. Profiles and player cards have independent server feature switches.
Social principals may connect verified role identities, but must not expose their
account IDs or relax role-specific operational ownership.

| Source route | Read scope and response |
|---|---|
| `GET /api/social/profiles/{handle}` | Normalized lowercase handle without leading `@`; active target principal/moderation, no account restriction, known adult status, block/mute checks. Private target requires owner, active follow, or accepted friendship when enabled; otherwise 404. |
| `GET /api/social/players/{player_id}/profile` | Resolves active, unarchived, untransferred roster player through an active player identity to a public, discoverable social handle, then applies the same profile policy. No raw player document. |
| `GET /api/social/profiles/{handle}/posts` | Profiles/posts switches, search throttle, active/restriction/block/mute checks and per-post visibility. Non-owner requires adult public/discoverable profile and published public-audience posts. Owner may read own published posts, including the minor-profile exception. `limit` 1–30, default 12; `offset` 0–10000; `{items,next_offset}`. |

Sources: [role eligibility](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L267),
[profile and player resolution](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L2739),
[profile posts](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L1802).

The base public projection is exactly `handle`, `display_name`, `bio`, approved
`avatar_url`, `official_badge`, `member_type`, `official_account_type`, `created_at`,
plus protected `cover_url`. It excludes principal/account IDs, phone, raw role,
DOB/age, guardian links and moderation metadata.
[Projection allowlist](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L822),
[cover projection](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L334).

Public/discoverable profiles may add `social_counts:{posts,followers,following}`.
Adult fan profiles expose `city` only with `show_city`, and `supporter_progress`
only with `show_supporter_progress`. Progress contains real scored-prediction
aggregates: `points`, `predictions`, `exact_scores`, `level`, `level_name`,
`next_level_points`, `progress_percent`, `favorite_team`, `badges`; no account IDs
or individual prediction history. Player profiles may add
`football_profile:{handle,display_name,member_type,player_cards}` with only
admin-approved safe card fields. Official club profiles have a separate safe
football projection.
[Fan/player additions and opt-ins](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L2772),
[aggregate/card builders](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L1656).

Unknown DOB remains private. Player age comes from canonical linked player
records, including same-phone player identities, so switching to fan does not
bypass youth restrictions. Public profile GET rejects minor targets; owner setup
uses the separate `/social/me/profile` route.
[Age policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L425).

## Owner social settings and private player cards

- `GET /api/social/me/profile`: owner projection plus `city`, `profile_visibility`,
  `discoverable`, `minor_restricted`, private `profile_completion`. Fan owners get
  actual counts/progress; player owners get their own safe football card.
- `PATCH /api/social/me/profile`: only `handle` (3–30), `display_name` (1–60),
  `bio` (≤240), `city` (≤80; fan only), `profile_visibility` (`private`/`public`),
  `discoverable`. Extra fields forbidden; social-write throttle, maintenance and
  moderation checks; public/discoverable denied for minors; duplicate handle 409.
- `GET/PATCH /api/social/me/settings`: separate consent settings, including
  `show_city`, `show_supporter_progress`, relationship visibility, comments,
  mentions, follow/friend requests and message audience.

Sources: [owner profile](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L2627),
[patch allowlist](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L342),
[settings](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L2926).

`GET /api/mobile/players/{player_id}/profile` returns
`{player_card,viewer_scope}`: player only own linked player; parent requires both
linked player and current matching canonical guardian phone; coach/management
require current intersecting team/group assignments. Fan has no private fallback.
Owner/guardian get all safe fields; coach/management staff get approved fields
plus display name. Published sessions still require field approval for every
viewer. Source legacy admin branches must not create new mobile roles
or elevate target management. `/api/mobile/my/player-card` is player-only;
`/api/mobile/my/player-cards` provides the scoped collection.
[Viewer scopes and routes](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L1827).

Card approval fields: `avatar`, `display_name`, `position`, `shirt_number`,
`team_affiliation`, `approved_goals`, `approved_assists`, `approved_appearances`,
`approved_yellow_cards`, `approved_red_cards`, `nationality`, `height`,
`preferred_foot`, `career_history`, `published_sessions`. The output uses
`avatar_url`; approved statistics may include `season_statistics` and
`recent_matches`. Populate these from real finished fixtures, stored statistics
and published session snapshots, never invented sample data.
[Approval allowlist](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L726),
[safe card builder](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L1406).
