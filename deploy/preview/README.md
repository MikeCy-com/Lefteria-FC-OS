# Isolated Hostinger preview

This deployment is a separate `lefteria-os-preview` Compose project. It must not
reuse the legacy project's containers, networks, database volumes, keys or
published ports. It serves a separate hostname on HTTPS port 8443; it does not
attach or redirect `lefteriafc.cy`.

`compose.yaml` uses verified prebuilt image IDs, separate owner/runtime database
credentials, fresh preview token/OTP keys, private application/data networks,
read-only application filesystems, dropped capabilities, process/memory/CPU
limits and rotated logs. Only the edge publishes a port. Its default bind is
loopback until TLS, authentication and private-service checks pass. The API has
no outbound provider network; SMS and the legacy token bridge are disabled.
The existing local test/preview host and its fixed OTP are never deployed here.

## Prepare an exact release

`.github/workflows/preview-images.yml` builds only a main push whose complete
`Rewrite build` workflow passed. It publishes API, web and owner-import image
archives, `manifest.json` and `SHA256SUMS` in a commit-specific prerelease. It
does not deploy. Existing tags/releases are refused rather than overwritten.

Download all five assets into a new private directory under
`/srv/lefteria-os-preview/bundles/<commit>`, verify every checksum and the expected
repository/commit, then load each image archive. Check each loaded image's ID,
platform and revision/source labels against the manifest. Do not build on the
live VPS or silently substitute a `latest` application image.

`prepare-environment.py` independently verifies the loaded image identities and
protected source/certificate paths before generating configuration. It writes a
new root-only configuration directory atomically, refuses an existing one, uses
independent secrets and preserves literal bcrypt dollar signs in single-quoted
Compose values. It emits no credentials. `client-access.json` contains only the
separate preview gate credentials; it is not an application login or OTP bypass.

```sh
python3 deploy/preview/prepare-environment.py \
  --source-sha <verified-main-commit> \
  --bundle-manifest /srv/lefteria-os-preview/bundles/<commit>/manifest.json \
  --output-directory /srv/lefteria-os-preview/config \
  --manifests-directory /srv/lefteria-os-preview/manifests/<verified-backup-stamp> \
  --certificate-directory /srv/lefteria-os-preview/tls
```

Keep configuration, certificates, backups and source manifests root-owned and
private. Do not print `.env`, run a verbose Compose config with real credentials,
commit data or mount source archives into the normal API/web containers.

## Backup and derived import

Back up the original database through read-only operations. Record the exact
source container/image, database, collection counts and archive checksum. Restore
the archive into a capped temporary Mongo container with its own internal
network/volumes and no published ports or production mounts. Validate every
collection and reconcile counts and logical BSON sizes. Disable TTL only in the
temporary clone while verifying/exporting so backed-up expired records remain
available. Preserve the original archive and all restore evidence.

A standalone source has no oplog. A live logical dump on that topology does not
provide a single global point-in-time snapshot, even when before/after counts
match. Do not lock or pause the live application to hide this limitation. Keep
this explicit in backup metadata and later reconciliation.

`export-source-manifests.js` runs only inside the verified isolated clone. Its
environment assertions supplement the operator's actual Docker isolation/hash
checks. Redirect its sole JSON bundle directly into a protected host file; never
display it in a terminal or download production data into the repository. It
retains whole objects in four bounded manifests with chained hashes. Original
BSON types remain recoverable from the retained archive; JSON exports preserve
canonical EJSON wrappers except safely represented numeric values.

The export covers roster, staff, accounts and the six calendar collections.
The full archive retains every other collection. This is not a claim that the
remaining business modules or all legacy APIs have been ported.

## Start and verify

Use this file explicitly with its own protected environment; never execute a
global Docker prune, `down --volumes`, or commands against the legacy project.
Record the three production container IDs, image IDs and start times before
and after staging work. Stop the temporary source clone after verified export
to release memory before starting the preview application.

1. Validate the generated edge, certificate pair and Compose configuration.
2. Start only the new PostgreSQL service and apply migrations through `migrate`.
3. Provision the restricted application role using the mounted PostgreSQL
   scripts, then run its permission probes.
4. Run each import tool dry-run, exact-digest owner apply and replay against
   `lefteria_os_preview`, in roster/staff/accounts/calendar order. Retain safe
   count/issue summaries; source objects and credentials stay private.
5. Investigate refusals/quarantine while preserving the original manifests.
   Never remove source rows, grant extra access, or force readiness to obtain an
   apparently successful preview.
6. Start API/web/edge on loopback, verify health and the Basic/Bearer route
   matrix, then publish only the separate HTTPS edge port. Verify from outside
   the VPS with normal certificate validation.

The [edge and certificate guide](EDGE-AND-TLS.md) describes the authorization
boundary and guarded renewal. Basic credentials must never reach either
upstream. Application Bearer tokens bypass the edge gate only for exact protected
method/path contracts; ASP.NET still enforces signature, expiry and current
record/organization ownership.

Real OTP delivery needs a separately prepared staging provider configuration
and a verified allowed test destination. There is no fixed-code product mode.
No payments, publishing, background notifications or other live providers run
merely because their source data was copied. Backups, provider checks, remaining
module/API/PWA compatibility and a reviewed recovery procedure remain production
cutover gates. A preview deployment does not authorize production cutover.
