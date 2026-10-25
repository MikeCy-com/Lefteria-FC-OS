# Profile, avatar and composer media source contract

Audited through the GitHub connector on 2026-10-25. Original repository main
resolved to `5111f06af5d8b17f855ceed9637caca02db117e2`; all source links below
pin that commit. This describes source behavior and requirements for the next
rewrite slice. It does not establish the deployed production revision, select
a new storage provider, or claim media is implemented in Lefteria-FC-OS.
No provider, production database or uploaded binary was accessed.

## Accepted composer flow and pending PR

The accepted [feature queue](feature-queue.md) requires: +, device photo/video
selection, upload, Next, text with hashtags and @mentions, preview, publish.
Parent mode has no + button. The current rewrite navigation also omits the
composer for coach and management. UI availability never grants API permission.

This staged flow already has backend and frontend evidence in original main:

1. Photo/video multipart creation uses empty content, `audience=private`, and
   `publish=false`. It returns a post ID, not a reusable public file URL.
2. A photo becomes `draft`; a video stays `processing` until its durable job
   produces the MP4/thumbnail and releases the post to `draft`.
3. Next is enabled only for `draft`. The editor keeps the returned post ID and
   version; publish edits that draft through `PATCH /api/social/posts/{post_id}`.
4. Cancel/replacement deletes the staged post. Processing video cancellation
   also cancels its job and marks objects for cleanup. Failed cleanup remains
   explicit; a failed upload must not be presented as a successful public post.

