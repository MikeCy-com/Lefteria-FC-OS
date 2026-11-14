# Historical public website parity notes

This records the original migration design. See the current implementation and tests for shipped behavior.

The user's current priority is restoring the original website's typography,
navigation, submenu behavior, page content and working public journeys. The
previous six-link approximation does not satisfy that requirement. This record
is an implementation contract and gap inventory, not a claim that all listed
routes have been ported or deployed.

Primary source: original `MikeCy-com/Lefteria-FC` GitHub main
`380b4022f0ab0db95fbc52a8613711160a5037a5`, fetched through the GitHub connector.
The coordinator also prepared an exact GitHub blueprint under the ignored
frontend build directory. Local historical application clones are not evidence
of deployed behavior. Work is isolated in `fix/public-website-parity`; the
uncommitted CMS editor worktree is frozen and production/domain stay unchanged.

## Shell, menus and typography

Original [navigation definition](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/frontend/src/club-site/navigation.js)
and [interactive navigation](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/frontend/src/club-site/PublicNavigation.jsx):

| Root item | Original destinations |
| --- | --- |
| ΑΡΧΙΚΗ | `/` |
| ΣΥΛΛΟΓΟΣ | `/about`, `/join`, `/seasons`, `/partners`, `/sponsors/first-team`, `/sponsors/academy` |
| ΟΜΑΔΑ | `/team`, `?tab=roster`, `?tab=results`, `?tab=schedule`, `?tab=gallery`, `?tab=venues`, `/trials`, `/partners?scope=first_team` |
| ΑΚΑΔΗΜΙΑ | `/academy`, `/academy/philosophy`, `/academy/groups`, `/academy/registration`, `/partners?scope=academy`, dynamic group links |
| ΑΓΩΝΕΣ | `/team?tab=schedule`, `/team?tab=results`, `/team?tab=overview`, `/vote`, `/shop` |
| ΝΕΑ / ΕΚΔΗΛΩΣΕΙΣ / ΚΑΤΑΣΤΗΜΑ / ΕΠΙΚΟΙΝΩΝΙΑ | `/news`, `/events`, `/shop`, `/contact` |

Rail category buttons open a modal drawer directly at that category. The header
menu opens the root menu. A selected category shows only its submenu; opening
or changing category focuses the title. Path or query navigation closes/resets
the menu. Escape, focus containment, close control and returning focus belong
to the modal behavior. Dynamic group/player/staff/news/match/season links use
accent-stripped title slugs, `--`, then an opaque ID, with raw-ID URL support.
Academy group links are real `/academy/<slug>--<id>` routes, not page fragments.

Header controls are menu, brand/founding date, search, push bell, CUSTOMER
account, cart count and gold tickets link. The account links to `/profile` or
`/login`; tickets link to `/shop`. Mobile hides the ticket button, not customer,
cart or search. Substituting mobile `/app` or administrator `/admin` login loses
the original customer journey.

Search uses flattened navigation, dynamic Academy groups and the latest100
global news articles. It ignores Greek accents/case, deduplicates paths and
returns up to16 links, with real loading/error/empty states. Public requests
must not inherit CMS cookies or role selectors.

The source stylesheet uses Fira Sans400/500/600/700 for body and Fira Sans Extra
Condensed500/600/700/800/900 for headings/navigation. Public letter spacing is0;
heading/card defaults are deliberately smaller than the initial rewrite.

| Viewport | Source dimensions and typography |
| --- | --- |
| Desktop | Rail116px, header76px; rail labels12px/icons23px; header brand25px/founding12px; hero h1 58px. Drawer left116px, width360px, full100dvh; root rows19px, submenu14px. |
| ≤1199px | Rail100px; labels11px below icons; drawer left100px; search text hidden; hero h1 46px; header padding20px. |
| ≤767px | Rail hidden, header64px/padding12px; brand18px/founding9px and30×40 logo; tickets hidden; drawer left0/max width viewport−20px. Page h1 38px, hero40px. |
| ≤374px | Header padding5px; brand17px/logo hidden; icons31px/push28px; hero35px. |

Global source heading h2 is18px, h3 19px; mobile16px/17px. Buttons/inputs have
at least44px targets. Original HomepageRefinements changes destination card
headings to17px,14px below1024 and12px below481, preserving whole Greek words.
Reduced-motion removes animation, transitions and smooth scrolling.

