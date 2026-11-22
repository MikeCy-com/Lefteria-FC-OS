# Lefteria FC OS — global WYSIWYG / UI redesign contract

Audit: 2026-11-22. Scope: `frontend/src/routes` (69 SvelteKit `+page.svelte` templates), shared public shell and styling, editor and backend publication.

## Ownership: do not create a second hardcoded theme

| Surface | Source of truth | Applies to |
| --- | --- | --- |
| Public H1–H6 / paragraph / button / navigation / caption / label / card text | Published `canonical-global-design` (12 text roles) | First Team and Academy public routes, by organization |
| Public background, foreground, accent, surface, muted, border and link colors | Published `canonical-global-design` (7 colors) | Public shell, shared components, and UI refresh |
| Public section gutter, section gap, card gap, card radius, button radius, homepage motto height **plus default page width, block gap, side padding, top/bottom margin** | **Optional validated `layout`** in the same published record (original six keys or extended eleven) | Public shared components, native shell and WYSIWYG pages |
| Individual WYSIWYG page width, block gap, side padding and top/bottom margins | Each page's **optional, per-field** `WebsitePageLayout` | Overrides only that page and only its set fields |
| Individual WYSIWYG block width, min height, top/bottom margins | Each block's `WebsiteBlockLayout` | `WebsitePageBlocks` only |
| Homepage hero image, Academy images, First Team photos, player photos and news media | Existing published media/source data | No static replacement permitted |
| Admin and authenticated mobile-app UI | Separate permission-scoped administration/member shells | **Not** governed by the public website editor |

The public shell resolves `publishedGlobalDesign(page.data.websitePages[organization], organization)` from validated, tenant-scoped published CMS snapshots; drafts do not change live pages.

## Routes inspected

Repository tree and all 69 route templates inspected for local inline CSS and hardcoded color literals:
- **35 public templates** including home, Academy landing/details/groups/registration, First Team, match/player/news, event/sponsor/partner catalogs, checkout, contact, shop, legal pages and CMS article pages.
- **32 admin templates** including dashboard, organization management, teams, Academy, communications, news, marketing, finance, POS, users, settings and the WYSIWYG website editor.
- **2 authenticated mobile member application templates**.

The public site has additional page-local style rules, not just shared `public.css`. Higher-volume existing fixed color declarations were seen in Academy landing, Academy group detail/directory, registration, First Team, contact, public article detail and public login. These are **pre-existing architectural gaps**, not proof that every existing native page is already fully driven by the editor.

### Current remediation in PR #467
1. Replaced new visual CSS's fixed palette values with `--lf-color-*` variables, including transparent tints derived using `color-mix`.
2. Extended the same CMS schema with **eleven enumerated layout options**, including five default-page measurements. Backend and client accept the original six-option layout and two-key themes; reject unknown CSS and out-of-range values.
3. Changed the WYSIWYG design preview so responsive headings and paragraph typography follow selected desktop/mobile values.
4. Bridged published colors into legacy `--public-gold`, `--public-black`, `--public-panel`, `--public-line`, etc. without affecting unpublished themes.
5. Implemented independent page overrides for max width, block gap, side padding and vertical margins. Each selector includes **Use global default** and an all-fields reset control. Saved legacy explicit dimensions stay explicit; absent/null fields inherit the owning organization's *published* global defaults. Existing `--cms-*` block geometry remains authoritative.
6. Converted original gold accents in the key public templates and shared CSS to the published accent token, bridged native public surfaces/brand variables to the palette and added focused inheritance/legacy security tests alongside responsive browser checks.

## Important remaining limits

The redesign does not by itself migrate every historical hardcoded native component color or all page markup into the WYSIWYG schema. A complete audit of all older native page-local gradients, media overlays and editor preview comparison is a **separate release gate**. Do not claim that the entire 69-template system is visually CMS-controlled yet.

The admin and mobile member apps are not public website themes. The independent admin UI PR #468 must not accidentally adopt an Academy/First Team public theme or alter role scopes.

## Release gate

- Confirm complete web/API build and screenshot CI, including 390px, 768px, 1440px, 1672px and 4K.
- Test a published **non-default**, readable palette and customized typography/spacing on First Team **and** Academy, separately.
- Verify a saved but unpublished draft has **no** effect on public pages; a legacy two-key published theme still renders correctly.
- Compare actual images/text/navigation, registration/checkout, CMS page layout and interactive elements.
- Keep PR in draft; do not merge or deploy until tests and browser inspection succeed.

## Inheritance example (same organization)

Global design publishes `page_max_width=1440`, `page_gap=32`, `page_side_padding=24`, `page_margin_top=48`, `page_margin_bottom=64`.

- Page A has no `layout` → inherits **1440 / 32 / 24 / 48 / 64**.
- Page B publishes `layout: {"gap":8}` → effective **1440 / 8 / 24 / 48 / 64**; no other page changes.
- Page C already saved `{"max_width":1120,"gap":16,"side_padding":16}` → continues using **1120 / 16 / 16** for those three; vertical margins inherit **48 / 64**.
- Clicking **Επαναφορά όλων στις γενικές ρυθμίσεις** removes Page C's local overrides in its *draft*, taking effect only after explicit publication.

Each organization publishes an independent global design; Academy values cannot affect First Team pages. Explicit native `WebsitePageBlocks` block geometry takes priority over inherited page defaults. Existing images and hero gradients are left under their original media components; changing the palette must not silently replace photos.

**Important:** WYSIWYG page-level override controls apply to CMS pages and CMS sections. Hard-coded native feature templates still retain some page-specific presentation rules; they share the published defaults through `--public-*` variables but are not all converted into individually editable WYSIWYG canvas pages. The editor does not control the private admin/member apps.
