# Isolated Hostinger preview

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

Deployment observed on 2026-10-31:

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
- All four services run in the separate `lefteria-os-preview` Compose project,
  using `/srv/lefteria-os-preview/source`. The complete source was transferred
  from a verified GitHub archive; private repository credentials were not copied
  to the VPS. API/web/owner-import images are the verified main build at
  `3b0a8dcb2c0b5d66cec95013e863efdedcd0e796`; later edge/operator fixes do not
  change those application binaries.
- PostgreSQL migrations, restricted runtime-role provisioning and the repository's
  rolled-back permission probes passed. Each of the four source manifests passed
  validation, exact-digest owner import and same-snapshot no-op replay.
- The copied roster contains one team, six academy groups and 36 players. Staff
  import contains three staff, three team assignments, three group assignments
  and 150 contact bindings. All original source documents remain private.
- Account import preserved 22 source rows, projecting 13 mobile identities, one
  admin and five fan-evidence rows, with three quarantined source rows. One admin
  points to missing staff; its linked management account consequently remains
  quarantined. One fan-evidence mirror lacks both required name components;
  the corresponding mobile fan identity imported. No automatic relinking or
  fabricated names were applied.
- Calendar import preserved all 783 source rows and projected all 627 schedule
  items. Two RSVP rows reference one event absent from every schedule collection.
  They remain quarantined and calendar readiness remains false. Raw rows were
  retained; no readiness flag was forced. Recovery needs an audited forward
  resolution, retaining the original evidence.
- The media backup and isolated restore matched all 171 regular files and their
  hashes. That copy stays private and is not exposed by the new web service.
- The generated edge passed 49 actual loopback HTTPS/access checks. A hosted
  Basic-401 header omission was fixed in main PR #14 and verified in the actual
  pinned Caddy container. Invalid JWTs reach the API's normal JSON 401 contract;
  missing credentials, wrong methods and unlisted routes retain the Basic gate.
- Public HTTPS is available at
  `https://srv1649602.hstgr.cloud:8443/app/login`. Both advertised address families
  publish only edge port 8443. The host firewall needed a narrow IPv6 TCP-8443
  rule because that mapping uses the host's IPv6 proxy. External anonymous
  requests return 401; authenticated login and database readiness return 200
  with normal certificate validation, approximately 0.3 seconds after the fix.
  Database/API/web ports remain unpublished.
- The dedicated preview certificate expires on 2027-01-29. Its isolated renewal
  timer is installed; service invocation and one Certbot simulated renewal with
  deploy hooks passed. Publication, preview-only edge reload and owned challenge
  cleanup succeeded without production container/configuration changes.
- Original-project PR #46 was deployed independently by the
  `Lefteria FC developer agent` chat during this work. Its replacement frontend
  and backend were reviewed against commit
  `380b4022f0ab0db95fbc52a8613711160a5037a5`; nginx's configuration hash and the
  Mongo container were unchanged. Original baseline/guard evidence was retained,
  and only the preview-owned guard adopted the reviewed frontend identity.
  Subsequent preview publication and renewal checks preserved the reviewed
  production IDs, images and start times; the live homepage returned 200.

Private access credentials, configuration, backups, manifests, diagnostic logs
and detailed import evidence remain outside GitHub. The separate Basic gate
credentials are delivered through a protected local access file, not this
document. They are not an application login or an OTP bypass.

The user explicitly paused Twilio setup. SMS credentials remain empty and the API
has no outbound provider network, so normal SMS-OTP login and a valid-user hosted
workflow have not been verified. No fixed OTP or test authentication host is
deployed. Parent provider credentials must stay out of the new runtime, and
production phone numbers must not be transferred. No SMS was sent. The adapter
supports bounded ASCII alphanumeric senders as well as normalized phone senders,
matching the preserved source integration; fake-handler regression checks cover
valid encoding and fail-closed rejection before HTTP.

No production cutover, domain reassignment, payment/publishing integration or
remaining module migration is implied by this preview work.
