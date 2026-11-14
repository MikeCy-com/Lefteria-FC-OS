# Phase 3A journeys: public design and club data

Compare against live `https://lefteriafc.cy` (read-only) with the same route and width. Phase 1 and 2 journeys stay valid.

- **JRN-35**: open `/` on the OS and on live at 1440 px.
  - expected: same body font (Manrope 16 px), the same header height, rail or sidebar layout (icon and label side by side, 11 px), section order, h1 size and footer. No match countdown on the OS. The header button reads tickets as on live.
- **JRN-36**: open `/` at 390 px on both.
  - expected: the same mobile header and menu, h1 at 38 px, the same section order, and page height within 10%.
- **JRN-37**: open `/` in a fresh browser on the OS.
  - expected: the cookie-consent banner appears with the same text and buttons as live. After accepting and reloading, it no longer shows.
- **JRN-38**: open `/team` at 1440 px.
  - expected: the same first-team players as live, with photos (not club placeholders), in the same card layout and font sizes.
- **JRN-39**: open a player who has no team on live (an academy/unassigned player) on the OS.
  - expected: the player page renders (no 404) with the same sections as live.
- **JRN-40**: open `/academy/groups`.
  - expected: the same 6 academy groups as live. Opening one (`/academy/:groupId`) shows its players, fixtures and training info as on live.
- **JRN-41**: open `/news`, then an article.
  - expected: the 11 articles with their real images. The article page width, title size and body font match live.
- **JRN-42**: open `/partners`, `/sponsors/first-team`, `/staff`, `/staff/:staffId`, `/venues`, `/seasons`, `/seasons/:archiveId`, `/fixtures`.
  - expected: each matches live in content count, images and typography. No 404 and no 500.
- **JRN-43**: open `/checkout` and `/checkout/success`.
  - expected: both render with the original layout (no 404). Card payment shows a disabled or unavailable state and never fakes success.
- **JRN-44**: request a public image URL from a news card.
  - expected: HTTP 200 with an image content type and cache headers.
- **JRN-45**: run the visual baseline script.
  - expected: typography matches on at least 95% of captures, page height is within 10% on at least 85%, and no route returns 404 on the OS while it returns 200 on live.
- **JRN-46**: sign in at `/app/login` with the parent phone and code 123456, then open `/admin/login`.
  - expected: mobile still works. Owner setup is still offered (or the owner account exists, if the user has already created it).
