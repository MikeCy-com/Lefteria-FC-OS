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
