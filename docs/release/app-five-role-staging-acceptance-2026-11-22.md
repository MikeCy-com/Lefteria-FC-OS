# Lefteria FC OS — five-role APP staging acceptance (2026-11-22)

**STATUS: NOT VERIFIED / NO DEPLOYMENT.** Evidence captured for draft PR #444 at `2fdc8bfecf5714e801fef350be34370333f55d4c` is **CI only**, not an observed Hostinger deployment. This checklist is for the isolated `lefteria-os-preview` environment and synthetic, permission-granted accounts only. Never run it against `lefteriafc.cy` or live families, money, SMS, Payabl, or production databases.

## Evidence already observed

- [Rewrite build #2512](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/45030593629): PASS, five jobs (SvelteKit, API startup/contract, ASP.NET, PostgreSQL mobile identity, Compose).
- [Browser UI audit #952](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/45030593627): PASS. Logs report `{"roleSnapshots":10,"failures":[]}`, `{"denialCases":25,"failures":[]}`, `{"actualProtectedRoleRouteCases":10,"failures":[]}`.
- The image comparator self-test passed; the run explicitly warns that original screenshot PNG references were unavailable. **Pixel parity is UNVERIFIED**.
- CI ran against a *draft PR head*. The documented preview image-release pipeline only accepts a separately checked **main-push** candidate. Do not load unverified PR images or silently substitute `latest`.

## Hostinger environment gate — G0, mandatory BEFORE any change

Authorized operator (never paste credentials in issue): capture privately and independently compare:
- Legacy vs preview Hostinger account/VPS, hostname/DNS and IP, TLS certificate paths, ingress and provider webhook destinations.
- Exact Docker Compose projects, containers (full IDs), image IDs/immutable revision labels, start times, networks, volumes, bind mounts and exposed host ports.
- PostgreSQL host, database name, owner/app roles, credentials fingerprints (not secrets); object/media paths and backup locations.
- Confirm no shared writable storage, credentials, port/proxy override, job provider egress, payment activation or source DB mount. Confirm disabled Twilio/Payabl-live/legacy-token exchange.
- Require independently verifiable original-production IDs and timestamps *before and after* acceptance; stop on any uncertain overlap.

**G0 observed result: NOT RUN.** Without read-only VPS/container inventory access and operator evidence, **do not deploy, migrate, seed or test against hosted private APIs**. Follow `deploy/preview/README.md`; never replace an existing root-owned config with fresh-install bootstrap. Backup and recovery rehearsal required before staging migrations.

## Five-role executable acceptance matrix

Each test must record candidate SHA, preview environment identity, UTC timestamp, synthetic fixture ID, role/scopes, request IDs/response codes, viewport and redacted screenshot location. Every negative permission case requires a server result, not a hidden UI button.

| ID | Role | Positive journey | Negative permission/data-isolation journey | Status |
|---|---|---|---|---|
| APP-PARENT | Parent | Phone login using approved *staging* OTP mechanism, select Parent, home → Calendar → Children → authorized child's schedule/attendance → Chat → More → finance/education/HUB. Confirm correct contextual highlight, browser back and draft-safe state. | Cannot use composer; cannot view unrelated guardian/child, another FC/Academy context or another family's invoice; revoked guardian link immediately denies API. | NOT RUN |
| APP-PLAYER | Player | Select Player, own team/matches, own progress and permitted educational content, profile and announcements; verify composer only when server-authorized. | Cannot read other player private development/guardian finance or unpublished youth documents; no staff writes. | NOT RUN |
| APP-COACH | Coach | Assigned group, training/calendar, roster, permitted attendance/evaluation and group Chat; member announcement highlights Chat. | Unassigned groups and other cities denied by API; no cross-org player/finance access; cannot compose social post in Coach mode. | NOT RUN |
| APP-MANAGEMENT | Management | Authorized FC/Academy selector, assigned teams, calendar, finance and announcements (contextual More) with fresh server session. | No extra scope through manual selection; city/site financial totals and youth records outside grants denied. | NOT RUN |
| APP-FAN | Fan | Home, fixtures/matches, Game Hub, Shop and public profile; role switch retains intended original navigation. | No private Calendar, Chat, child/coach records, private finance; cannot borrow other formal roles via stale cached claims. | NOT RUN |
| APP-SWITCH | Multi-role account | Role switch → correct primary tab order, header profile and notification controls, preserved account session/organization scope, refresh/back/deep links. | Revoked formal role cannot be restored via deep link; fan fallback cannot access previous private data; no cross-role cached screen flashes. | NOT RUN |
| APP-MOBILE | All five | Capture actual authenticated screens at 320×640, 390×844, 768×1024 and representative desktop width; confirm Fira fonts, 72px header, 68px nav, 44px controls, safe area, keyboard, readable Greek text and zero horizontal overflow. | Hidden/disabled flows do not expose private data or misleading success; test connection loss/loading/403 and accessible focus/back. | NOT RUN |
| APP-PIXEL | All five | Pair original owner-provided reference PNGs with same-state, same-viewport staging screenshots using `frontend/scripts/compare-reference-screenshots.mjs`; document mismatches and approval. | Missing baseline is UNVERIFIED, never PASS. | NOT RUN |

## Deployment/acceptance decision

1. G0 must pass with separate production/preview inventory and operator signoff.
2. Resolve merge/candidate process: PR #444 is **not** a release. Review/merge only under repository policy after CI; verify **exact main** commit and immutable `preview-images.yml` prerelease manifest/checksums. Do not let draft head masquerade as main.
3. Authorized operator performs guarded, reversible **staging-only** image rollout after staging backup, isolated restore verification, permission probes and migration review. Preserve production IDs/start times.
4. Verify running revision from image labels and application health, compare to validated manifest; collect real HTTP/API results and screenshots for all matrix rows.
5. Repeat negative authorization checks after role revocation and organization change. Record evidence privately (never child data/tokens in CI artifacts or PR comments).
6. Roll back **staging only** on failure, using the rehearsed compatible image/recovery approach. Do not mark release ready without completed matrix and separate production approval.

**Current decision: CI PASS (for documented draft head); G0 NOT RUN; STAGING NOT VERIFIED; PRODUCTION NOT TOUCHED; NO-GO for deployment/merge-as-release.**
