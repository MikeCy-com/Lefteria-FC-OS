# Calendar verification — 2026-10-31

This records local checks for the scoped-calendar slice on
`rewrite/scoped-calendar`, based on published main
`c462b7c0ac7783ca2e65b22bae8e709569db64d4`. It does not record a merge, deployed
revision or production migration. The calendar contract was inspected directly
in original GitHub source at `a52d66b8e01c010cfe95ce53fa139e126bae4d84`.

The [source contract](calendar-source-contract.md) and
[implementation/recovery guide](scoped-calendar.md) define the accepted scope.
The following results were reported by the coordinating agent from actual runs;
pending checks are identified separately.

## Observed checks

| Check | Observed result |
| --- | --- |
| Complete HTTP/PostgreSQL executable | 42/42 groups passed against the guarded disposable loopback PostgreSQL fixture, with the restricted application role and test-only SMS adapter. |
| Pure calendar import checks | Passed within that complete run: manifest/date/time-zone/publication validation, source preservation and ambiguity handling. |
| Calendar HTTP/PostgreSQL scenario | Passed within that complete run: complete-empty versus quarantine readiness, owner review/apply/replay, atomic failure, audience-specific dates, current role/child/group/team/guest/organization scope, paging, atomic RSVP mirrors/audit and untouched physical attendance. |
| Calendar migration recovery | Actual recorded EF empty Down/Up operations passed in a rollback-isolated transaction; populated Down raised the forward-recovery guard and retained imported data. Global history and original schema privileges remained intact. |
| Domain policy executable | 26/26 passed. |
| EF model consistency | No pending model changes after the scoped-calendar migration. |
| Owner/runtime separation | Runtime grant application and restricted SQL permission probes passed; raw manifest/source access and canonical writes remained denied. |
| API contract | Controlled local OpenAPI export, contract/security assertions and generated TypeScript client generation passed. |
| Default preview API smoke | Passed using the separate guarded loopback-only tool and fictional identities. |
| Calendar preview API smoke | `--calendar --smoke` passed across the four scoped roles, child selection, confirmed RSVP, cancellation denial, organization isolation, staff reply paging and denied staff RSVP writes. |
| Final frontend tests | 90/90 passed after the review fixes; the focused calendar run passed all 20 cases. |
| Svelte/TypeScript check | Passed with zero errors and warnings. |
| Final production frontend build | Passed after the review fixes. |
| Final production-only web package smoke | Passed after the review fixes, using the production build and production-only dependencies. |
| Interactive parent calendar | Actual fictional parent login/role picker, October 30 frozen publication at 17:00, confirmed Maybe RSVP and child switching passed in the guarded production-build/API/PostgreSQL preview. |
| Mobile viewport | At 390 CSS pixels, document client width and scroll width were both 390; no horizontal page overflow was observed. |
| Preview shutdown/cleanup | Graceful shutdown left zero calendar state, item, response, mobile identity and admin rows; append-only audits remained. |

The preview tool uses fresh keys, clears inherited provider configuration and
accepts its fixed test code only for its generated fictional phone allowlist.
This behavior is confined to that tool's Testing host; the product has no fixture
OTP option. These checks neither send real SMS nor grant arbitrary imported
records to a real user.

## Published calendar checks

All five [PR checks](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/41225661360)
passed at `9299fd542a007f75af4b23d2c96a40ac3c1ade5d`.
[PR #11](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/11) merged to
main as `a5a3492511e3abbbf75ed22962b30de289caca31`; the
[main push workflow](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/41226063272)
also passed. The frontend results above include
the review fixes for imported `no_response` rows and clearing private paging
data after a current 403 scope denial. A successful source merge does not claim
that the application has been deployed.

The browser showed the approved October 30 publication at 17:00 while its staff
draft was dated November 2. Selecting Maybe sent the actual RSVP POST and a
fresh calendar GET confirmed it. Switching to the second child hid the first
child's published training; switching back retained the saved response. These
are observed fictional parent flows, not a claim that every role's browser
journey or real production data has been verified.

Local Docker/Compose execution was unavailable. The observed GitHub workflow
passed its disposable Compose build, migrations, runtime grants and startup
checks at both the PR head and the published main revision.

## Production and preview limits

All imported records in these runs were synthetic and belonged to the guarded
disposable fixture. No production records, live providers, production database
or domain configuration were changed by these checks. Exact fixture cleanup
retains append-only security audits.

At the calendar verification checkpoint, Hostinger work was limited to
authenticated read-only inventory; no separate application project had been
deployed. Subsequent preview preparation is tracked separately. Keep preview
database, secrets and access separate; do not attach `lefteriafc.cy` or alter the
live application. Source backup/export, protected restore and real-data
reconciliation remain unperformed staging gates, followed by the other unported
modules and legacy/PWA compatibility work. A production switch still requires
separate approval.