The footer has city photo/banner, club/public contacts,12 quick links, customer
account/orders/cart/tickets/register, three separate social groups (first team,
Academy and legacy club), back-to-top, install control, privacy and terms. It
uses four desktop columns, two below1200, one below481, retaining two quick-link
columns on narrow phones. The source installation action uses an actual browser
install prompt or device-specific help; it does not claim an install succeeded
when no prompt is available.

## Route and data inventory

| Public route family | Actual source page behavior and data |
| --- | --- |
| `/about` | App.js static history, values, crest and stadium sections. The stale literal league position/13–2–3 record must not become current generated statistics. |
| `/team` and tab query | Overview, roster, results, monthly schedule, gallery, venues. Players First Team; fixtures limit200; standings, staff, club and current non-Academy season. Roster position filter; results status filter; calendar month navigation/day selection; gallery category filter/lightbox; venue map links. |
| `/academy` | Source introduction, philosophy/group/registration actions, actual `/academy/stats`, public enquiry form and sponsor/partner strip. |
| `/academy/groups`, `/academy/:groupId` | Group catalogue and detail. Detail breadcrumbs, coach/training/season, roster/schedule/stats/gallery tabs, scoped fixtures and match detail. |
| `/player/:playerId`, `/staff/:staffId` | Public identity, sporting/biography fields, source tab behavior and safe share links; broad source API responses must be replaced by explicit DTOs. |
| `/match/:fixtureId` | Public fixture detail/report, score/status/events and source read-only match information; private squad/RSVP/contact data is excluded. |
| `/partners`, `/partners/:slug` | Optional first_team/academy scope, active partners, type labels, source partner content, contact CTA preserving scope/referral. |
| `/sponsors/first-team`, `/sponsors/academy`, `/sponsors/:id` | Type-filtered sponsor cards and detail; public branding/description/website/social fields only. |
| `/events`, `/events/:id` | Published marketing event list/detail, filters and actual RSVP submission; participant/contact lists remain private. |
| `/seasons`, `/seasons/:id` | Archived season list and detail with verified historical fixtures/standings/roster/public content. |
| `/news`, `/news/:id` | Global default feed, existing categories/featured article/detail, explicit FC/Academy filters where offered. Shared items appear once. |
| `/contact` | Actual public contact details plus name/email/subject/message submission. Partner CTA query survives navigation. |
| `/join` | Despite the menu's member label, source page is Academy trial enquiry; preserves optional `partner` referral and full-registration link. |
| `/trials` | First-team trial settings/open/closed state and real application. Positions Goalkeeper/Defender/Midfielder/Forward, preferred-foot Right/Left/Both. |
| `/academy/philosophy`, `/privacy`, `/terms` | Complete original static content and legal cross-links, not generic headings. Legal text retains its original February2026 publication date and is not a new compliance attestation. |
| `/shop`, `/login`, `/register`, `/profile`, `/cart`, `/checkout`, `/checkout/success`, `/vote` | Customer realm and real cart/order/payment/voting contracts below. These cannot be declared functional because their nav links exist. |
| Compatibility redirects | `/fixtures`→team results; `/calendar`→team schedule; `/venues`→team venues; `/staff`→team overview. |

Safe public section data is being mapped to a separate initialized
`GET /api/public/sections?organization_id=<known>` capability, retaining private
original evidence and binding its provenance to the initial public snapshot.
Unavailable import must remain503, not an invented successful empty response.
Only a complete known empty collection can render source empty-state text.

## Public projection field evidence

Source intentional anonymous UI fields and source filters provide the publication
basis; an overbroad anonymous API response alone does not. Do not invent blanket
adult/DOB/consent gates that remove source public squads/coaches. Apply source
active/archive/transferred and membership/organization filters, with concrete
record-level quarantine only when evidence is ambiguous.

- Team cards/roster: player ID, name, number, position, image, sporting age/
  nationality and the displayed statistics goals/assists/minutes/appearances.
  Sporting detail can include public biography/preferred foot/previous club names.
- Academy squad cards: ONLY ID, display name, number and image. The source
  `formatAcademyDisplayName` returns full first name plus first TWO surname
  letters and a dot; player URLs use that abbreviation too. Project this name
  server-side, not only in the visible text. No raw surname in public JSON/SEO.
- Public coaches/staff: name, role, image, nationality, biography, qualifications,
  joined date and prior experience array `{role,club,years}`, plus scoped
  team/group assignment IDs needed for intentional coach links.
