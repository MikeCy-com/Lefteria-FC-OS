# Academy private player profiles · implementation and staged acceptance

**Status:** Isolated draft feature branch, not merged or deployed. Applies to verified Academy children in the administrative roster only.

## What is implemented

A player card offers a private editor showing the verified official name, shirt number and position as read-only source-of-truth data. Staff can edit a separate Academy biography (1,000 characters), preferred playing position and preferred jersey number (1–99), then upload or remove a private photo. The portrait is never exposed as a public media URL. Uploads go through the existing 5 MiB multipart limit and JPEG/PNG/GIF/WebP image sanitizer, which decodes, strips EXIF, limits the output to 1 MiB and stores a private sanitized JPEG in PostgreSQL. A cached private image is refreshed after changes.

The profile has an optimistic concurrency revision, so stale edits fail with HTTP 409 rather than overwrite a colleague's changes. The server validates identity, role, Academy grant, live active status and **all** current child-group memberships every time it reads the profile or portrait. Write operations additionally enforce exact city/location write grants for the whole membership set. Super Admin may edit unassigned children; managers may not read outside their sites or edit children assigned to inaccessible sites.

The new tables have RLS enabled and no direct runtime table privileges. Runtime admins can only invoke purpose-specific SECURITY DEFINER functions; the mobile app role has none of these permissions. Writes and photo changes record audited staff identities. Recovery refuses to delete existing child portraits/bios silently.

## Data boundaries and outstanding work

- **Official player name, kit number, verified birth date and guardian identity:** intentionally unchanged. The new editable preferences are private overlays and do **not** silently rewrite COMET registrations, invoices, attendance, parent access or match results.
- **Public player image:** not implemented by design. Publication requires a separate guardian photo-consent and moderation workflow with verified opt-in and explicit release.
- **Media Storage Manager:** this slice uses private database-backed sanitized portraits (same design as admin self avatars), not the shared public file manager. Migration to an encrypted object store should preserve permissions and audits.
- **Full editable official registration form, guardian document workflow and financial status:** additional source-of-truth amendments need separate workflows and tests.
- **Site managers:** can edit their complete assigned child scope only; finance remains independently authorized. The parent app is untouched.

## Acceptance checklist

1. Run EF migration in isolated CI database, dedicated role grants, and disposable rollback-only SQL fixtures.
2. Verify a Super Admin can open a real Academy player, save biography, upload JPEG/PNG/WebP, see sanitized portrait, remove photo and refresh.
3. Verify unauthorized site manager cannot enumerate, read or upload another site's player photo (including stale/revoked session).
4. Verify mixed-site memberships fail closed, concurrent stale save returns 409 and official name/registration and financial history remain unchanged.
5. Validate no photo cache, public URL, EXIF or cross-child access; storage failure returns clear errors without partial updates.

**Do not deploy to production or process real minors' portraits until the above and parental privacy notices/consent policies are verified.**
