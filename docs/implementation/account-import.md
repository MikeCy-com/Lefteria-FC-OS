# Legacy account import and canonical mobile authority

This extension imports an initial protected account snapshot alongside the
existing roster and staff imports. The [source contract](account-import-source-contract.md)
records field authority and deliberate validation differences. It does not
implement CMS password login, invitation acceptance, legacy JWT exchange, private
operational dashboards or production cutover. No production accounts or exports
were accessed or migrated during implementation.

## Protected input and explicit execution

Use whole original objects in a protected offline manifest with these fields:

- `schema_version`: `1`; `source_repository`: `MikeCy-com/Lefteria-FC`.
- `source_commit`: the same 40-hex revision as both imported base snapshots.
- `roster_sha256` and `staff_sha256`: their exact complete input digests.
- `exported_at`: an explicit UTC ISO timestamp ending in `Z`.
- `complete_collections`: exactly `["mobile_users","fans","admin_users"]`.
- `mobile_users`, `fans`, `admin_users`: complete arrays of whole source objects.

Bounds are 32 MiB, JSON depth 32, 50,000 rows each for mobile users/fans and
5,000 admins. Duplicate JSON keys and invalid manifest structure reject input.
Semantic row conflicts produce fixed issue codes and owner-only quarantine;
every original object remains retained. A successful import with quarantine
requires operator review before a staging or cutover decision. Equal code
revisions do not prove that three independently captured data exports agree;
operators must establish a consistent export boundary separately.

Offline validation opens no database and does not read owner credentials:

    dotnet run --project backend/tools/LefteriaFc.RosterImport -- --kind accounts --input <protected-manifest>

Its `manifest_valid` status validates the envelope only. It explicitly reports
`bound_snapshots_checked=false`; resolving accounts requires both owner-only
base archives. It is not a full semantic review or migration approval.

Target review derives and reconciles the plan without persisting accounts,
quarantine, readiness or audit rows:

    dotnet run --project backend/tools/LefteriaFc.RosterImport -- --kind accounts --input <protected-manifest> --review-target --expected-sha256 <input-digest> --expected-database <verified-database> --environment staging

Actual import uses the same arguments with `--apply` instead of `--review-target`.
Both modes require the separate server-only `LEFTERIA_ROSTER_IMPORT_CONNECTION`.
The API runtime connection is never a fallback; normal startup never imports or
migrates. `--apply` and `--review-target` are mutually exclusive. Reports contain
only digests, counts and fixed codes, without raw records, names, phones,
credential hashes or connection strings.

Only `development` and `staging` assertions are accepted. The tool verifies
database name and owner authority, but an environment argument cannot prove a
host is nonproduction. Independently verify host, port, actual server, owner and
approved target before any apply. These commands do not authorize production
access or deployment.

## Atomicity, replay and retained evidence

The importer holds the shared import advisory lock, bounded table locks and one
transaction. It reconciles canonical roster/staff/contact fields against their
private archives and exact bound digests. A first import requires empty target
account, admin, grant and account-provenance tables. It creates typed subjects,
supported encoded credentials, current grants, relationship links, private raw
objects, exact manifest bytes, mappings, issue rows and one safe import audit
atomically. It never merges into an independently populated account target.

Exact-digest replay succeeds only while all typed fields, source bytes, mappings,
issues, grants and links still reconcile. It does not overwrite subsequent
verified profile changes, restore revoked grants or replace a different initial
snapshot. Timestamp projections use PostgreSQL microsecond precision while the
private archive retains the exact finer-precision source representation.

`migration.imported_admin_grants` records historical origins and has independent
admin/organization FKs. Removing a current grant leaves that evidence intact;
authorization reads only `identity.admin_organization_grants`. Historical
provenance never restores access. Unknown source metadata, invitation material
and social-principal evidence remain private; they do not become login grants.

## Runtime ownership and fan proof

Mobile `(phone,role)` subjects remain separate. Explicit verified IDs take
precedence over a unique full-phone fallback; contradictory or ambiguous source
links quarantine the affected account. Rejected source phone candidates still
count when checking ambiguity. Multiple valid guardian children remain valid.
Public staff job titles are separate from CMS roles and current mobile grants.

The runtime re-reads canonical relationships: active own player, active guardian
children, active/unarchived staff and current assignments, or active nonpending
linked admin with matching phone and current organization grants. Imported
formal roles lose availability when those relationships are revoked. Source
mobile activation remains independent, so restoring a canonical relationship
does not silently rewrite the imported source flag. The picker also rebinds the
current graph in its final SQL snapshot. A favourite-player preference grants
no guardian or player authority.

Fan registrations are evidence created before source OTP verification. Import
stores them separately; it does not manufacture a mobile subject. Active evidence
may seed a new fan only after successful phone OTP proof. Inactive evidence does
not reactivate through registration. A stale challenge cannot overwrite an
already verified fan profile. Fans and mobile users retain distinct source IDs.

After migration, reapply `ops/postgres/identity-app-grants.sql` for the configured
runtime role before starting the updated API, then run the permission probe.
The runtime reads only required canonical fields and evidence projections. It
cannot read admin credential hashes, full source/provenance archives, quarantine
or mapping tables, or mutate canonical account links and grants. A future CMS
credential service needs a separately reviewed access boundary.

## Schema recovery and verification

Migration `20261027131644_LegacyAccountImport` extends published migrations.
An empty schema can roll back and reapply. Once account import data, links,
evidence or new admin state exists, `Down` refuses with
`account_data_requires_forward_recovery` before dropping anything. Do not delete
imported or verified data to bypass that guard. Preserve protected original
manifests, checksums and a verified backup. Populated recovery requires a reviewed
forward fix or an approved protected restore, retaining subsequent user changes;
incremental import and automatic account-data rollback are not implemented.

Observed locally on 2026-10-27: migration apply and EF model consistency passed;
restricted runtime SQL probes passed; all 38 real HTTP/PostgreSQL test groups
passed. Account coverage includes offline and target-review CLI privacy,
quarantine, atomic archive failure, concurrent initial import, exact replay,
same-count mutation refusal, populated rollback refusal, canonical role/phone/
link revocation, historical grant retention, profile isolation and active,
inactive and stale fan-evidence OTP paths. Test cleanup targets only generated
fixture IDs and fictional phones and retains append-only audits. API, importer
and guarded preview builds passed without warnings. API and importer build and
publish artifacts retained the exact Unicode license notice.

The source comparison helper pins Python 3.11's Unicode 14 decimal-digit table;
its Unicode license travels in `third-party/UnicodeData/NOTICE.txt`. No production
data sampling, CMS credential authentication, legacy-token bridge, staging
restore rehearsal or live-provider test is implied by these checks.
