# Authenticated browser verification — 2026-10-25

The built SvelteKit app was exercised at a 390 × 844 mobile viewport through the test-only [MobilePreview host](../../backend/tools/LefteriaFc.MobilePreview/README.md), the real ASP.NET request pipeline and the restricted PostgreSQL runtime role. All identities and profile values were fictional disposable test data. SMS was replaced only in the test host; no production data, credentials or providers were used.

## Defects reproduced and fixed

- The OTP input's Svelte attribute `pattern="[0-9]{6}"` rendered as `[0-9]6`. Native browser validation blocked valid six-digit codes before the request reached the API. An explicit string expression now preserves the pattern. A regression test compiles and renders the actual component, then checks accepted and rejected codes against the rendered pattern.
- Global foundation-page `main` alignment rules centered the mobile content and narrowed its cards. Foundation selectors are now scoped to `.foundation-page`. Reloading the production build confirmed normal mobile alignment and full-width management context cards.

## Observed browser results

- OTP issue, verification, the five-role picker and explicit fan login succeeded.
- The profile phone was read-only. Cancel restored the original name and focus. Saving Greek first/last names, birthday and email showed the actual returned values and update time.
- Team preference and notification switches persisted after a full reload. Role switching kept fan preferences independent of management organization selection.
- Parent and coach navigation had no composer; player navigation had one. The management role required an explicit organization and preserved the selected Academy context after reload.
- Header My Profile selected the fan identity. Logout followed by a private-route visit returned to `/app/login` without rendering the private shell.

The preview host shut down gracefully and removed its exact synthetic identities and associated fixture rows. Security audit records remain append-only in the disposable database.

## Automated evidence and limits

Observed locally: 43 frontend tests, zero Svelte/type diagnostics, production build and route/package smoke passed. The preceding profile checkpoint passed 26 policy cases, 23 real HTTP/PostgreSQL groups and all five GitHub jobs. The preview executable passed locked restore, Release build and its HTTP smoke, including hostile inherited listener configuration, restricted-role writes, exact role-switch expiry, request limits and shutdown-origin checks. The workflow now builds and smoke-tests that executable on Linux as well.

This verifies implemented authentication, profile and navigation flows. Operational dashboards still display **Under Developments**. Imported accounts, legacy sessions, real player/guardian relationships, avatar/media workflows, public social profiles, provider delivery, complete mockup parity and PWA/TWA parity remain unfinished. These checks do not authorize production cutover.

## Avatar follow-up — 2026-10-26

Using the same isolated production-build/API/PostgreSQL setup at 390 × 844, a repository logo was selected as a non-personal test image. The chooser showed the filename and preview; Save returned the actual generated JPEG URL. Both profile and header images loaded at 500 × 500, and a full reload retained the same URL and image. The UI showed the returned update time and save confirmation. Reopening and cancelling the avatar editor closed it and restored focus to its trigger. Logout returned to `/app/login`.

The host shut down gracefully and cleaned its exact generated identities and avatar row. Automated evidence for this slice: 49 frontend tests, clean generated multipart Blob types/Svelte check and production build/package smoke, 27 HTTP/PostgreSQL groups, native-codec probe, no pending model changes, migration apply/down/reapply and restricted-role probes. Rollback restored the original URL, preserved an unrelated later URL and allowed explicit recovery of synthetic probe bytes after reapply. These are disposable fixture checks; real account/media import and production restoration remain unproven.

New own-mobile-avatar upload is now implemented. Social-avatar approval, generic/private media, legacy-file delivery, roster linkage and the wider incomplete workflows above remain separate work.

## Favourite-player follow-up — 2026-10-26

The same production-build/API/restricted-PostgreSQL setup at 390 × 844 showed
an unavailable message and retry before roster import. An explicit CLI import
of a fictional complete snapshot made exactly one eligible first-team player
available; archived and Academy players were excluded. An existing unlisted
preference retained its neutral label. Selecting a different player and cancelling
preserved the saved choice and restored focus. Saving the eligible player, then
clearing with None, each persisted after a full reload. Logout returned to login.

Automated evidence: 63 frontend tests, zero Svelte/type diagnostics, production
build/package smoke and all 31 real HTTP/PostgreSQL groups passed. Coverage also
reproduced and fixed a picker-401 race: late profile/avatar/team/context responses
cannot restore authenticated state after the picker ends the session.

Migration apply/import/replay/down/reapply/reimport preserved existing identity
fields, and runtime permissions passed. The preview shut down gracefully; exact
snapshot digest/ID cleanup left zero canonical, source archive, readiness and
preview identity rows. Audit history remains append-only. This initial three-
collection import does not implement private role rosters or a full account/data
migration, and no production data or provider was used.
