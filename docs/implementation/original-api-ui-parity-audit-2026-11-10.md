# Lefteria FC source → OS API, public site, admin and mobile UI audit

**Date:** 2026-11-10. **Status:** audit in progress; no claim that every UI or API works perfectly.

## Scope, source, and evidence limits

Original **read-only** repo `MikeCy-com/Lefteria-FC`; original main source baseline
`91eafcc9efd01ad177b4f13a869cd5af7041e5b2` (see the pinned
`docs/implementation/go-live-gap-audit-2026-11-09.md`). The original was
freshly fetched in this audit for:

- `frontend/src/App.js` (public/router), `frontend/src/club-site/navigation.js`,
  `frontend/src/mobile/mobile-theme.css`,
  `frontend/src/mobile/pages/{ParentDashboard,PlayerDashboard,CoachDashboard}.jsx`;
- `frontend/src/pages/admin/{TeamsTab,EnhancedAcademyTab,ChargesTab}.jsx`,
  `frontend/src/pages/{AdminPanel,CartPage,CheckoutPage}.jsx`,
  `frontend/src/pos/PosApp.jsx`, and `backend/server.py`.

OS source checked in `main`: `frontend/src/lib/public/navigation.ts`,
`frontend/src/lib/mobile/navigation.ts`,
`frontend/src/routes/{team,join,trials,academy/registration,vote}/+page.svelte`,
`frontend/src/routes/admin/groups/+page.svelte`, `frontend/src/lib/admin/AdminRosterPlayersPanel.svelte`,
`frontend/src/lib/admin/module-catalog.ts`, public route/redirect tests, and
the existing 2026-11-09 functional gap inventory.

**Do not mistake the inspected subset or source assertions for a complete
live test**. No actual browser screenshot, live provider, youth account,
production API, Docker runtime, Svelte build or PostgreSQL tests were performed
in this audit. The original site has private and age-restricted workflows;
use synthetic seeded fixtures and authorized dedicated test roles only.

## Original source inventory baseline

The existing pinned, read-only legacy route inventory identifies **39 public
route patterns** (including redirects) and **72 AdminPanel switch labels**,
including aliases, from the earlier source snapshot. It also records **208**
static HTTP decorators in the main Python `server.py` alone, plus further
handlers in `backend/routes/`. These are **not unique API operation counts**,
and 72 render labels are not necessarily 72 distinct screens. The audit runner
now enumerates the OS page and API declarations at its current checkout,
while the full source-to-OS operation-level mapping remains open.

This prevents claiming “every endpoint verified” from a rough count or from
viewing an admin menu. The acceptance unit is an actual original workflow:
HTTP verb, mounted path, request schema, role/org/relationship eligibility,
response states, mutation effect and user-visible confirmation.

## Findings actually supported by source

