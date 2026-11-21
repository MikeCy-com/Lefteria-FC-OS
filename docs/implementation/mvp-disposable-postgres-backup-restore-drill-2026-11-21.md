# MVP release engineering — synthetic PostgreSQL backup / restore gate

**2026-11-21** · Adds a disaster-recovery *mechanism test*, not permission to restore or deploy the live application.

## Why this is P0

The rewrite now has multi-tenant Academy guardian consent, financial ledgers, calendar and POS state held in PostgreSQL. A successful database migration or API health check says nothing about whether a backup can actually be restored. The live launch must have an independently **encrypted, access-controlled, tested off-host backup** of PostgreSQL and all private media plus a practiced rollback and restoration owner.

## New CI acceptance

`ops/release/disposable-postgres-restore-drill.py` runs only in the existing `Mobile identity with PostgreSQL` GitHub Actions job after the apply/rollback/reapply migrations. It fails closed unless **all** exact fixture conditions match: GitHub Actions, the correct repository, an explicit disposable-only flag, `identity-postgres` Docker container with PostgreSQL 18 Alpine, known loopback 127.0.0.1:54329, the exact test DB `lefteria_rewrite_identity_tests` and its signed-purpose metadata. It refuses a restore database or synthetic marker that already exists.

The test:

1. Inserts a **random synthetic-only sentinel** into a disposable table.
2. Catalogues all non-system tables and computes a stable count and sorted row-content digest per table, without printing identities, phone numbers, finance data, actual rows, credentials or complete database dumps.
3. Streams a `pg_dump --format=custom --no-owner` backup to an **ephemeral GitHub runner directory** (never GitHub artifacts, releases, or any public object storage).
4. Creates a **different**, previously absent disposable database using `template0`; restores through `pg_restore --exit-on-error --no-owner`.
5. Compares relation names, row counts, row-content digests and the synthetic sentinel. Any incomplete restore, schema mismatch, missing row or changed content blocks CI.
6. Drops the isolated restore database and removes the synthetic source table even when verification fails.

It does **not** run against production, the protected preview or real customers; it is deliberately not a general-purpose restoration command. Negative unit tests check that live-looking hosts, databases, identities and absent CI consent are rejected before any Docker call. There is no destructive `pg_restore --clean --create` against an existing DB.

## Production operator gates, still mandatory

The CI drill verifies the *procedure* and catches code/migration incompatibilities. It **does not** prove a real Hostinger VPS backup or allow go-live by itself. Before any approved launch:

- Take an authenticated, internally consistent, properly encrypted off-host PostgreSQL backup of the authoritative live-migrated data and **separately** protected player documents, media, invoice PDFs and required keys; verify retention, backup access, TLS, and usable restore key custody.
- Restore both into a separately isolated, access-restricted staging host. Reconcile every source collection/table, all logical amounts, sample sensitive documents, linked child/guardian IDs, legal receipts and payment event ordering. Verify migration history, exact application SHA, permissions and role ownership.
- Rehearse incident timing, DNS/app-image rollback, restoration and notification with responsible operators; test database changes that cannot be rolled back without a forward correction.
- Require independent evidence for `backups-restore`, `data-import`, `finance-reconciliation`, `privacy-security` and `cutover-rollback` in `ops/release/go-live-gates.json` for the **exact release SHA**. Do not mark these verified merely because the disposable CI drill is green.
- Keep the original public site and its authoritative data working until the explicit final cutover approval. This PR does not install timers, collect production credentials, deploy images, connect payment providers or touch Hostinger.

The CI drill is a foundation for a documented production restore runbook and recurring evidence capture; the launch gate remains **blocked** until an actual operator-controlled restore and reconciliation pass.
