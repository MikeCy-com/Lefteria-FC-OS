# Lefteria FC OS — production MVP release decision and cutover runbook

**Status (2026-11-21): NO-GO.** This is an operator playbook and read-only release evidence tooling, **not a release approval or permission to modify the working Lefteria FC deployment**. The existing Hostinger live environment, real domains, customers, payment provider and source database must remain untouched until a separately recorded cutover decision.

## What an MVP launch can safely mean

Prioritize verified member sign-in, canonical roster and Academy city/location/group permissions, training/matches/attendance, the approved education HUB, core web pages, and functioning administrative actions. Keep nonessential marketing and AI enhancements out of the critical path. A capability is **in scope only if the real user can complete the workflow and a denied-role test verifies its boundaries**, not merely because a page renders.

**Do not claim a partial release is safe simply by describing a feature as deferred.** Any unverified feature that accepts payments, exposes child data, sends messages, changes financial balances or allows staff writes must be disabled by an explicitly tested server-side kill switch or removed from the deployment's public ingress. Disabling a button in the UI is not sufficient. The existing release guard currently assumes full-production scope and remains blocked; a reduced release profile requires separate, test-backed implementation.

## Release order (non-negotiable P0)

| Sequence | Owner / action | Evidence required before GO |
| --- | --- | --- |
| 1. Immutable candidate | Engineering | Current \`main\` SHA, exact-successful Rewrite Build (API, SvelteKit, real PostgreSQL, Docker, contracts), Browser UI visual audit; every latest required job must pass at this exact SHA. |
| 2. Host/environment identity | Infrastructure | Independently document real production vs isolated staging hostname/IP, Hostinger VPS/account, Compose project, domain/DNS, volume IDs, DB names, object-store paths, network, origin and webhook URLs. No shared writable data or secrets. |
| 3. Data freeze / mapping | Operations + finance | Approved legacy extraction window; reconciled IDs, team memberships, guardian phone ownership and consent, Academy city/location/group, first team, balances, fees, settled/partial/refunded records, invoices and documents; zero unexplained quarantine. |
| 4. Login & permission proof | Security + club staff | Real approved OTP provider, login/recovery/session expiry with production-equivalent configuration. Test parent, player, coach, fan, location manager and Super Admin allow/deny cases, isolation between Academy cities/sites and First Team, revoked users, sibling/guardian consent. Do not use test OTP bypasses. |
| 5. Approved education | HUB editor + coach | A new athlete starts at Lesson/Quiz Week 1 even if an older athlete has progressed; role-specific approved content and lessons never cross audience/organization; supervised position selection; age-targeted items remain hidden without canonical age proof; private coach feedback needs human sign-off. AI draft is never automatically published. |
| 6. Finance/provider rules | Finance owner | Payabl merchant-approved signature/callback/replay and actual sandbox settlement/refund with amount, currency, organization, invoice and child; recurring mandates only after explicit verified consent. Unsigned redirects, mock payments and unissued months may never mark paid. POS/location balances, legal VAT/invoice numbering and reversals must reconcile independently. Keep sandbox-only payment paths or disabled modules until verified. |
| 7. Content, media, legal | Data protection owner | Child image rights/consent, protected documents and revocation, retention, cookies, communication opt-in, sponsor isolation, Greek copy and page/SEO parity; legal review of receipts and invoices. |
| 8. Recovery rehearsals | Infrastructure + DBA | Fresh **encrypted** DB and object-store backup with checksum, offsite custody, isolated restoration on a distinct host/volume, read and reconciliation checks, documented downtime/RTO/RPO, database migration forward/backward compatibility. |
| 9. Cutover rehearsal | Release operator | Reversible traffic/DNS plan and tested application rollback on isolated staging, migration compatibility, monitoring/alerts, runbook owner, time-bound change window, verified fallback origin; no destructive live migration or source DB overwrite. |
| 10. Human GO/NO-GO | Club owner + engineering + finance | Every gate recorded with actual dated evidence **against the same current release SHA**, latest independent CI green and separately recorded authorized release decision. A missing or failed gate means NO-GO. |

## Read-only checks (safe on a developer workstation or in GitHub Actions)

To check the authoritative **current main** GitHub CI without touching any host:

~~~bash
node ops/release/verify-exact-ci.mjs --sha=<exact-main-commit-sha> --require-passing
~~~

To display all operator blockers without making network calls or deploying:

~~~bash
node ops/release/check-go-live.mjs --sha=<exact-main-commit-sha>
~~~

To request an independent GO decision check, which also performs authenticated read-only GitHub API verification:

~~~bash
GITHUB_TOKEN=<read-only-actions-and-contents-token> \
  node ops/release/check-go-live.mjs --sha=<exact-main-commit-sha> --require-ready
~~~

Or open GitHub Actions → **Production MVP release candidate preflight (read-only)** → Run workflow → enter the exact 40-character current \`main\` commit SHA. The workflow has only \`contents:read\` and \`actions:read\`, no SSH, Hostinger, Payabl or production database credentials. A red result is expected while operational gates remain unverified. **A successful CI verification is not a deployment command.**

The JSON manifest \`ops/release/go-live-gates.json\` stays blocked by default. To record a gate as \`verified\`, the authorized operator must attach non-secret, accessible HTTPS evidence, an exact SHA and a calendar date. Do **not** copy sensitive live records, credentials, phone numbers, bank/card data, private child names/photos or backups into GitHub. Link an access-controlled evidence location instead and confirm audit access. Release decision is an independent human entry with a change ticket and named approver. Static source checks cannot verify the authenticity of those operator claims; real-world inspection, two-person review and an isolated restore/cutover rehearsal remain mandatory.

## Go-live rehearsal: minimum end-to-end scripts

1. With synthetic children and verified staff in isolated staging, create Academy cities/locations/groups. Check one location manager can act only in the granted location and can never read other sites' financial totals; a city may share opponents/fields but not another city's catalogs; First Team is separate.
2. Admin creates training → coach records individual attendance, evaluation and a signed development note → player and permitted guardian see only authorized records. Check removal of permission revokes access immediately.
3. HUB manager creates human-authored course and a published quiz → independent human review approves → new player sees own first lesson/Week 1; another player already at Week N is unaffected; verify position-specific quiz, progress, withdrawal and fresh revision requirements.
4. Verify Admin CRM enrollment and second-parent phone permissions; check all children in one family without leaking records across families and sites.
5. Exercise cash/card/bank, partial and refund cases only in an approved synthetic financial staging environment; download receipts and reconcile totals and numbering. Do not activate real payment capture, recurring cards or external SMS from this runbook.
6. Simulate invalid or replayed payment callbacks, expired/deactivated mobile identity, removed coach, org switch, abandoned checkout, admin session revocation, server outage, worker retry, mobile reconnect, 401/403, and database recovery.
7. Inspect same-class screens at 320, 390, 768 and desktop widths; accessible font sizes, no overflow, Greek labels, loading/error states and mobile role navigation. Record real viewport evidence, not source-only tests.
8. Confirm logs and metrics suppress tokens, payment payloads and children's private details; verify alarms for API 5xx, failed OTP, payment webhook mismatch, queue backlog, DB storage/backup age, memory pressure, TLS and restart loops.

## Rollback decision

If release preparation fails, leave production unchanged and record the blocker. If a separately approved cutover later fails, stop outbound providers and worker actions first, then follow the **rehearsed** application/routing rollback. Never assume reverting a container image also reverses a database schema or financial settlement. Restore a database only from a separately approved, tested point-in-time recovery plan with a complete transaction reconciliation. Preserve immutable audit and payment histories.

## What this change does and does not prove

This PR improves the integrity of the **release authorization process**, not the number of completed product features. It rejects fabricated GitHub CI URLs, stale main SHAs, green PR checks mistaken for release checks, missing/failed jobs, absent operator approval, and missing production login/HUB/backup gates. It does not claim that Hostinger staging was audited, that production SMS or Payabl works, that data was reconciled, or that a backup restore has already passed. Those items remain explicitly **BLOCKED** until demonstrated.

**Never run migrations, connect production Payabl credentials, change DNS, or deploy from this document or its CI workflow.**
