# Isolated Hostinger preview

This deployment is a separate `lefteria-os-preview` Compose project. It must not
reuse the legacy project's containers, networks, database volumes, keys or
published ports. It serves a separate hostname on HTTPS port 8443; it does not
attach or redirect `lefteriafc.cy`.

`compose.yaml` uses verified prebuilt image IDs, separate owner/runtime database
credentials, fresh preview token/OTP keys, private application/data networks,
read-only application filesystems, dropped capabilities, process/memory/CPU
limits and rotated logs. Only the edge publishes a port. Its default bind is
loopback in both address families (`PREVIEW_BIND=127.0.0.1` and
`PREVIEW_BIND_IPV6=::1`) until TLS, authentication and private-service checks pass. The API has
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

## Upgrade an existing preview

`prepare-environment.py` is a fresh-install bootstrap, not an upgrade command.
It refuses an existing configuration directory. Do not rename that directory to
bypass the refusal: it generates new database passwords, mobile signing/OTP keys
and gate credentials, and resets the edge to loopback. A persistent PostgreSQL
volume does not change its existing role passwords when container environment
variables change.

Use the verified new release images/source and a guarded operator process for
this sequence. The operator must verify the exact Compose project, container
IDs, image provenance, private paths and existing configuration before changes.
Keep a private record of the original configuration, source/image IDs and
production container IDs/start times. Never source the dotenv file as a shell
script, put secrets in command arguments, or print resolved Compose configuration.

The commands below use these non-secret paths and exact project/database names:

```sh
PREVIEW_ROOT=/srv/lefteria-os-preview
PREVIEW_ENV="$PREVIEW_ROOT/config/.env"
PREVIEW_COMPOSE="$PREVIEW_ROOT/source/deploy/preview/compose.yaml"
preview_docker() {
  docker --host unix:///var/run/docker.sock "$@"
}
preview_compose() {
  preview_docker compose --project-name lefteria-os-preview --env-file "$PREVIEW_ENV" \
    --file "$PREVIEW_COMPOSE" "$@"
}
```

`PREVIEW_PG_ID` must be the verified full ID of the sole `postgres` service in
`lefteria-os-preview`, with its existing dedicated volume. Resolve and inspect
that ID before the commands below; it must never identify the legacy database.

1. Before migrating, make a root-only PostgreSQL backup of the **preview** using
   its owner role. Retain the old environment/access files and release metadata
   privately alongside the recovery evidence. For example, after setting the
   verified `PREVIEW_PG_ID`:

   ```sh
   : "${PREVIEW_PG_ID:?Set the verified full preview PostgreSQL container ID}"
   umask 077
   PREVIEW_DB_BACKUP="$PREVIEW_ROOT/backups/postgres-before-web-platform-$(date -u +%Y%m%dT%H%M%SZ)"
   mkdir -m 700 -- "$PREVIEW_DB_BACKUP"
   preview_docker exec "$PREVIEW_PG_ID" pg_dump --no-password \
     -U lefteria_os_preview_owner -d lefteria_os_preview --format=custom \
     > "$PREVIEW_DB_BACKUP/database.dump"
   test -s "$PREVIEW_DB_BACKUP/database.dump"
   preview_docker exec -i "$PREVIEW_PG_ID" pg_restore --list \
     < "$PREVIEW_DB_BACKUP/database.dump" > "$PREVIEW_DB_BACKUP/archive-list.txt"
   sha256sum "$PREVIEW_DB_BACKUP/database.dump" > "$PREVIEW_DB_BACKUP/SHA256SUMS"
   ```

   Stop on any command failure. Listing/checksumming an archive is not a restore
   test; verify recovery in a separate capped disposable PostgreSQL instance,
   never by overwriting either working database. Backup contents stay on the VPS.
2. Atomically extend the existing root-owned `0600` environment file within its
   `0700` directory. Preserve the exact five existing values
   `PREVIEW_OWNER_PASSWORD`, `PREVIEW_APP_PASSWORD`, `PREVIEW_SIGNING_KEY`,
   `PREVIEW_OTP_KEY`, `PREVIEW_GATE_HASH`, and the unchanged gate access file.
   Preserve the hostname, both published binds, TLS directory and original
   `PREVIEW_MANIFEST_DIRECTORY`. Append `PREVIEW_WEB_ADMIN_ENABLED='false'`,
   `PREVIEW_ADMIN_AUTH_PASSWORD` and `PREVIEW_ADMIN_THROTTLE_KEY`. Generate the
   two new secrets independently from 32 random bytes: URL-safe base64 without
   padding for the database password, standard base64 for the throttle key.
   They must differ from existing credentials/keys. If any new key already
   exists, inspect the prior upgrade state instead of replacing it. Update only
   the verified source SHA and immutable API/web/import image IDs as release
   metadata; retain a private before/after integrity record.
