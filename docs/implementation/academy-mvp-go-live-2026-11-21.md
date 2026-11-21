# Academy-first MVP — release evidence and cutover policy

**Status 21 November 2026: BLOCKED pending real operational evidence.**
Passing CI is not production approval. The original site, historical data,
Hostinger production and live payment processing are unchanged.

## First release: the smallest reliable club workflow

MVP covers the Greek public club/Academy website and a privately authenticated
Academy workflow for legitimate club roles: city/site provisioning, site-managed
groups, authorized coaches/staff, player and guardian identities, training and
match events, attendance, registration, basic scoped finance/invoices,
app-visible scheduling and explicitly approved announcements.

The following are **not assumed to be production-ready** merely because code
or screens exist: live Payabl capture/recurring billing, physical POS checkout,
card refunds, fan subscriptions, automated AI publishing, bulk SMS campaigns,
stock/warehouse reconciliation and optional marketing automation. They may
remain unavailable in the first release only after independent evidence that
all client/API/database execution paths are disabled, that no user can
accidentally initiate billing, and that public UI does not advertise an
unavailable paid capability. This exclusion is itself a required release gate.

Mobile authentication cannot be deferred: parent, player and coach OTP
delivery and account recovery must work with an approved real provider in
isolated tests before their logins go live. The old fixed test OTP must never
be enabled in production.

## Critical sign-offs — exact code SHA, environment and named reviewer

Use `node ops/release/check-go-live.mjs --profile=academy-mvp` for an
unverified planning report (expected BLOCKED). A release decision requires
`node ops/release/check-go-live.mjs --profile=academy-mvp --require-ready --sha=<40-char-reviewed-commit>`
**and independent, verifiable evidence** for every required gate.

| Owner / gate | Required result before approval |
| --- | --- |
| Engineering: API/CI, staff, calendar and operations | All required main-branch CI checks pass at the exact SHA; Super Admin can create canonical Academy city/site, group, assign coach, publish a training event, record presence and a match result in private staging |
| Identity: role-mobile-parity and identity-delivery | Parent, player, coach, site manager and Super Admin complete real sign-in/recovery using approved test devices; permissions revoked immediately; first-team and Academy never cross; no fixed OTP |
| Safeguarding: CRM, documents and privacy | Child/guardian permissions, protected files, consent, join/leave actions, denied cross-child/site probes, expiry and negative tests independently signed off |
| Finance: finance-reconciliation | Only authorized manual cash/bank and legally reviewed invoices are enabled unless card capture/settlement fully verified; location totals reconcile to original source; no invented paid status |
| Data: data-import and media-and-assets | Source snapshot provenance, exact current counts and IDs, guardian relationships, documents and media checksums reconciled; critical quarantined rows resolved |
| Content and communications | Published website source, announcements, group scoping, attachment visibility and parental restrictions verified against preview; no unintended campaign sends |
| Infrastructure: backups-restore, operations-monitoring | Encrypted DB and media backup verified by actual isolated restore; measured RPO/RTO, alert/rotation and credential lifecycle |
| Release: cutover-rollback | Explicit deployment approval, pinned digest and commit, TTL/DNS/TWA plan, domain and cookie continuity, monitored canary, and a rehearsed recovery that does not overwrite old production |
| Release: mvp-deferred-features | Independently demonstrate optional providers/routes/subscriptions are disabled; provider secrets remain isolated, no accidental payment, and misleading controls are absent |

Do not set any release gate to `verified` by merely seeing a CI badge, a
successful staging GET request or a completed code diff. The required evidence
includes actual per-role staging user acceptance, source-to-target
reconciliation and authorized human sign-off.

## Read-only staging network preflight

Run from an approved workstation with access to the **isolated preview**
(not the public production domain). This tool has a strict preview-origin
allowlist, uses HTTPS, never follows redirects, never submits transactions,
and never prints the optional gate authorization:

```sh
node --test ops/release/check-go-live.test.mjs ops/release/staging-mvp-preflight.test.mjs
node ops/release/staging-mvp-preflight.mjs \
  --origin=https://srv1649602.hstgr.cloud:8443
```

If the preview is protected by an upstream edge authentication gate,
set `LEFTERIA_PREVIEW_GATE_AUTHORIZATION` through a secret-aware environment
mechanism, **not** a command-line argument or checked-in file. The tool
checks public liveness/readiness, homepage/admin login, and that anonymous
requests for Academy POS site data, family data and roster data receive a
401/403 rather than a 200. Any timeout, redirect, 503, private-data leak or
unexpected page content blocks the report. It does **not** establish the
deployed binary SHA, backup success, OTP delivery, correct private content
or payment-provider status.

## Why an admin screen may render but its actions are denied

PostgreSQL runtime roles execute security-definer functions through explicit
reviewed `GRANT EXECUTE` statements. A UI button existing in source does not
mean that the preview's runtime database role received the matching grant.
The disposable CI suite now explicitly exercises Academy location group
creation alongside roster, training-plan, attendance, Academy CRM and
guardian grant availability. **CI grants do not update staging or production.**

After deploying an approved candidate to an **isolated staging database**,
run the separate read-only audit using your exact runtime role names:

```sh
psql -X -v ON_ERROR_STOP=1 \
  -v admin_auth_role=<isolated-admin-db-role> \
  -v app_role=<isolated-mobile-db-role> \
  -f ops/release/academy-mvp-permission-preflight.sql
```

It fails on a missing Academy group-creation, coach/training scheduling,
attendance, CRM or parent/guardian permission grant, or on accidental
mobile execution of an admin function. It never applies grants. If it
fails, repair and re-review the **individual scoped grant** in the isolated
environment; never grant table-wide permissions or bypass authorization.
This check does not replace tests for actual user roles and city/location
boundaries, nor does it claim that all app features are available.

## Deployment decision and rollback

1. Record the immutable candidate Git SHA, image manifest SHA256/digests,
   the separate Hostinger preview project IDs and DB volume identifiers.
   Verify they do not refer to original production containers.
2. Complete exact-SHA CI, read-only staging preflight and the functional
   per-role scripted walkthrough. Reconcile real source imports through
   approved, protected methods; never copy raw children/financial records
   into this repository or logs.
3. Take and verify encrypted backups of the production source **without
   altering it**. Restore an independently secured copy, including media,
   and rehearse a timed rollback of the new routing; preserve legacy access.
4. Require a separate, explicit human cutover authorization and published
   support/incident contacts. First switch a small reversible portion of
   traffic, monitor failed logins, authorization denials, discrepancies,
   error rates and background jobs before any wider promotion.
5. If readiness or reconciliation fails: stop new enrollment/payment writes
   to the rewrite, restore routing to the untouched original, retain the
   immutable incident log, and reconcile write deltas manually. Do not
   attempt automatic reverse synchronization of live payments.

**No script in this slice deploys, grants payments, runs production migrations,
changes DNS, or modifies Hostinger.** The narrower MVP profile intentionally
keeps critical gates BLOCKED until independently verified.
