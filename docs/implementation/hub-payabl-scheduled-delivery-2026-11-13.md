# HUB Payabl entitlement and weekly delivery — private foundation

## Scope

This change is a **schema-only foundation**, not a functioning payment subscription or notification service. No merchant charge, webhook-to-entitlement activation, automated delivery, live deployment, or fan content access is enabled by this PR.

- Every registration receives an independent enrolment row keyed by the authenticated mobile identity, organization and audience. The enrolment starts at **Week 1**, whether the member registers today or months later. Existing members keep their own week position.
- `next_due_at` is stored per enrolment, so weekly scheduling is based on that member's own enrolment time rather than the date the lesson was first published.
- An idempotent private outbox enforces one in-app delivery per member/week and prevents repeating the same content for that enrolment.
- The Payabl entitlement predicate requires a **live fan role** plus an **active, verified, unexpired, uncancelled Payabl subscription** with the matching parent/player interest. A fan's interest selection, checkout URL, or browser redirect never counts as proof.
- No runtime grants or dispatch worker are included. All new tables have forced row-level security and no public access. The owner-only entitlement function is not granted to mobile, admin, or unauthenticated roles.

## Required next implementation slices (not yet completed)

1. Payabl sandbox subscription checkout: create an immutable, idempotent, user-bound pending intent and provider transaction correlation. Keep all merchant secrets server-side. Never activate from a browser redirect.
2. Verify provider callbacks, replay protection, signed fields, merchant reference, exact amount/currency, and authoritative settlement/reconciliation. Only a separately approved reconciliation function may transition a fan subscription to `active`.
3. Implement explicit opt-in enrolment with canonical academy role/roster checks and a verified subscription for fans. A member's first approved lesson is Week 1; keep the same track for late joiners.
4. Add a restricted worker that selects due enrolments with row locking, rechecks entitlement and approved/current content, inserts an idempotent in-app outbox row, advances `delivery_week` and `next_due_at` by exactly seven days, and emits a private notification. Do not allow workers to deliver drafts, revoked content or lessons for cancelled subscriptions.
5. Provide member-visible schedule, history, quiz gating, missed-week catch-up, cancellation and administrative pause controls. A quiz is completed only when the server verifies its result.
6. Add disposable PostgreSQL concurrency, replay, cross-organization, late-registration, role-swap, cancelled-payment, clock boundary, and content-withdrawal tests; run all five CI jobs and isolate staging before rollout.

## Deployment boundary

Only Lefteria FC OS is changed. Do not apply this migration to the original Lefteria FC database or live Hostinger production. The Payabl integration remains **sandbox-only** and unconfigured by default. PR #238 (per-member learning) and PR #239 (weekly player quizzes) remain separate unmerged dependencies until CI works.