Sources: [staging/Next/cancel](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/SocialNetworkPage.jsx#L653),
[post edits](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L1854),
[post deletion/job cancellation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L2067).

[Original PR #42](https://github.com/MikeCy-com/Lefteria-FC/pull/42) was **open,
unmerged** at review, head `5599ef99b8670b06e9480dcc283c060516ee10c6`, base
`5111f06`. Its 15 changed files are frontend layouts/navigation/tests. Its social
page patch changes public profile presentation, not upload/composer/backend
contracts. It is pending parity/visual evidence, not shipped behavior. Its
singular “Under Development” wording does not supersede the accepted exact
rewrite label **Under Developments**.

## Upload and edit routes

All mobile/social routes below require a valid current mobile identity. Social
routes also require an active social account; writes lease the account write
gate so approved account deletion can drain in-flight writes.

| Route | Request and result | Source limits and storage |
|---|---|---|
| `POST /api/mobile/profile/avatar` | Multipart `file`; returns `{avatar_url}` and updates the authenticated identity. | 5 MiB; optional declared MIME allowlist JPEG/PNG/WebP/GIF. Filename `{identity_id}_{sha1(bytes)[0:12]}.{ext}` under `avatars/`; API URL `/api/uploads/avatars/...`. No social moderation approval. |
| `POST /api/social/posts/with-media` | Multipart repeated `files`, `content` (default empty), `audience` (default private), optional `community_id`, `fixture_id`, `event_id`, `publish` (default true). Returns `{item}` containing post ID/version/status and media DTOs. | 1–4 photos by default, 5 MiB each; bounded reads and decoded JPEG/PNG/WebP sanitization. UUID media IDs; private keys `private_attachments/social/{media_id}.jpg`. |
| `POST /api/social/posts/with-video` | Multipart `file`, `content`, `audience`, `publish`; optional fixture/event/story-chain reference, `soundtrack`, `soundtrack_rights_confirmed`. Returns `{item}` with `processing` status. | One MP4/MOV input, 25 MiB and 60 seconds by default; MIME admits `video/mp4`, `video/quicktime`, `application/octet-stream`, with real validation deferred to the worker. Private source, MP4 and thumbnail keys. |
| `PATCH /api/social/posts/{post_id}` | JSON `content`, `audience`, `fixture_id`, `event_id`, `publish`, `expected_version`; rejects extra fields. Returns `{item}`. | Owner/authorized official manager only; compare-and-set version, 409 on conflict. Media publication/editing enters `pending_review`; client cannot set `status` or replace arbitrary media IDs. |
| `GET /api/social/me/posts` | `status`, `limit` 1–100 (default 30), `offset` 0–10000; `{items,next_offset}`. | `status=video_status` returns own `processing`, `pending_review`, `processing_failed` videos; used to poll processing. `status=draft` retrieves drafts. |
| `DELETE /api/social/posts/{post_id}` | Owner/authorized manager; returns `{deleted:true}`. | Version/state-checked soft removal, media tombstones, pending cleanup, queued/processing jobs cancelled. |
| `POST /api/social/me/profile/cover` | Multipart `file`; returns `{cover_url,message}`. | JPEG/PNG/WebP, 5 MiB default, same sanitized-image pipeline, adult-only, private media ID/key. Cover media becomes `published` immediately; this route does **not** use post-review moderation. |
| `DELETE /api/social/me/profile/cover` | Owner; `{cover_url:null,deleted:boolean}`. | Compare-and-set current cover, tombstone old media and retain cleanup retry state. |

Sources: [mobile avatar](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1743),
[photo upload](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L712),
[video upload](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L924),
[cover upload/removal](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L1219),
[edit schema](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L232),
[own post polling](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L1765),
[account write gate](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_write_gate.py#L27).

Photo/video requests accept optional `Idempotency-Key` matching
`[A-Za-z0-9._:-]{8,128}`. Its SHA-256 is scoped to the social principal; a unique
`(author_id,idempotency_key_hash)` index prevents duplicate posts. Retry returns
the prior item; replacement needs a new key. Media DTOs expose `id`, API `url`,
`content_type`, `size`, dimensions, optional duration/thumbnail URL, and status.
They do not expose storage credentials or private object keys.
[Idempotency and response](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L669),
[unique indexes](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L6093).

## Validation, moderation and privacy

**Avatar gap to address when porting:** the source avatar route reads the entire
file before its size check. It accepts an omitted declared MIME and does not
decode/sniff/re-encode bytes or reject an empty body. Extension controls storage
MIME through the shared helper. Preserve its URL/ownership contract while adding
bounded reads and genuine image validation; do not copy these weaknesses into
the new public delivery path. The general validator used by CMS uploads is a
different function, not proof the avatar uses it.
[Avatar handler](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1754),
[shared MIME helper](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L246),
[bounded CMS validator](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/upload_validation.py#L10).

Social photos are decoded, format-checked to JPEG/PNG/WebP, limited to 16 million
pixels, EXIF-oriented, resized within 2048×2048, converted to RGB, then rebuilt
as JPEG quality 86. Source metadata/EXIF/comments/profiles are not copied. Client
MIME or filename alone cannot pass this check. Photo publication always goes
to `pending_review`, even when global text-post review is disabled; a private
draft is not a published post.
[Sanitizer](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L634),
[photo state transition](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L867).

Video processing uses FFprobe, a real video track, MOV/MP4 container detection,
positive dimensions up to 4096 per side/16 million pixels, and the configured
duration limit. FFmpeg rebuilds H.264/AAC MP4 within 1920×1920, strips metadata
and chapters, enables fast start, bounds output size, and creates a JPEG preview.
The source permits two processing slots per process; FFprobe/encode/thumbnail have time
budgets and a `file,pipe` protocol allowlist. Optional ClamAV scanning reports
`unavailable` when absent; it is not a proven mandatory malware scan. The source
Dockerfile installs FFmpeg, not ClamAV. An optional soundtrack is capped at
10 MiB, requires its feature switch and rights confirmation, and is probed as
audio-only, at most 300 seconds. Do not claim a third-party moderation/AI service
or mandatory scanner exists.
[Video inspection/transcode](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_video.py#L20),
[soundtrack request validation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L948),
[source container dependencies](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/Dockerfile#L5).

Social media upload requires the current role's `posts` capability, relevant
feature switches, no maintenance pause or applicable account/media/posting
restriction, active profile moderation, and recorded adult status. Unknown age
is restricted; all accounts attached to a principal contribute to the age
decision. Public posts require a public adult profile. Photo audience permits
public/followers/friends/private/community with the relevant access checks;
video omits community. Safe fixture/event references are mutually exclusive.
The source defaults all five social roles' posting capability to true; the
accepted rewrite + visibility is narrower and does not replace backend checks.
[Role/feature defaults](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L19),
[age policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L515),
[upload authorization](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L724).

Moderation decisions use `POST /api/admin/social-control/posts/{post_id}/decision`
with publish/reject/remove; content managers need super-admin or explicit
marketing/social write permission. Post and media states transition together;
mention notifications wait for publication. Rejection/removal hides media and
retains moderation evidence until the configured appeal/evidence window permits
cleanup. This is distinct from technical image sanitization/video transcode.
[Review decisions](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L4529),
[content-manager authorization](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L280).

Captions default to a 2000-character limit. Hashtags are case-folded, distinct,
2–30 word characters each, at most ten. Mentions resolve handles on the server,
default maximum five, respecting feature/privacy/age/visibility settings;
`GET /api/social/mentions/suggest?q=...&limit=...` is authenticated and throttled.
User-entered text or a selected handle cannot grant access or create arbitrary
notification recipients.
[Hashtag extraction](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L83),
[mention suggestions](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_network.py#L661),
[publish-time mention resolution](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L815).

## Delivery, ownership and identifiers

| Delivery contract | Required behavior |
|---|---|
| `GET /api/social/media/{media_id}?thumbnail=true` | Bearer-authenticated. Active social account and feature switches. Owners may view their draft/pending/published/archived post media; others need a published post, published media and `can_view_social_post`. Ineligible/missing items return 404. Processing source binaries are never served. |
| Profile-cover media | Current published cover only. Non-owner additionally requires public/discoverable, active unrestricted adult profile, and block/mute checks. Knowing a media UUID grants nothing. |
| Social response headers | `private, no-store`, `nosniff`, CSP sandbox, inline safe filename. Video supports a single bounded byte range, 206/416, and thumbnail uses the same authorization. Frontend fetches authenticated bytes, not an unauthenticated `<img>` shortcut. |
| `GET`/`HEAD /api/uploads/{path}` | Shared public/legacy delivery. Reject empty/dot/traversal/percent/backslash/NUL paths and any `private_attachments` segment; tombstones prevent delivery. Avatar/player/gallery/media URLs are ordinarily public and immutable-cached for 30 days. Private folders and legacy chat have separate authentication checks and no-store. |

Sources: [social delivery and ranges](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L1378),
[shared upload delivery](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/upload_delivery.py#L56),
[authenticated frontend media fetch](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/SocialNetworkPage.jsx#L991).

Mobile avatar synchronization follows explicit linked player/staff/admin records;
it does not copy a parent's photo to a child. The source includes a management
phone fallback when exactly one active admin matches; the rewrite must rely on
its verified imported links rather than inventing ownership. Player image writes
also update linked player identities. Public social profile `avatar_url` comes
from `approved_avatar_url`, not the freshly uploaded mobile avatar; no avatar
approval-write route was found in the inspected profile/social modules.
[Avatar linkage](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/profile_media.py#L13),
[linked player image upload](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L1547),
[public social avatar projection](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L829).

Keep mobile identity ID, social principal ID, post ID, media ID, registry ID and
object key distinct. The source social principal joins verified role accounts
for a phone using a stable earliest account ID; it never exposes that phone in
social APIs. This must not merge operational role permissions. Post/media/job
IDs are UUIDs; registry IDs are UUIDv5 derived from relative paths. Import needs
an explicit legacy mapping and must preserve existing API URLs and canonical
provider keys instead of regenerating them from a display name or filename.
[Social principal](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L490),
[registry metadata/IDs](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/storage_registry.py#L28).

## Storage provider and required configuration

The source uses Emergent Object Storage through the integration proxy. Its
adapter exposes put/get/delete, prefixes `lefteria-fc/uploads/{relative_path}`,
initializes a storage key from server credentials, and persists the provider's
returned canonical path. Upload success requires remote storage **and** durable
registry metadata. Provider outages fail explicitly; reads do not convert an
outage to a false 404. Transient network/500/502/503/504 failures get one bounded
retry, a 404 can refresh the key, and quota/configuration failures map to safe
503 responses. No key/provider response body goes to the client.
[Storage adapter](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/storage.py#L28).

The registry records relative path, canonical storage path, MIME, bytes, folder,
private flag, original filename, API URL, timestamps and deletion tombstone.
Actual delivery serves an untouched legacy local file only when its registry
record is absent; otherwise it uses remote storage. A failed registry does not
authorize falling back to publicly serving a private or deleted object.
[Registry](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/storage_registry.py#L10),
[legacy/local decision](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/upload_delivery.py#L73).

| Configuration/dependency | Verified source behavior; rewrite prerequisite |
|---|---|
| `INTEGRATION_PROXY_URL`, `EMERGENT_LLM_KEY` | Required by source storage initialization. Keep server-only behind the storage adapter. An operator must confirm the existing account/namespace and export/restore access before any real provider integration. No credentials belong in fixtures, source or browser config. |
| `MONGO_URL`, `DB_NAME` | Source registry metadata dependencies. Replace metadata persistence with PostgreSQL as part of the media migration; these are not proposed rewrite runtime settings. |
| FFmpeg/FFprobe, writable temporary workspace | Needed by the video worker. Preserve restricted subprocess protocols, time/resource/output bounds and safe temporary cleanup. Optional `clamscan` availability must be reported truthfully. |
| Feature flags | Source defaults media/video/soundtrack/mentions off; profiles/posts on. Read persisted server switches through `/api/social/features`, not environment guesses. Preserve maintenance and per-role capability controls. |
| Size/rate/storage limits | Defaults: image 5 MiB, video 25 MiB/60 s, 4 images/post, 8 media uploads/day, 500 MiB/member, 10 posts/hour, 2000 caption characters, 5 mentions. Settings are authoritative; client prechecks mirror server limits. |
| Storage quota and jobs | Reserve quota atomically including sources, outputs, thumbnails, pending reservations and incomplete deletion. Durable video jobs need lease/retry/cancel/terminal state, recoverable source cleanup, and audit. No process-local fire-and-forget replacement. |
| Retention/backups | Defaults include export retention 7 days, moderation evidence 180 days, appeal window 168 hours; terminal video jobs expire after 30 days. Snapshot PostgreSQL metadata, canonical keys, legacy files and remote objects as one recoverable system before cutover. |

Source jobs claim a 10-minute lease, resume expired leases, and use bounded
exponential retry delays. Processing becomes terminal after eight attempts,
except a completed transcode still needing source cleanup remains retryable.
Successful cleanup removes the raw video/soundtrack keys; orphaned, cancelled
or failed objects retain explicit pending/complete cleanup state. These are
observable lifecycle states, not a reason to expose unprocessed source bytes.

Sources: [storage configuration/error handling](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/storage.py#L45),
[limits/defaults](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/social_policy.py#L148),
[feature contract](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_control.py#L2076),
[quota reservation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L201),
[leased worker/retry](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/social_media.py#L276).

## Adjacent media contracts and implementation acceptance

The shared storage layer also serves separately scoped consumers. Do not route
them through a generic public social upload:

- CMS player/general images: admin-authenticated, 5 MiB, shared decoded-image
  validator, `/api/uploads/players/...`; player updates additionally enforce the
  administrator's organization and update linked player identity avatars.
- CMS media library: `/api/admin/media/upload`, multipart file/tags/description/
  alt_text, 20 MiB and shared image/document/video validation; stores media asset
  metadata separately from the registry under `media/{uuid}.{ext}`.
- Conversation attachments: `/api/mobile/conversations/{cid}/attachments` and
  the admin equivalent require actual conversation participation; default
  10 MiB. Private keys are delivered by `/api/attachments/{id}` with fresh
  conversation/announcement visibility checks. The old unscoped mobile chat
  image uploader is closed.

Sources: [CMS images](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L1547),
[media library and auth wrapper](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/media_library.py#L64),
[scoped attachments](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/communication_media.py#L75),
[closed legacy uploader](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1782).

Before enabling the next rewrite slice, prove bounded malformed/MIME-spoofed/
oversized input rejection; metadata stripping; owner/stranger/minor/unknown-age/
blocked/muted and cross-organization denials; private draft/thumbnail/range
delivery; moderator-only publication; idempotent retries; concurrent quota
reservation; cancel-versus-worker races; explicit provider failures; durable
cleanup retry; and preserved legacy IDs/URLs. Use in-memory/fake storage and a
disposable database first. Live provider, account import, backup/restore and
production cutover remain separately verified deployment work.
