# Academy MVP — operational API readiness (21 November 2026)

**Scope:** API runtime health behavior, not a production rollout, an operator approval or proof of source data correctness.

## Problem fixed

Previously `GET /api/health/ready` returned HTTP 200 whenever `CanConnectAsync` succeeded. A PostgreSQL server could accept connections while the OS API lacked the EF migrations or least-privilege SQL function grants needed for actual member and staff operations. Docker and a staging preflight would then misleadingly mark the API ready.

## Read-only behavior

`GET /api/health/live` stays independent of PostgreSQL and only reports a running process. `GET /api/health/ready` now returns 200 **only if**:

1. The API connects using its **actual unprivileged mobile role**, not the owner/migration credential. The role is a LOGIN, not a superuser, role-inheritance member, schema owner or role-grant recipient.
2. `identity.runtime_schema_ready(text,integer)` confirms that the complete set of compiled EF migrations up to this API version exists in the target database. The function is `SECURITY DEFINER` with `pg_catalog,pg_temp` search path, returns only a boolean, and is granted solely to the mobile role. No direct `SELECT` on EF migration history is added.
3. Core identity, approved public website, chat and player classroom permissions exist while direct staff and admin password-hash reads remain denied.
4. **If WebAdmin is enabled**, the separate admin-auth runtime connection also succeeds under its already reviewed identity/role guards; essential staff directory, coach provisioning and managed-account grants exist while mobile identity and raw staff tables remain inaccessible.
5. A deadline caps the work. Any error, disabled privilege, migration mismatch, timeout or unavailable admin credential produces a **generic 503**; no database name, migration, role, password or internal exception appears in the HTTP response. Both successful and unsuccessful readiness responses have `Cache-Control: no-store`.

This is not an application/data reconciliation test. A 200 does not imply imported players, legal invoices or migrated guardian relationships are complete. Future additive migrations on the same database may coexist briefly with an older draining API image: the compiled migration baseline must be complete, but no equality with future *newer* migrations is required.

## Acceptance

The existing isolated PostgreSQL CI test suite now executes `identity.runtime_schema_ready` as the mobile role, proves its correct compiled migration baseline succeeds, and proves a nonexistent future revision or incorrect migration count is rejected. The app role cannot read `public."__EFMigrationsHistory"`. A second real HTTP test uses the separately configured **enabled** WebAdmin test host and asserts the admin database privilege gate and generic no-store status.

Deployment operators must provision migrations, the mobile runtime role and the separate admin-auth role **before** marking the API operational; missing grants fail the release rather than producing green health. The existing disposable Docker Compose smoke rechecks `/api/health/ready` after migration and runtime-role creation. Runtime probes do not create users, write finance, import data, run migrations, contact SMS/Payabl or change permissions.

**Remaining go-live decision: NO-GO** until the exact-SHA release profile, real staging role flows, reconciled data, encrypted off-host backup/restore and signed cutover gates are independently verified. No Hostinger production environment was accessed or modified.
