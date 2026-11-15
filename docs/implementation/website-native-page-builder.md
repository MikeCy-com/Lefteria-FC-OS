# Native Website Page Builder — first implementation slice

Status: branch implementation; staging migration/SQL role grants and CI must be
verified before use. No live deployment or data migration is authorized.

## Delivered in this slice

- Native `/admin/website-pages` editor, with organization selector, new and
  existing *CMS-created* page editing, structured blocks (headings, paragraphs,
  images, CTA buttons), instant visual preview, keyboard-accessible block
  reorder controls, SEO metadata, and navigation group/order selection.
- Organization-scoped private drafts with optimistic versions. Edits to a
  published page do **not** change its public snapshot automatically.
- Separate manager/super-admin publication and unpublication, both protected
  by a fresh admin-session lock, exact organization grant, role capability and
  atomic audit. Editor/Marketing can write drafts but cannot publish.
- An allowlisted public SQL projection that reveals only published snapshots,
  no raw HTML, no draft payload, credentials, or private versions.
- Server-rendered organization-aware navigation, and safe public `/pages/[slug]`
  pages. Academy and First Team pages do not mix.

## Existing content and deliberate limits

Existing first-team, Academy, news, club profile, matches, sponsors, and app
pages retain their routes and current backend contracts. Existing club/news
editors remain available; this first slice does not claim to convert every
legacy page to blocks. Sponsors, media uploads and social publishing remain
separate pending modules.

New CMS pages use stable namespaced URLs `/pages/{slug}`, so no user-controlled
slug can shadow existing private/public routes. The Academy link retains an
explicit organization query. Menu insertion is by selected parent and zero-based
position; the pre-existing menu is preserved.

Images are approved HTTPS or local paths, not arbitrary uploaded HTML.
Publication remains human-controlled. There are no plugins, arbitrary widgets,
script injection, or unreviewed external integrations.

## Staging rollout

1. Apply versioned migration `20261115910000_WebsitePages` on an isolated
   staging database using explicit migration credentials.
2. Apply reviewed `ops/postgres/identity-app-grants.sql` and
   `ops/postgres/web-admin-auth-grants.sql` for that database only.
3. Run the respective runtime permission audits and verify both roles cannot
   read `public_content.website_pages` except the admin-auth role.
4. In staging: create a draft as Editor; deny publication, verify not publicly
   visible; approve as Manager; verify site and nav; change draft and verify
   public snapshot unchanged; republish, then unpublish.
5. Confirm a manager with only Academy grant cannot list, edit or publish
   First Team pages (and vice versa); verify revocation/conflict handling.
6. Back up staging before migration. Rollback refuses destructive down
   migration if pages or publication audit history exist; forward recovery is
   required.

## Follow-on slices

Convert approved existing page regions into page blocks without losing current
dynamic sports/registration elements; build a dedicated scoped media library
and sponsor/partner CRUD; add navigation hierarchy depth, reusable templates,
revision diff/restore, workflow approvals, responsive layout block properties,
and reviewed social account integrations. None is implied to be live here.

## Homepage hero editorial integration

The follow-on slice adds two **fixed, owner-scoped native CMS records**:
`homepage-hero-first-team` (`lefteria_fc`) and `homepage-hero-academy`
(`lefteria_fc_academy`). Admin → Website → Pages has a dedicated starter action
per granted organization. Each record contains exactly one line-break-aware
heading block and one paragraph block, and is restricted to a hidden menu entry.
Edits are private until the existing audited, manager-approved publication.

The existing homepage (`/`) uses approved text only for the matching slide.
No approved snapshot means precisely the former hero text and markup remain.
The carousel, first-team banner, match card, countdown, buttons, Academy links,
styles, and sport data are never CMS-replaced. Hero snapshot slugs are not
standalone `/pages/` routes or user-selectable menu destinations, preventing
SEO duplicates. No browser auth/session data is used to read these snapshots.

This does **not** enable editing every existing static section, nor does it
allow arbitrary HTML or plugin hooks. More canonical homepage and site regions
still need individually verified slot contracts to preserve live features.

## Navigation owner policy

CMS menu parents are validated by the API (including forged requests), filtered
in the admin editor, and checked again in the public navigation projection.
The Academy may not attach its pages to the First Team `team` or `matches`
menus; First Team pages may not attach to the Academy `academy` menu.
Shared root, club/news/events/shop/contact slots retain their existing
organization-specific pages. Incompatible pre-existing snapshots remain
available through their direct scoped URL but cannot appear in the wrong
organization's public menu until corrected and republished.

## Native page search indexing

