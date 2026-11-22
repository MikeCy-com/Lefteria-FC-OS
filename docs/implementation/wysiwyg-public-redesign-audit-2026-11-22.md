# Lefteria FC OS — global WYSIWYG / UI redesign contract

Audit: 2026-11-22. Scope: `frontend/src/routes` (69 SvelteKit `+page.svelte` templates), shared public shell and styling, editor and backend publication.

## Ownership: do not create a second hardcoded theme

| Surface | Source of truth | Applies to |
| --- | --- | --- |
| Public H1–H6 / paragraph / button / navigation / caption / label / card text | Published `canonical-global-design` (12 text roles) | First Team and Academy public routes, by organization |
| Public background, foreground, accent, surface, muted, border and link colors | Published `canonical-global-design` (7 colors) | Public shell, shared components, and UI refresh |
| Public section gutter, section gap, card gap, card radius, button radius, homepage motto height | **Optional validated `layout`** in the same published record | Public shared components and UI refresh |
| Individual WYSIWYG page width, block gap and side padding | Each page's `WebsitePageLayout` | `WebsitePageBlocks` only |
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
2. Extended the existing CMS schema with **six enumerated layout options**. Both server validation and client parsing reject arbitrary CSS, invalid values and extra properties. Two-key legacy theme records remain supported.
3. Changed the WYSIWYG design preview so responsive headings and paragraph typography follow selected desktop/mobile values.
4. Bridged published colors into legacy `--public-gold`, `--public-black`, `--public-panel`, `--public-line`, etc. without affecting unpublished themes.
5. Kept existing `--cms-*` page/block geometry isolated, so global changes do not erase a page editor's saved layout.
6. Added source-level regression tests and browser checks for theme precedence, contrast-validated schema and existing original content/actions.

## Important remaining limits

The redesign does not by itself migrate every historical hardcoded native component color or all page markup into the WYSIWYG schema. A full native component-by-component palette migration and per-page preview comparison is a **separate release gate**. Do not claim that the entire 69-template system is visually CMS-controlled yet.

The admin and mobile member apps are not public website themes. The independent admin UI PR #468 must not accidentally adopt an Academy/First Team public theme or alter role scopes.

## Release gate

- Confirm complete web/API build and screenshot CI, including 390px, 768px, 1440px, 1672px and 4K.
- Test a published **non-default**, readable palette and customized typography/spacing on First Team **and** Academy, separately.
- Verify a saved but unpublished draft has **no** effect on public pages; a legacy two-key published theme still renders correctly.
- Compare actual images/text/navigation, registration/checkout, CMS page layout and interactive elements.
- Keep PR in draft; do not merge or deploy until tests and browser inspection succeed.