3. Validate Compose with `preview_compose config --quiet`, then run
   `preview_compose --profile tools run --rm --no-deps migrate`. The normal API
   never migrates on startup. Reapply the mobile/public projection grants as
   owner, then explicitly provision the separate admin-auth role:

   ```sh
   preview_docker exec "$PREVIEW_PG_ID" psql -X -v ON_ERROR_STOP=1 \
     -U lefteria_os_preview_owner -d lefteria_os_preview \
     -v app_role=lefteria_os_preview_app \
     -f /opt/lefteria-postgres/identity-app-grants.sql
   preview_docker exec --env DATABASE_ADMIN_AUTH_PASSWORD --env DATABASE_APP_PASSWORD \
     "$PREVIEW_PG_ID" psql -X -v ON_ERROR_STOP=1 \
     -U lefteria_os_preview_owner -d lefteria_os_preview \
     -v app_role=lefteria_os_preview_app \
     -v admin_auth_role=lefteria_os_preview_admin_auth \
     -f /opt/lefteria-postgres/create-web-admin-auth-role.sql
   ```

   For the second command, the operator supplies `DATABASE_ADMIN_AUTH_PASSWORD`
   from `PREVIEW_ADMIN_AUTH_PASSWORD` and `DATABASE_APP_PASSWORD` from the
   preserved `PREVIEW_APP_PASSWORD` only in the Docker client's subprocess
   environment. The flags intentionally contain names without values. These
   commands use the existing container's owner socket access; they do not
   recreate it. The mounted SQL files must match the verified new source.
   `WebAdmin__Username` in Compose is exactly
   `lefteria_os_preview_admin_auth`; its password is a database credential, not
   an imported administrator's application password. The role script refuses
   missing migrations and unsafe existing roles; it never rotates a role.
4. Run `identity-app-permissions-check.sql` as `lefteria_os_preview_app`, then
   `web-admin-auth-permissions-check.sql` as `lefteria_os_preview_admin_auth`,
   using `psql -X -v ON_ERROR_STOP=1 -h 127.0.0.1 -d lefteria_os_preview` inside
   the verified PostgreSQL container, with `-U` set to that probe's exact role
   and `-f /opt/lefteria-postgres/<probe-file>`. The admin probe also requires
   `-v app_role=lefteria_os_preview_app` and
   `-v admin_auth_role=lefteria_os_preview_admin_auth`. Supply the corresponding
   `PGPASSWORD` only through the operator's per-command subprocess environment
   and `preview_docker exec --env PGPASSWORD`; never echo it. All fictional
   probe writes roll back. Require both probes to pass: the mobile role must still be denied
   credential hashes/admin session state, and the admin-auth role must have no
   account/grant writes, mobile identities, source archives or DDL access.
5. Keep the original four-manifest directory unchanged. For the fifth manifest,
   set `PREVIEW_PUBLIC_MANIFEST` to its verified root-private absolute JSON path
   and `PREVIEW_PUBLIC_SHA256` to its exact verified digest. Add only this file
   to the one-off owner-import container:

   ```sh
   : "${PREVIEW_PUBLIC_MANIFEST:?Set the verified public-content manifest path}"
   : "${PREVIEW_PUBLIC_SHA256:?Set its verified SHA-256}"
   preview_compose --profile tools run --rm --no-deps \
     --volume "$PREVIEW_PUBLIC_MANIFEST:/public-content-manifest.json:ro" \
     import --kind public-content --input /public-content-manifest.json
   preview_compose --profile tools run --rm --no-deps \
     --volume "$PREVIEW_PUBLIC_MANIFEST:/public-content-manifest.json:ro" \
     import --kind public-content --input /public-content-manifest.json --apply \
     --expected-sha256 "$PREVIEW_PUBLIC_SHA256" \
     --expected-database lefteria_os_preview --environment staging
   ```

   Omission of `--apply` is the dry run; there is no `--dry-run` flag. Require
   validation to pass before apply, then repeat the exact apply command and
   require `same_snapshot_noop`. Retain counts/digest evidence privately. Do not
   mount any source manifest or private uploads into API/web/edge.
6. Only after migration, role probes and imports succeed, explicitly set
   `PREVIEW_WEB_ADMIN_ENABLED='true'` atomically, retaining both new secrets.
   Run `preview_compose up -d --no-deps api web`. Do not use an unscoped `up`,
   `down`, or recreate PostgreSQL to pick up its new environment variable;
   explicit role provisioning already supplied that secret to its one-off
   process. Validate the generated Caddyfile against the new contract and
   explicitly reload the verified preview edge if its content changed; a bind
   file update alone does not reload Caddy. Verify health, trusted TLS in both
   families, gate/header stripping, public projections and admin cookie/Origin
   boundaries. Existing imported active, non-invited accounts with supported
   bcrypt credentials can use their original username/password. Do not seed a
   default administrator or reset credentials to make a smoke check pass.
7. Twilio remains paused: keep its settings empty, provider egress closed and
   the legacy bridge disabled. Accounts requiring two-factor authentication
   must fail closed with `two_factor_unavailable`; no SMS bypass is enabled.
   For application rollback, disable admin auth and restore the recorded prior
   image/source configuration while refreshing only the preview application
   services. Retain the added schema, new keys and private source/evidence.
   The migration's `Down` refuses recovery once public content, admin sessions
   or admin audit evidence exists; use forward recovery instead of deleting
   those rows. Restoring an older backup discards later preview writes and
   requires a separate controlled recovery procedure. Never restore it into
   legacy production or alter its domain/services during this upgrade.

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

Public publication is an explicit operator change for each address family:
`PREVIEW_BIND=0.0.0.0` enables IPv4 and `PREVIEW_BIND_IPV6=::` enables IPv6
(specific approved addresses may also be used). Keep both generated defaults
on loopback during preparation. If the preview hostname advertises both A and
AAAA records, verify HTTPS on both families before handing over the URL; an
IPv4-only listener can cause slow IPv6 connection fallback. These bindings do
not change the production project's ports, DNS or domain configuration.

Inspect the host/cloud firewall too. An IPv6 Docker publication may use a host
proxy and require an INPUT allow rule even when the IPv4 Docker mapping works.
On the verified UFW host, the narrowly scoped rule was
`ufw allow proto tcp from ::/0 to any port 8443 comment lefteria-os-preview-https`.
Review the rule with `--dry-run` before applying it, preserve existing rules,
and verify external trusted TLS afterward. Do not disable the firewall or open
database/upstream ports to solve preview reachability.

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
