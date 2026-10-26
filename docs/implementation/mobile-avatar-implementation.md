# Own mobile avatar implementation

This slice implements the account editor's public mobile avatar. It does not
approve a social avatar, add generic upload delivery, import accounts or infer
roster links. Source behavior is pinned in [media-contract.md](media-contract.md).

## Request and image boundaries

`POST /api/mobile/profile/avatar` requires the mobile Bearer policy and exactly
one multipart file named `file`. It returns `{avatar_url}` with no-store headers.
The handler manually streams to bounded memory; default form buffering and raw
temporary files are not used. File size is capped at 5 MiB, multipart overhead
at 64 KiB, boundary at 70 characters, section headers at 16/4096 bytes. Empty,
extra-field and multi-file requests fail.

`IAvatarImageSanitizer` is an Application port with a real SkiaSharp adapter.
Actual codec recognition admits JPEG/PNG/WebP/GIF, checks declared MIME when
supplied, requires complete containers and successful first-frame decoding.
Input is limited to 16 million pixels and 8192 per side before allocation.
Fresh sRGB pixels apply all eight EXIF orientations, scale without upscaling
within 1024x1024, flatten alpha white and encode JPEG quality 86, at most 1 MiB.
Only that JPEG reaches persistence; original names, bytes and EXIF are not kept.
Image sanitization does not provide content/abuse moderation.

Two native processing slots per process have no waiting queue; saturation
returns 429 with Retry-After. Native decode is synchronous and cannot be
cooperatively stopped mid-call. Cancellation is checked between stages.

Authorization occurs before body reads. Decode runs outside DB transactions;
commit repeats current signed subject/full-phone/role/active/admin checks under
the existing profile identity/admin locks. Revocation during decode denies
commit. Same-phone role identities keep independent avatars.

## Storage and delivery

`identity.mobile_avatars` contains one current row per identity: cascading PK/FK,
unique opaque filename, canonical JPEG bytea, dimensions, UTC update time and
the pre-first-upload previous_avatar_url. Database constraints cap bytes and
dimensions and enforce JPEG markers/name shape; the codec validates the image.
Avatar bytes, identity avatar_url/updated_at and the mobile.avatar.update /
own_avatar actor/device audit commit atomically. Replacements overwrite the row
and retain the original previous URL.

Public GET/HEAD `/api/uploads/avatars/{fileName}` admits only 32 random lowercase
hex characters, underscore, 64 lowercase SHA-256 hex characters, `.jpg`.
Responses use image/jpeg, nosniff, CSP sandbox, a fixed inline name and
public/max-age=2592000/immutable caching. HEAD returns the same length and no
body. Invalid/missing names return 404. No user ID, source filename, provider
redirect or generic/private/social path is served.

Replaced generated URLs return 404 at origin. Existing immutable browser caches
and downloaded public copies can retain old bytes; origin removal cannot erase
those copies. Old imported URLs are not rewritten or fetched. Legacy-file
delivery remains separate import/cutover work.

Runtime grants allow SELECT/INSERT and column UPDATE of filename/JPEG/dimensions/
time. Ownership, rollback provenance, DELETE/TRUNCATE and DDL are denied.
Apply administrator grants after the migration. Probes use rollback-only rows.

Migration `20261025172802_MobileAvatars` Down restores previous_avatar_url only where the current identity URL
still matches that row's generated URL, then removes the avatar table. Unrelated
later/imported URLs remain untouched. Down loses newly stored JPEG bytes: an
approved DB backup is required to recover those images when moving forward.
Logical live storage is one image per identity; PostgreSQL MVCC/WAL/backup
retention and vacuum still need capacity monitoring after replacement.

No external storage credentials, provider account or upload directory is added.

## Dependency and verification

SkiaSharp and executable SkiaSharp.NativeAssets.Linux.NoDependencies are pinned
to 4.153.1. Retain their upstream MIT license and applicable native notices in
distribution. The native package works without fontconfig; managed restore
alone does not establish published-container decoding.
Verified package license/native notice text and provenance are retained in
[backend/third-party/SkiaSharp](../../backend/third-party/SkiaSharp/README.md).
[Package](https://www.nuget.org/packages/SkiaSharp/4.153.1),
[Linux assets](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1),
[release](https://mono.github.io/SkiaSharp/docs/releases/4.153.1.html),
[license](https://github.com/mono/SkiaSharp/blob/v4.153.1/LICENSE.md).

`dotnet LefteriaFc.Api.dll --check-avatar-codec` encodes/decodes/sanitizes synthetic
1x1 pixels and exits 0 or 1 before auth/DB configuration, without external calls.
Run it in the actual published Linux image. Upstream cautions that native Skia
vulnerabilities may not all appear in NuGet advisory results; keep native assets
current. A clean package audit is not a zero-vulnerability guarantee.
[Native dependency guidance](https://github.com/mono/SkiaSharp/blob/v4.153.1/documentation/dev/dependencies.md).

Four guarded real HTTP/PostgreSQL groups cover public GET/HEAD, digest/headers,
replacement/role isolation; invalid MIME/dimensions/size/truncation; all eight
EXIF orientations, metadata/alpha, GIF/WebP; identity/admin revocation after
decode; and exact-identity PostgreSQL failure proving atomic rollback. The
test-only post-decode gate still executes the real codec. Source checks alone
do not establish success; record actual build/run results in the handoff.

Observed local verification on 2026-10-25: the guarded, restricted-role fixture
run passed all 27 HTTP/PostgreSQL groups with zero failures. Exact-owned cleanup
completed; a follow-up read-only check found zero avatar rows and zero temporary
failure triggers/functions. Security audit rows remained append-only. Published
Linux image codec execution is a separate CI check; local Docker was unavailable.
