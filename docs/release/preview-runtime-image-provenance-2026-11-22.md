# Lefteria FC OS — staging runtime image provenance (read only)

**Status:** Runbook and offline verifier implemented. **NO HOSTINGER ATTESTATION HAS BEEN EXECUTED.** Production remains NO-GO. This is a subset of [release gate #446](https://github.com/MikeCy-com/Lefteria-FC-OS/issues/446), not a substitute for G0 isolation, staging rollout, backups, role tests, or rollback.

## Why

A successful GitHub build and published preview bundle do not prove that an operator actually installed those images on the staging VPS. The manual HTTP readiness workflow cannot identify an image revision from a 200 response. The source CLI in \`ops/release/verify-preview-runtime-images.mjs\` compares *minimal sanitized evidence* from a separately approved preview host to the exact source SHA and published preview bundle \`manifest.json\`.

The CLI does **not** connect to Hostinger, Docker, databases or other services; it does not perform deployment. It trusts operator-supplied observation JSON only as a local comparison. An independent infrastructure reviewer must verify its origin, timestamp and container identities. Never infer a valid G0 from this script.

## Prerequisites — stop without them

1. Independently document production vs staging VPS identity, Docker Compose project, networks, volumes, databases, credentials/providers, ingress and webhook targets. Confirm no shared writable resources. Sign the *G0* review ticket in an access-controlled evidence location.
2. Choose an **exact 40-character current main SHA** with successful **main-push** Rewrite build and Browser UI audit. The successful image bundle is a GitHub prerelease named \`preview-<sha>\`; privately verify \`SHA256SUMS\` and \`manifest.json\` match downloaded archives.
3. On the independently verified **preview-only** VPS, inspect existing running preview \`api\` and \`web\` container full IDs. **Do not invent, guess or discover IDs from the live application.** Never run this procedure against live production.
4. Keep all observations in a protected private directory with mode 0700. Do not upload raw \`docker inspect\`, \`docker compose config\`, environment, credentials, parent/child records, payment data or original source manifests to GitHub Actions/artifacts/issues.

## Capture only whitelisted fields — operator on approved preview host

The following Docker template returns a minimal **single line of JSON per container**; it does *not* include environment variables, bind mounts, secrets or the full \`Config.Labels\` object. Run it only against two separately verified full preview container IDs.

~~~bash
umask 077
API_ID='<verified full preview API container ID>'
WEB_ID='<verified full preview WEB container ID>'

FORMAT='{{printf "{\"service\":%q,\"project\":%q,\"image_id\":%q,\"image_ref\":%q,\"revision\":%q,\"source\":%q,\"running\":%t,\"healthy\":%t,\"read_only\":%t}" (index .Config.Labels "com.docker.compose.service") (index .Config.Labels "com.docker.compose.project") .Image .Config.Image (index .Config.Labels "org.opencontainers.image.revision") (index .Config.Labels "org.opencontainers.image.source") .State.Running (eq .State.Health.Status "healthy") .HostConfig.ReadonlyRootfs}}'

docker inspect --type container --format "$FORMAT" "$API_ID" > api-sanitized.json
docker inspect --type container --format "$FORMAT" "$WEB_ID" > web-sanitized.json

OBSERVED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
{
  printf '{"schema_version":1,"observed_at_utc":"%s","containers":[' "$OBSERVED_AT"
  cat api-sanitized.json
  printf ','
  cat web-sanitized.json
  printf ']}\n'
} > preview-runtime-observations.json
chmod 600 preview-runtime-observations.json
~~~

The container IDs are never placed inside the resulting JSON. The template does not probe a database or any live service and has no mutation operation. Do not paste the output into chat or a public issue; keep it as private evidence for a reviewer.

## Offline comparison — approved candidate

Run from the same exact vetted repository checkout; \`manifest.json\` must be the checksum-validated file from the verified preview bundle:

~~~bash
node ops/release/verify-preview-runtime-images.mjs \
  --sha=<exact-current-main-sha> \
  --manifest=/private/path/to/verified/manifest.json \
  --observations=/private/path/to/preview-runtime-observations.json
~~~

Require exit code 0 **and** \`verified:true\`. It rejects stale/future observations (older than 30 minutes), incorrect project, missing/extra services, duplicate service labels, stopped/unhealthy containers, a writable root filesystem, wrong OCI revision/source, wrong image ID/reference and mismatched source manifest. Invalid evidence never echoes the supplied private fields.

A PASS establishes only that the **operator-captured API/web labels and image IDs agree with the chosen published manifest**. It does not authenticate the origin of the input file, verify PostgreSQL/images/permissions end-to-end, inspect a separate live installation, or prove a backup can be restored. Archive checksums, image labels, G0 isolation, real HTTP staging preflight, authorized synthetic five-role journeys, and recovery rehearsal must each have separate evidence.

**Release policy:** Do not update \`go-live-gates.json\` to \`verified\` based solely on this result. No production cutover without protected branch controls, fresh staging acceptance, source-data reconciliation, full recovery and five explicit approvals.
