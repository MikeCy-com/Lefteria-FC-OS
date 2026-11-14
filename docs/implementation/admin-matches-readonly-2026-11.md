# Admin matches read-only flow — November 2026

## Source of truth and scope

This OS implementation exposes published match schedules and results under
`/admin/matches` while the original Lefteria FC application remains authoritative
for fixture creation and results. The UI reuses the existing
`GET /api/public/site?organization_id=<organization>` endpoint and its
`PublicFixture` projection. The API returns a maximum of 50 selected fixtures,
not an entire historical season. It includes no certified standings table.

Club managers with `teams:read` may use the First Team scope. Academy managers
with `academy:read` may use the Academy scope. The UI uses the global admin
header organization preference and checks the current authenticated admin
session before loading. This is **not** a private fixture endpoint; the public
site API continues to serve already-published records only.

## UI behavior

- Four tabs: upcoming, completed results, Cup, and all published fixtures.
- Team/group and year filters apply only to returned records; changing a
  filter cannot invent more results.
- Next/completed/cup counters reflect the returned set (at most 50).
- Exact backend date, time, score, venue, competition and teams are displayed;
  absent values remain unavailable.
- Each published fixture links to its existing public match detail route.
- Read-only by design. No private import evidence or roster/guardian records
  are ever requested by this page.

## Remaining gaps

This increment does **not** implement the original authenticated fixture
editor, opponent management, match-status changes, standings computation,
CSV import, Cup draws, or push notifications. Those require separately reviewed
contracts, server-side RBAC, organization-scoped writes, audit logs and
tested rollback/migrations. The acceptance issue is #273.

## Verification

`frontend/tests/admin-matches-readonly.test.mjs` covers API selection, route,
current-organization guards and absence of writes. PR CI, responsive browser
checks and preview testing must pass before merge. Do not infer deployment from
merge or a passing screenshot audit.
