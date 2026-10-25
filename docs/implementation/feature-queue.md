# Rewrite feature queue

The rewrite target is MikeCy-com/Lefteria-FC-OS. Existing behavior is pinned to
MikeCy-com/Lefteria-FC at 5111f06af5d8b17f855ceed9637caca02db117e2, verified on
GitHub on 2026-10-25. GitHub main is not proof of the deployed revision.

## Accepted requirements from the other chat

Retrieved from **Lefteria FC developer agent**, chat
01a14eb0-6895-7013-8c3f-6a11d3bfcc82:

- Turn 01a16f68-283f-7312-ba39-398e99a176f3 (2026-10-24): match the supplied role
  screenshots exactly; preserve the global role changer, notification and profile
  header; retain current fonts, with smaller sizes allowed. Unsupported functions
  use the exact label **Under Developments**.
- Turn 01a16a12-58e1-7373-9440-913bc496afa4 (2026-10-23): the user's correction
  retains separate switching for parent/fan and every other role. Parent mode
  must have no + button. Preserve the FC/Academy organization selector.
- Turn 01a16899-7a08-7c40-8ed3-12f76df143c2 (2026-10-23): isolate FC and Academy
  statistics, players, payments, games, training and all operational data.
  Backend assignments may grant FC, Academy or both to a person. Removing a club
  role leaves the person able to use their regular fan identity.
- My Profile opens the public/fan profile home, with an overflow/settings menu.
- Composer: +, device photo/video selection, upload, Next, text with hashtags and
  @mentions, preview, publish.
- Player profile functions must be completed; role layouts, banner logo and club
  wordmark follow the supplied references.

The prior no-switching/unified-role proposal is superseded by the later explicit
role-switching correction. Navigation never grants data authorization.

The older comprehensive queue could not be retrieved because a historical chat
turn exceeds the reader's response limit. Do not invent requirements for it.

## Implementation order

This order follows the dependencies of the accepted requirements:

1. Repair and validate framework builds, dependency locks and container startup.
2. Implement identity/session policies, explicit role selection and organization
   scope, then wire phone OTP, persisted grants, API contracts and generated client.
3. Build the authenticated mobile shell and header; preserve /app paths, query
   aliases, role switching and parent mode without the composer action.
4. Port the player/fan profiles and parent, coach and management dashboards using
   real scoped data and the supplied visual references.
5. Port the media/post composer with the existing upload/privacy/moderation rules.
6. Continue the mapped academy, players/staff, finance, CMS, community,
   notifications, media and provider workflows from the architecture roadmap.

## Current code slice

- C# policy core: device lifetime, role switch expiry, deliberate fan recovery,
  bounded hashed OTP verification and FC/Academy scope decisions.
- Typed navigation: all five roles, source tab/query/path aliases and active-role
  composer eligibility. Parent, coach and management have no composer action.
- Mobile authentication is now a real HTTP workflow backed by PostgreSQL, shared
  throttles, hashed OTP challenges, an SMS adapter, JWT validation and audit rows.
- The generated client powers `/app/login` and the authenticated shell, explicit
  roles and FC/Academy selector. Operational content is still unported and uses
  the requested **Under Developments** label; no records or statistics are mocked.
- Runtime permissions and migration/recovery checks are documented in
  [mobile identity implementation](mobile-identity-implementation.md).
- Next: own profile editing and the player/fan privacy contracts, then scoped
  roster relationships and real role dashboards. Account import and legacy
  token compatibility remain cutover gates.

The original repository's [PR #42](https://github.com/MikeCy-com/Lefteria-FC/pull/42)
was open and unmerged when inspected. It is pending React implementation evidence;
its source and visual review can inform parity without treating it as shipped code.
