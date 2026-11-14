# Reference UI and role workflow audit

Baseline: OS main ab6d1167f1fa9cc15ef7f0e73b5a3789b2259b49, 2026-11-15. Work is GitHub-only; production remains unchanged. This is an incremental audit, not a declaration that every component or API has reached parity.

## Reference policy
Use the supplied complete desktop homepage references (four news cards) for homepage composition, the dedicated matches/academy images for those pages, and the later parent/player/coach/access images for those experiences. Preserve the existing homepage carousel behavior and desktop stage height. Retain every supported action even when it is absent in a mockup, especially parent family settings. Real API content, accessible controls and authorization take precedence over fictional mockup dates, counts and names.

The exact font and clean photographic layers cannot be recovered reliably from a flattened screenshot. Existing licensed Fira Sans / Fira Sans Extra Condensed are the working font family; real italic faces are now loaded instead of relying on prohibited font synthesis. Exact photographic, glyph and pixel equivalence remains unverified. Record browser screenshots and computed font metrics, not just code assertions.

## Implemented in this increment
- Shared self-hosted upright/italic fonts cover public, admin and mobile entry routes; external Google CSS requests removed from admin/mobile styles.
- Homepage category icons, equal news-card composition, match details CTA and news-before-promotion ordering; all API content and filters retained.
- Admin access header, brand/form hierarchy, role badges and responsive sizes; username authentication, password clearing and 24h/30/60/90-day session choices retained.
- Admin live module cards restored where the dashboard previously rendered a disabled fallback. Academy office follows existing role and organization policy.
- HUB and schedule sidebar access and active state.
- Member matches uses the existing scoped calendar with match filtering, including RSVP, cancellations and staff participation responses. This does not add legacy score/crest contracts.
- Parent-only legacy family-link alias; no family actions removed.

## Confirmed workflow gaps
| Surface | Evidence in legacy | Current OS evidence / remaining work |
| --- | --- | --- |
| Staff lifecycle | StaffTab.jsx create/edit/archive/unarchive/delete | Coach directory and assignments exist; complete staff lifecycle absent |
| Admin users | backend/routes/admin_users.py list/create/update/reset/invite/delete | Authentication/setup exist; account lifecycle contract absent |
| Fixture editor | FixturesTab.jsx CRUD/CSV/opponent creation | Admin fixture write contract absent |
| News editor | Legacy admin content management | /api/admin/website/news is GET-only |
| Parent | Parent dashboard and child-detail workflows | Family/calendar/fees/learning/chat present; child attendance/profile/team-detail parity incomplete |
| Player | Player dashboard, MatchesPage.jsx and social/development pages | Calendar matches restored; social/composer/shop/history endpoints absent |
| Coach | Coach dashboard and roster/training/attendance pages | Feedback composer is not a full roster; mobile attendance recording/statistics/training-plan history missing |
| Management | Management dashboard | teams and financials still fall through to unavailable state |
| Fan | Fan dashboard/gamehub/predictions/POTM/shop | Public news/match/team routes work; gamehub/predictions/voting/checkout/social remain incomplete |
| Public academy/team | Dedicated reference pages and legacy navigation | Preserve existing dedicated routes and tabs; exact section composition and every inner-route visual state still need verification |

OS evidence: frontend/src/lib/admin/module-catalog.ts, AdminWorkspace.svelte, AdminFrame.svelte; frontend/src/lib/mobile/navigation.ts, components/MobileCalendar.svelte, components/MobileFamily.svelte, routes/app/[...path]/+page.svelte; contracts/openapi.json. Admin-cookie attendance/evaluation endpoints must not be reused in mobile JWT workflows without appropriate scoped contracts.

## Verification and next work
Browser audit now includes anonymous admin login using a synthetic 401 response, computed heading typography and an actual Greek italic font load at 390/768/1440/3840 widths. These checks detect rendering/overflow/font failures; they are not screenshot pixel-diff acceptance.

Remaining visual checks include real data states, mobile hero total height, all role dashboards, matches/academy detail routes, and exact image crops. Add missing workflows as reviewed vertical slices with server authorization, API callers, realistic error states and regression coverage. Never replace unavailable functionality with buttons that simulate success.

The reviewed chat “Review Latest Merge” ended with deployment of 0c0b1073 awaiting terminal access. That operational blocker was superseded by this chat's verified testing deployment of ab6d1167 and confirmed owner login; do not repeat owner recovery or initial database import.
