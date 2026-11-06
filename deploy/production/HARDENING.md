# Production Compose container-hardening change

**Scope:** Production `compose.yaml` only; this change is not a deployment.
The separate locked-down preview and current live deployment are untouched.

## Configuration introduced

All five production services use an anchored security baseline: read-only root
filesystem, `no-new-privileges`, all default Linux capabilities dropped, an
init process, and rotated JSON logs (10 MiB × three files per service).
Temporary writable locations use size-bounded `tmpfs` mounts.

Caddy retains only `NET_BIND_SERVICE` to start its official binary while binding
HTTP/HTTPS, and continues to persist its ACME state through the existing
`caddy_data`/`caddy_config` volumes. The official PostgreSQL entrypoint retains
only `CHOWN`, `DAC_OVERRIDE`, `FOWNER`, `SETGID` and `SETUID`, with its
existing durable database volume and dedicated writable temporary directories.
The API and web run under their existing non-root Dockerfile users. The one-shot
migration remains opt-in under the `tools` profile and does **not** restart.

Only Caddy publishes host ports. The existing private application/data networks,
API outbound-provider network, explicit trusted-proxy address, and credential
bindings are unchanged.

## Suggested initial resource ceilings (tune to measured VPS capacity)

| Service | Memory | CPU cores | PID limit |
| --- | ---: | ---: | ---: |
| Caddy | 128 MiB | 0.5 | 96 |
| SvelteKit web | 512 MiB | 1.0 | 160 |
| ASP.NET API | 768 MiB | 1.0 | 160 |
| PostgreSQL | 768 MiB | 1.0 | 160 |
| One-shot migration tool | 512 MiB | 1.0 | 128 |

Each memory/CPU value can be adjusted without editing tracked Compose YAML:

- `LEFTERIA_PROXY_MEMORY_LIMIT`, `LEFTERIA_PROXY_CPUS`
- `LEFTERIA_WEB_MEMORY_LIMIT`, `LEFTERIA_WEB_CPUS`
- `LEFTERIA_API_MEMORY_LIMIT`, `LEFTERIA_API_CPUS`
- `LEFTERIA_POSTGRES_MEMORY_LIMIT`, `LEFTERIA_POSTGRES_CPUS`
- `LEFTERIA_MIGRATE_MEMORY_LIMIT`, `LEFTERIA_MIGRATE_CPUS`

The defaults are starting bounds **not validated against the Hostinger VPS**.
The running machine might be smaller or have other workloads. Review available
memory, CPU, concurrency, page-cache needs, and PostgreSQL maintenance operations
before release. A memory cap can result in an OOM restart; a low CPU cap may cause
latency. The limits intentionally do not assert guaranteed capacity.

## Verification and release gate

The existing GitHub Actions `Docker Compose build` job validates the fully
resolved production configuration, builds containers, checks migration and
runtime database roles, and runs API/web health probes. A new contract check
(`deploy/production/verify-compose-security.py`) rejects a Compose
configuration that loses a required hardening control, publishes private service
ports, or causes migrations to restart. It accepts only the explicitly retained
edge/PostgreSQL capabilities. This is build-time evidence, **not** a live VPS test.

Before a human-approved production deployment:

1. Record running container/image digests, existing volumes, credentials and
   database revision; confirm the exact correct Compose project and target host.
2. Run isolated restore/backup verification and confirm rollback procedures.
3. Check the VPS memory/CPU capacity and size-specific resource overrides.
4. Re-run Compose config validation in a protected environment without printing
   expanded credentials. Validate the Caddy config and all container startup,
   DNS, TLS, web, API, PostgreSQL, SMS and admin sessions.
5. Observe memory, CPU, restarts, log rotation and permissions under typical and
   burst traffic. Roll back the **application image/config** if needed; database
   rollback requires a separately verified plan.

Do **not** enable untested profiles, rotate credentials, modify the live source
database or switch the main domain automatically as part of this PR.
