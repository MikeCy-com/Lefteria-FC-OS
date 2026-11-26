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

The public site has additional page-local style rules, not just shared `public.css`. **The redesign branch has migrated the color literals** in Academy landing, Academy groups/details/registration, First Team, contact, public article and login styles, as well as the three shared CSS files and the remaining major public components. Existing sizes and functional page structures are intentionally retained.

### Current remediation in PR #467
1. Replaced new visual CSS's fixed palette values with `--lf-color-*` variables, including transparent tints derived using `color-mix`.
2. Extended the same CMS schema with **eleven enumerated layout options**, including five default-page measurements. Backend and client accept the original six-option layout and two-key themes; reject unknown CSS and out-of-range values.
3. Changed the WYSIWYG design preview so responsive headings and paragraph typography follow selected desktop/mobile values.
4. Bridged published colors into legacy `--public-gold`, `--public-black`, `--public-panel`, `--public-line`, etc. without affecting unpublished themes.
5. Implemented independent page overrides for max width, block gap, side padding and vertical margins. Each selector includes **Use global default** and an all-fields reset control. Saved legacy explicit dimensions stay explicit; absent/null fields inherit the owning organization's *published* global defaults. Existing `--cms-*` block geometry remains authoritative.
6. Migrated the shared `public.css`, `club-polish.css`, `home-reference.css`, major native pages and 10 public Svelte component styles to semantic `--lf-color-*` values with the original palette as fallback. Photo overlays preserve their image source and use the selected background tint. Cookie consent now inherits the enclosing public organization's published theme. Added source-level guards and isolated browser checks for Academy, registration, contact and team.

## Important remaining limits

The redesign now gives shared and high-priority native page visual colors a single palette source; **it does not make every feature-specific layout or data-driven native component individually editable in the WYSIWYG canvas**. Saved content blocks have per-page and per-block control; functional pages retain some native component geometry. Complete visual comparison across live CMS variants and actual published content remains a separate release gate.

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

Each organization publishes an independent global design; Academy values cannot affect First Team pages. Explicit native `WebsitePageBlocks` block geometry takes priority over inherited page defaults. Existing images remain under their original media components. Their overlay tints now derive from the global palette without replacing the photos or media URLs.

**Important:** WYSIWYG page-level override controls apply to CMS pages and CMS sections. Hard-coded native feature templates still retain some page-specific presentation rules; they share the published defaults through `--public-*` variables but are not all converted into individually editable WYSIWYG canvas pages. The editor does not control the private admin/member apps.

## Authoritative CSS cascade (not four competing themes)

The public shell imports styles in this fixed order:

1. `public.css`: original component geometry, Fira typography and fallback tokens only.
2. `club-polish.css`: established photographic/editorial composition, using those tokens.
3. `global-design.css`: the **published, organization-scoped** WYSIWYG contract for typography, seven colors and eleven layout dimensions. These values are injected on the `.public-site` root from a validated, published canonical CMS record.
4. `ui-refresh.css`: presentation-only Juventus/Ferrari-inspired refinements. **Every tint, gradient, border, shadow, card spacing and corner uses the published tokens** and respects any page/block editor override.

All legacy color literals in the audited shared/public files are kept **only inside `var(--lf-color-*,original-fallback)`** or derived `color-mix` expressions, not as a competing override. If no theme has been published, those fallbacks retain the original Lefteria palette.

No child Svelte component is permitted to establish a new independent brand palette. The regression suite monitors the base stylesheets, native Academy/registration/contact/team pages and shared public cards/dialogs. The admin and member app deliberately have **separate** design systems; they are unaffected by website editor settings.

### Visual direction

Luxury Juventus editorial photography + Ferrari-like precision: crisp typographic hierarchy using original Fira fonts, club black/approved gold, deliberately restrained depth, low-noise cards, accessible interactive states and slim accent borders. This is **design language**, not copied Juventus/Ferrari brand assets. User-uploaded logos, text, fixture/team data and real photos remain unchanged. No new font-size, photo asset or fixed accent is introduced by the premium skin.

### Rollout

Backend and frontend schema changes (including nullable page overrides) **must ship together**, after green builds and browser testing. Leave PR #467 a draft until a real published CMS staging theme has been tested for both organizations and the screenshots have been reviewed.


## Native-page cascade cleanup (follow-up)

Several layers previously assigned different geometry to the same public
`.public-section` and `.public-page-heading` elements. In particular, the
published max-width could be correct on `.public-site` while an old 28px
section padding from the premium skin bypassed centered content width. The
authoritative native-page inset now resolves:

```css
padding-inline:max(var(--lf-layout-gutter,28px), var(--public-page-inset,24px));
```

`--public-page-inset` is computed from **the published global page max width
and side gutter**. `ui-refresh.css` no longer sets those two elements' horizontal
padding; it retains presentation-only gradients, borders, focus and shadows.

The About, catalog, player, match, sponsor and shop *page containers* now
consume published max-width / gutter tokens. Narrow article columns, forms,
cards and imagery retain their intentional size constraints. Without an approved
published theme, the original **1200px** narrower match/story widths and
**1400px** standard native content widths remain in effect.

Standalone WYSIWYG articles no longer receive horizontal padding from both the
parent and nested block grid. An embedded CMS section likewise owns its local
header width, side padding **and block gap** independently; other published
sections are not touched. The original About page's fixed `text-white`,
`text-zinc-300`, `bg-[#0a0a0a]` and `text-[#F6BB38]` utility classes have
been replaced by semantic theme colors, without changing words or the real
stadium and crest images.

Regression protection now covers all 35 public route templates' CSS **and
markup paint classes**, native container widths, and computed browser geometry
under an injected non-default published theme (About, Shop and Team).
`global-design.css` remains the sole source of published geometry: an
unpublished draft never changes the live public UI.

Remaining limit: native feature pages are token-driven and inherit global
layout, but only CMS page/block records expose independent per-page WYSIWYG
overrides. Making every business-logic page's full layout editable requires a
separate native-page settings schema/editor, not a risky second CSS overlay.