- Never expose DOB, guardian/contact provenance, medical/injury records,
  contracts, private documents, admin identities/credentials, invitation/session
  metadata or full imported rows. Original public profile contact/DOB leakage
  does not justify adding those fields to safe DTOs.
- Standings/fixtures/calendar: source sporting IDs/names/scores/dates/competition/
  season/status; calculate results only from real matching records. Preserve
  season/team-type metadata needed for actual filters.
- Gallery: bounded public image/title/category/description and safe public
  association IDs. Private document/media paths never become image delivery.
- Venues: source public name/address/city/country/capacity/surface/map/coordinates/
  home-ground flag. Partners/sponsors/events/products require strict nested
  allowlists; source staff/player ID arrays and RSVP/contact rows are not event
  public output.

## Customer and public form contracts

Customer identity uses original `users`, distinct from mobile OTP and CMS:

- Register: name,email,password, optional phone/age/sex/country. Email is trimmed
  and lowercased, name trimmed, bcrypt password preserved. Source minimum is six
  characters. Login takes email/password and null/30/60/90 remember choice.
- Source sets Secure/HttpOnly/SameSite=Lax `user_access_token` and returns a
  customer JWT which React stores separately as `customer_token`. The rewrite
  must isolate the customer realm; an administrator/mobile session is not a
  replacement customer login. Server-side logout/session revocation and fresh
  ownership checks apply to the target implementation.
- Own profile fields: name,phone,address,city,postal_code,age,sex,country. Password
  change requires current_password/new_password. Account and order history are
  private; no public list or cross-user ID lookup.
- Product add: `{product_id,quantity,size}`; ticket add: `{ticket_id,quantity}`.
  Update/delete key includes product ID and size. Cart/count are customer-owned.
  Server enriches current product/ticket prices; client prices/totals are not
  authoritative. Prices need decimal transactional handling.
- Checkout: shipping_name/address/city/postal_code/phone and notes. Card request
  `/checkout/stripe-session` adds origin_url and source web; source503 explicitly
  offers cash-on-delivery instead of reporting paid success. Success polls
  `/checkout/status/:sessionId` until a verified paid state. Payment providers
  remain adapters, with staging isolation and no production charges.
- Original cash-order handler iterates products only and silently omits ticket
  rows. Do not reproduce that data-loss defect: mixed cart pricing, inventory,
  atomic order creation and cart clearing need real regression coverage.
- POTM uses public results and customer-owned check/vote/withdraw, selecting an
  eligible real first-team player. Private voter identities do not become public.

Public enquiry posts `/marketing/leads/public`: name, phone/email (at least one),
nullable player_age, interested_in, message, consent_gdpr and optional
consent_marketing. Source is website or partner_referral, preserving partner
context. Success appears only after actual server persistence.

Academy registration is the original five-step child/address, guardian/emergency,
medical, consent/payment and signature flow. POST `/registrations` forces Academy
ownership and returns only message/ID. Its full child/DOB/guardian/emergency/
medical/signature/consent record stays private; it must not enter public section
DTOs or logs. Required participation/medical/GDPR/liability/financial consents,
explicit media yes/no and signature are validated; marketing consent remains
optional. A registration is not automatic player creation, role assignment,
payment completion or proof that a separate roster row has matching media consent.

First-team trials posts `/trials/submit` with full_name,DOB,phone,email,position,
preferred_foot,previous_club,years_played,height_cm,weight_kg,notes, after actual
trial settings confirm applications open. These private applicant fields are
not public roster fields. Contact and event RSVP likewise need bounded persistent
records, honest errors and no fabricated confirmation or provider side effects.

## Acceptance and implementation status

Compare actual original and preview at desktop, tablet,390px and narrow mobile:
fonts, sizes, rail/header, every submenu, focus/Escape/close, search, every
menu destination, footer links, filters/tabs, image fallback and forms. Test
success against stored fixture data and test failed/unavailable requests. Do not
deploy a broken destination list as completed functionality or silently redirect
the preview to the live original.

The shell/home/global CSS is owned by the frontend specialist; route pages and
page components by the public-site specialist; new public section backend/import
contracts by the backend specialist. Legal/About/Philosophy source ports have
started; richer data routes and customer/form workflows are still in progress.
The coordinator records integrated tests, hosted acceptance and publication.

