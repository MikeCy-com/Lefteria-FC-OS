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
or screens exist: full drag-and-drop CMS editing, real-time chat/reactions/push campaigns, live Payabl capture/recurring billing, physical POS checkout,
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
| MVP public essentials | Homepage, Academy, first-team, contact/news/fixture routes, safe scoped Academy announcements and content privacy verified in staging. The complete CMS editor and full chat/push suite may remain off for MVP |
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
mechanism, **not** a command-line argument or checked-in file. The tool checks public liveness/readiness and homepage/admin login. It
requires a stable **503 JSON `mvp_feature_disabled`** for each optional
Academy POS, Payabl hosted payment-request and fan-subscription surface.
A generic outage 503 or anonymous 401 does **not** prove feature disablement.
Private family and roster endpoints must still deny anonymous reads with
401/403. Any unexpected status, redirect or private-data leak blocks the report. It does **not** establish the
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


## Strict server-side Academy MVP-only feature boundary

In **isolated staging only**, explicitly set:

```sh
LEFTERIA_ACADEMY_MVP_ONLY=true
Payabl__Enabled=false
TwilioPaymentSms__Enabled=false
```

The setting accepts only the exact strings `true` or `false`; invalid values
fail API startup. It is **not** switched on automatically by merging source.
With `true`, the API returns non-cacheable 503 JSON
`{"code":"mvp_feature_disabled", ...}` *before* calling the handlers for:

- Academy and First Team POS admin endpoints, including cashier and sales.
- Payabl hosted payment requests and incoming Payabl webhooks.
- Optional fan subscription checkout/plans and fan lesson delivery.
- Optional training-fee SMS sending. Parent requests to save a card or enable
automatic collections are also rejected with a stable disabled-feature error;
an ordinary preferred payment day remains editable without starting billing.
The optional fan lesson delivery background worker is not started in this mode.

**Still functional** (subject to existing exact role/city/site/guardian grants):
Academy training-fee issuance and readback, invoice PDFs, manual accounting,
the roster, sessions, attendance, CRM, guardians, private educational HUB and
approved public pages. This feature flag is not permission escalation.

The launch preflight verifies the denied optional routes with unauthenticated,
read-only GET requests. It must be run against a real isolated staging build;
checking the configuration file or a mock alone never proves deployed behavior.
The UI must also be verified not to advertise disabled paid checkout; retain
the `mvp-deferred-features` gate as BLOCKED until that review and financial
sign-off. To expand the pilot, disable MVP mode **only after** separately
approving and testing merchant/POS/fan readiness and user-facing controls.

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