Indexable production sitemap discovery now includes only the anonymous,
**published** CMS projection for each verified organization. The sitemap does
not expose editor drafts, hidden menu entries, reserved homepage hero slots,
or snapshots attached to a different organization's menu. It includes the
publication date when valid. Non-production origins remain fully blocked from
sitemap generation, before either CMS projection is requested.

Academy CMS URLs keep `?organization_id=lefteria_fc_academy` in their canonical
and social `og:url` metadata; First Team pages have the clean path. Untrusted
query parameters and spoofed organization identifiers are dropped. Changes
are code-only and require normal staging verification before deployment.

## Accessible native block arrangement

The CMS editor now provides an explicit drag handle for pointer-based block
reordering, alongside the existing keyboard/mobile **up and down** controls.
Dropping uses an in-session index only (never arbitrary external drag payloads)
and is disabled for read-only users, homepage hero slots, and in-flight saves.
Blocks can also be duplicated with one click, preserving text/URLs while
respecting the 40-block content limit; all changes remain private drafts until
separately saved and approved. Editor movement and duplication are pure,
nonmutating operations with automated boundary tests. No untrusted HTML,
plugins, or media uploads are introduced by this improvement.

## Published media browser and image block picker

The dedicated /admin/media page is an **owner-scoped, read-only browser** of
already publicly visible image URLs from gallery, sponsors, partners and events.
It uses only the anonymous public-sections DTO, never sensitive player records,
families, original import evidence or document storage. Users must be signed in
with gallery:read and a current grant for the selected organization. Every
record is re-filtered by that owner, and only safe local or HTTPS image URLs are
accepted. A failed/uninitialized publication is shown as an error, not as an
invented empty library.

In the native Visual Builder, an image block can open the same browser and
populate its URL (plus optional alt text) without introducing a second upload
store. The editor can still use a manually entered safe URL. Image selection
changes **only the local private draft**; saving and manager publication stay
separate and fully audited by the existing CMS backend.

This does not claim to implement uploads, media deletion, private asset ACLs,
license clearance, resizing or a media-publishing pipeline. Those remain
separately gated follow-up work.

## Canonical Academy spotlight editing

The existing Academy spotlight on the public homepage has a separate fixed
CMS editorial record: `homepage-academy-spotlight`, owned only by
`lefteria_fc_academy`. Academy-granted editors can create/edit a private,
hidden draft with exactly two text blocks: a three-phrase headline (using
the existing highlighted middle-dot separators) and a description.
The server independently rejects wrong-owner, reordered, extra, oversized
or differently positioned blocks. A manager/super-admin must separately
approve publication through the existing audited CMS flow.

Only approved, structurally valid copy replaces the current spotlight text.
Without it, the exact legacy headline and description are rendered. The
background image, actual Academy group count, layout, styles, link and
destination remain owned by their original dynamic components. This fixed
slot cannot be opened as a public /pages URL or indexed in the sitemap;
it is excluded from navigation even if an older record is malformed.
There are no new database tables, permissions or Hostinger deployments.

## Sponsor and partner publishing administration

The first native sponsor/partner editorial slice is in `/admin/sponsors`.
It lists **only current, already imported public sponsor/partner records**
for each explicitly granted organization. It preserves their canonical
source IDs and imported public details; it does not create a new independent
sponsor catalog. Each editor can propose an audited, optimistic-concurrency
draft (name, short description/tagline, sponsor logo/partner cover image,
HTTPS website, display order). There is a visual preview and an image
picker restricted to already published images from the same owner.

A current `sponsors:read` + organization grant is needed to load records.
`sponsors:write` authorizes drafts, and a manager/super-admin with the same
current grant must separately approve publication or unpublish changes.
Each request rechecks the real account, active credential-bound session,
owner grant and imported record identity *inside the same locked transaction*.
A conflicting version is rejected, never silently overwritten. Publication
copies only an approved draft into a separate published snapshot and writes
an actor/session/owner security audit event. Unpublishing restores the source
record in the anonymous catalog; imported source and provenance are immutable.

The public runtime cannot read private drafts or the new table; the narrow
`read_published_sponsorship` security-definer function exposes only
published fields to the existing First Team / Academy public-sections
projection. The original sponsor and partner detail routes, related
benefits, personnel, slugs and layouts are retained. The admin side has
no cross-owner fallback and distinguishes an unavailable published source
from a valid empty list.

This slice does **not** implement sponsor creation, contract/payment lifecycle,
invoicing, cancellation, files or public sign-up. Apply the EF migration and
separate least-privilege grants in an **isolated staging database** before
testing. No automatic Hostinger or production deployment is performed.
