# Isolated preview preparation

The user requested a separate Hostinger project, a protected copy of existing
data and a preview URL while the live site and `lefteriafc.cy` keep working.
The [operator guide](../../deploy/preview/README.md) and
[edge/TLS guide](../../deploy/preview/EDGE-AND-TLS.md) describe the deployment.

The preview uses image bundles built from successful main checks, an independent
Compose project, database roles, keys, networks and volumes, and a separate
HTTPS port/hostname. Its Basic access gate covers pages and anonymous routes;
only exact generated protected API method/path contracts accept Bearer traffic
directly, with normal ASP.NET authentication/authorization still required.
Basic credentials are removed before proxying to either application service.

The official Caddy binary's `NET_BIND_SERVICE` file capability requires that
one capability to remain in its bounding set even on port 8443. The actual
read-only validation container failed to execute with every capability removed
and passed with only this capability restored. Application containers retain
their dropped capabilities; this does not alter host or production privileges.

Preparation observed on 2026-10-31:

- A protected source Mongo backup was restored in a capped, isolated container.
  All collection counts and logical BSON sizes matched; full validation passed.
  The standalone live dump has no single global point-in-time guarantee.
- Four byte-exact manifests were exported from that clone with chained hashes;
  all source fields and the full original BSON archive remain protected on the
  VPS. The clone was stopped after export to release memory.
- A dedicated preview certificate was issued and validated through its own
  Certbot state. Temporary, individually owned validation files used the existing
  HTTP challenge route. No nginx configuration edit/reload or live container
  restart was required; the live homepage continued returning HTTP 200.
- The certificate publisher safely replayed its versioned regular PEM pair,
  and the generated edge validated in the actual Caddy image. Linux private-file
  preparation tests passed, including atomic nonreplacement and cleanup checks.

This checkpoint does not claim an application deployment or successful real-data
import. Record the exact published bundle/main SHA, import count/quarantine
results, runtime permissions, edge routing checks, external TLS and renewal
status before reporting the preview as available.

The user chose separate Twilio staging credentials. Parent provider credentials
must stay out of the new runtime, and production phone numbers must not be
transferred. No real SMS is sent by the verification tools. The adapter now
supports bounded ASCII alphanumeric senders as well as normalized phone senders,
matching the preserved source integration; fake-handler regression checks cover
valid encoding and fail-closed rejection before HTTP.

No production cutover, domain reassignment, payment/publishing integration or
remaining module migration is implied by this preview work.
