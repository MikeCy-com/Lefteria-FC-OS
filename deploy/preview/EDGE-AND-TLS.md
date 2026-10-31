# Preview edge and certificate boundary

This configuration belongs only to the separate Hostinger preview. It does not
bind host ports 80/443, reuse production TLS/JWT keys, change production nginx
configuration or restart the production containers. HTTPS uses the reviewed VPS
hostname on port 8443 and its own certificate, ACME account/state and renewal job.

Generate a **new** Caddyfile from the exact deployed API contract:

```sh
node deploy/preview/generate-edge.mjs \
  --contract contracts/openapi.json --host srv1649602.hstgr.cloud \
  --output /protected/new-release/Caddyfile
node deploy/preview/generate-edge.mjs \
  --contract contracts/openapi.json --host srv1649602.hstgr.cloud \
  --output /protected/new-release/Caddyfile --check
```

The file records the contract hash. Normal generation refuses to overwrite an
existing file. Only operations with exactly `security: [{ Mobile: [] }]` receive
a Bearer route, bound to their exact method and anchored path. Template parameters
match one non-empty path segment. The current contract has twelve such operations.
Ambiguous Mobile security, global security defaults and unsafe templates refuse
generation. Newly unmarked/anonymous routes remain behind the Basic gate.

The Basic username is `preview`; configure only a generated bcrypt hash through
`PREVIEW_GATE_HASH`. Pages, public avatars, health endpoints and anonymous login,
OTP and legacy exchange routes require that gate. A Bearer header cannot bypass
it on those paths. Explicit protected routes preserve the Bearer header and still
require the API's valid JWT/current role and record scope. An invalid token can
reach only that protected API operation, which rejects it. The Basic fallback
removes Authorization before either API or SvelteKit forwarding; Basic credentials
must never reach application code. This avoids putting both schemes in the same
Authorization header. See [Caddy authentication](https://caddyserver.com/docs/caddyfile/directives/basic_auth)
and [request matchers](https://caddyserver.com/docs/caddyfile/matchers).

All responses carry no-store/noindex, no-referrer, nosniff and frame-denial headers.
The Caddy admin listener is confined to its container's loopback for preview-only
certificate reloads. It must never be published to a host port. These controls
supplement the existing API authorization; they do not create a test login or
enable SMS. Real provider setup remains an explicit staging configuration step.

## Existing-route HTTP-01 hooks

The live frontend already has an HTTP-01 location rooted at `/var/www/certbot`.
The preview hooks create only its new directories when absent and one uniquely
named public challenge file. They do not edit the route or any app/database data.
The checked frontend container ID and SHA-256 of **stdout** from `nginx -T` must
match the protected guard before any token creation or removal. A changed live
container/configuration stops renewal until an operator reviews and updates the
guard; the hook must not silently adopt a changed production deployment.

`PREVIEW_ACME_GUARD_FILE` must point to a root-owned private regular JSON file
inside `/srv/lefteria-os-preview/acme`. Its parent path must have no symlinks or
directories writable by other users. Example shape (replace both hashes from
actual read-only inventory):

```json
{
  "schema_version": 1,
  "hostname": "srv1649602.hstgr.cloud",
  "frontend_container_id": "64-lowercase-hex-characters",
  "nginx_config_sha256": "64-lowercase-hex-characters"
}
```

The hook admits only that hostname, a bounded token alphabet and matching HTTP
key-authorization bytes. Shell code is static; the token is a separate argument
and validation bytes arrive on stdin. Root-owned directories cannot be symlinks
or writable by others. POSIX noclobber refuses any existing challenge file or
symlink. Ownership state is kept under the private ACME directory. Cleanup requires
the exact successfully created ownership record, regular-file metadata and an
exact byte comparison before removing that token alone. It never removes a
directory, an unknown file, an existing certificate or a production record.
Uncertain/partial creation retains pending evidence for an operator.

Use installed Certbot's `certonly --manual --preferred-challenges http` with
`--manual-auth-hook 'python3 /protected/hooks/auth-hook.py'` and
`--manual-cleanup-hook 'python3 /protected/hooks/cleanup-hook.py'`. Supply separate
`--config-dir`, `--work-dir` and `--logs-dir` under the preview ACME directory.
Do not use the nginx installer or standalone authenticator. The auth hook checks
the existing HTTP route locally; external DNS/reachability must also be verified
before issuance.

After issuance or renewal, run `python3 deploy/preview/publish-certificate.py`
as root, without arguments. It reads only the dedicated Certbot live links under
`/srv/lefteria-os-preview/acme/config/live/srv1649602.hstgr.cloud`, requires their
regular-file targets to be in that hostname's dedicated archive at one matching
generation, and validates the exact hostname SAN, system trust, validity window
(at least seven days remaining) and matching public keys with OpenSSL. It never
reads the production certificate tree. It publishes a dereferenced regular PEM
pair with mode 0600 under `tls/versions/<certificate-DER-SHA256>`, then atomically
changes the contained relative `tls/current` symlink. Existing version files
cannot be overwritten; exact replay verifies their bytes. A private lock
serializes publication. Invalid or incomplete pairs cannot advance `current`.

Mount `/srv/lefteria-os-preview/tls` read-only into the preview edge as
`/certificates`, with the pair read at `/certificates/current/fullchain.pem` and
`/certificates/current/privkey.pem`. Do not mount a Certbot `live` directory alone:
its symlinks point into a separate archive directory. Do not mount the whole
ACME configuration/account tree into Caddy. The publisher makes no service reload.

The included `lefteria-os-preview-renew.service` targets only that ACME config
and certificate name. Its deploy hook runs `renew-preview-tls.py` only after a
new certificate is renewed. The hook invokes the fixed publisher, then reloads
only one running container after checking its immutable ID and exact Compose
project `lefteria-os-preview` and service `edge` labels. Missing, multiple,
paused, restarting or changed containers fail closed. It uses the local Docker
socket and a clean subprocess environment, never an ambient Docker context.
No container name, Compose-wide reload or production reload is accepted.

The timer checks twice daily with randomized delay and persistent catch-up.
The service has CPU/memory/I/O bounds and writes only the preview's ACME/TLS
paths. Install it only after the preview edge exists. Never replace the
production Certbot timer, reload production nginx, or stop it to free a port.
Periodically check certificate expiry and timer results; a refused renewal is an
actionable failure, not permission to bypass the guard. If publication succeeds
but edge reload fails, fix the cause and rerun the fixed renewal hook explicitly;
the certificate may no longer be due for renewal on the next timer check. See
[Certbot validation hooks](https://eff-certbot.readthedocs.io/en/stable/using.html#pre-and-post-validation-hooks)
and [fixed ACME validation ports](https://letsencrypt.org/docs/challenge-types/).

## Verification

```sh
node --test deploy/preview/generate-edge.test.mjs
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s deploy/preview -p 'test_*.py'
```

Before publishing the edge, validate its generated config with the actual Caddy
image and isolated test certificates. Test missing/wrong Basic credentials,
Basic with anonymous OTP routes, invalid Bearer on private routes, invalid Bearer
on OTP/legacy/public/unlisted paths, wrong HTTP methods, nested/trailing template
segments, security headers and Basic stripping at both upstreams. Distinguish the
edge's Basic challenge from the API's Bearer rejection. Never use live SMS for
this matrix. A separate real API smoke must verify valid scoped JWT traffic.

The source tests do not claim a hosted certificate, renewal or edge deployment
has happened. Record actual issued SAN/expiry, protected-route smoke outcomes and
unchanged production health when those steps are performed.
