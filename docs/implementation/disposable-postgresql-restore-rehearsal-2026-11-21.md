# MVP launch safety: isolated PostgreSQL backup/restore evidence

**Status:** Automated **CI fixture restore rehearsal only**. This does **not** satisfy the production backup/restore gate, confirm Hostinger access, or approve go-live.

## Why this is a release blocker

An API and database migration passing CI does not prove recovery of the club's actual data. Before Academy MVP cutover, operators need evidence that a full copy of the **approved staging snapshot** can be recovered to an isolated database, with verified tables, roles, FK constraints, RLS policies, authentication, encrypted document and media assets, consent lineage, correct guardian/child mapping, and payment/receipt history. Restore must not change existing live records.

The implementation added in this PR proves only the narrower **database restore mechanism** in GitHub Actions, with purely synthetic records:

1. The identity CI workflow migrates a fresh, loopback-only PostgreSQL database named `lefteria_rewrite_identity_tests` under its disposable container.
2. `ops/postgres/disposable-backup-restore-drill.sh` requires an explicit CI acknowledgment, fixed test DB/user, loopback address and fixed port **54329**. Unexpected settings or preexisting restore database cause an immediate refusal.
3. In a randomly named private temporary directory inside the CI container, it records a synthetic row with a real PRIMARY KEY and CHECK constraint, then creates a compressed PostgreSQL **custom archive** using `pg_dump --no-owner --no-acl`.
4. It validates the archive listing, removes the source canary, creates a **separate new database**, and uses `pg_restore --exit-on-error`. The source is never overwritten.
5. It compares the exact aggregate manifest (EF migrations, schema object counts, function count, PostgreSQL policies, constraints, staff count and synthetic record count) between source-at-backup and restore; it verifies the synthetic row and constraints.
6. It destroys the CI-only archive, test canary and restored database on exit. Neither backup data nor credentials are printed or uploaded.

The restore intentionally omits database ACLs and owner mappings: it verifies schema/data/RLS objects, **not** the complete production role bootstrap or restored API correctness. No actual club data or off-host encrypted backup is involved. CI failures block the PR rather than claiming recovery is operational.

## Remaining human-approved release evidence

The release gate `backups-restore` remains **BLOCKED**. Evidence required separately on a protected staging machine before human approval:

- Operator-authorized and immutable encrypted backups for PostgreSQL **and** private files/assets, with encryption-key custody and documented off-site storage/retention.
- Snapshot origin, database migration revision, size, SHA-256 digest, staff/parent/player/guardian counts and authorized accounting totals; no raw PII in CI logs.
- Isolated full restore from that exact backup, including role-grant bootstrap, file decryption, private access checks, and historical invoice/receipt comparison.
- Repeated staging restore, measured recovery time and data-loss target, tested app-level smoke checks for owner, coach, guardian and player sessions, and independent approval.
- Release-commit binding, observed monitoring/incident response, documented reversible web routing and app image rollback. Database down-migrations are **not** a substitute for a tested point-in-time recovery plan.

Never run the CI script on production or use the CI archive as a valid off-host backup. This PR changes no Hostinger environment, DNS, remote databases, payment provider, SMS or live application.