| Priority | Surface | Original behavior | OS observation | Required completion |
|---|---|---|---|---|
| P0 | First Team admin | Original TeamsTab supports team/fixture creation/edit, player/staff associations, analytics and media workflows | OS admin groups provides scoped team selection and roster assignments, but it is not evidence of the complete original TeamsTab or its fixture/video/statistics controls | Compare every field/action and implement missing authorized writes, then verify app/website propagation |
| P0 | Academy admin | Original EnhancedAcademyTab supports group create/edit and training/session, player transfer, fixture and staff workflows | OS group/roster editor covers a subset; original business-field and downstream behavior parity is unverified | Build exact fixture/group/player/coach matrix for existing and newly created groups |
| P0 | Parent/coach/player mobile | Original role dashboards have live session, attendance, match, finance, attachments and notification actions | OS mobile navigation defines all five roles; a declared role/tab does not prove matching content, server permissions or data persistence | Real authenticated synthetic role flows, child/group grants, date/timezone, notification delivery and no cross-role data leakage |
| P0 | Public enrollment | Original /join, /trials and /academy/registration provide data-entry workflows | OS currently shows truthful non-submitting fallback/closed pages | Restore fully authorized/consented registration and anti-spam APIs before enabling forms |
| P0 | Public voting | Original /vote supports eligibility and vote submission | OS /vote remains an honest non-voting placeholder | Rebuild authenticated eligibility, anti-duplicate vote, publication and admin review |
| P0 | E-commerce | Original CartPage, CheckoutPage, PosApp have cart/checkout or cashier workflows | Public cart/customer checkout parity remains incomplete; POS financial branch is separate and unmerged | Integrate customer identity, basket, real payment confirmation, receipt, stock and return without inventing success |
| P0 | Admin finance | Original ChargesTab contains 8 original charge categories, partial amounts, multiple methods and invoice states | OS finance parity under other draft branches; feature catalog does not establish complete or merged functionality | Compare every charge/event, partial payment, reversal and issued month; reconcile posted balances and invoice snapshots |
| P1 | Public styling | Original public black #080808, gold #f5a623, Fira Sans / Extra Condensed, 116px rail and 76px header | Existing OS CSS asserts those tokens; no equal-viewport screenshot or rendered font check here | Real same-data browser captures at 390, 768, 1440, 1920 and 3840 pixel widths |
| P1 | Public navigation | Both menus have nine groups and corresponding principal destination paths | Source comparison found OS already preserves the main structure and improves a standings anchor | Preserve semantic link scope, dynamic Academy groups and focus/navigation behavior |
| P1 | Legacy staff link | Original React router deliberately redirects `/staff` to `/team?tab=overview`; the First Team hub separately has a staff tab | OS matches that legacy redirect, and `/team?tab=staff` remains the dedicated staff screen | **Confirmed exact source match:** keep the original redirect; regression checks guard both destinations |

## Systematic audit inventory now committed

`frontend/scripts/audit-original-parity.mjs` recursively enumerates the OS
Svelte page routes, route/load files, all API endpoint declarations under
`backend/src/LefteriaFc.Api`, all frontend UI component source files and
static client API calls. It compares the original nine public menu
sections, five mobile role definitions and known source route destinations
against the OS and reports source-identified unported sensitive workflows.

Run from repository root:

```sh
node frontend/scripts/audit-original-parity.mjs
node frontend/scripts/audit-original-parity.mjs --json > /tmp/lefteria-ui-api-inventory.json
cd frontend && npm test
```

**These are static inventories, not evidence that APIs work at runtime.**
Dynamic routes, middleware grouping, URL proxies and variable-computed request
paths must be resolved in the manual/HTTP contract matrix. A `200` with an
empty placeholder is not business parity, and an admin link is not proof a
secure write exists. Never mark a pending/fake financial or medical workflow
complete from a menu or page alone.

## Required execution sequence

1. **API contract sweep:** enumerate every original Python route/verb/schema and
   match one OS .NET endpoint, role policy, organization scope, DTO validation,
   persistent mutation and error contract. Mark absent, partial, true and
   explicitly deprecated separately. Exercise authorized and forbidden callers.
2. **Role/browser sweep:** load every public and private page with actual
   synthetic fixtures for first-team admin, Academy admin, parent, player,
   coach, management and fan. Check empty/loading/error/revoked session states.
3. **End-to-end persistence sweep:** mutate First Team/Academy teams, players,
   coaches, fixtures, attendance, protected evaluations, communications,
   CMS media/news, payments and refunds; verify exactly those changes appear
   across authorized app/public pages and disappear on revocation.
4. **Visual parity:** compare original and OS at identical locale, viewport,
   font loading and synthetic state; test desktop/mobile, keyboard,
   4K assets, reduced motion, screenshots and layout regression.
5. **Release:** exact SHA build, security/API contract suite, PostgreSQL
   rollback/concurrency fixtures, real sandbox Payabl flows and independent
   release sign-off. Never change legacy production or live website.

Other draft PRs may add functionality that is **not in main**. Review
`#129`, `#143`, `#151`, `#153` and related pending PRs before
duplicating implementation or declaring a current main feature live.

**No production changes or original Lefteria FC writes permitted.**
