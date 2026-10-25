# Mobile identity implementation

This slice implements the pinned [mobile contract](mobile-identity-contract.md)
on the target stack. Source main was rechecked on GitHub on 2026-10-25 and remains
5111f06af5d8b17f855ceed9637caca02db117e2. This is implementation in the rewrite;
no production accounts, credentials, data or services have been changed.

## Implemented behavior

- Request OTP and stage fan registration; no personal-field changes before proof
  of phone possession. Unknown phones receive a real delivery attempt, followed
  by 404 after correct proof unless fan signup was staged.
- HMAC-SHA256 OTP digest bound to the challenge and canonical full phone. Five
  minutes, five verification calls, row-locked atomic consumption, no debug code
  or simulated login. Failed older delivery cannot remove a newer challenge.
- Shared PostgreSQL IP/destination request throttles and verification throttles.
  Trusted forwarded headers come only from an explicitly configured proxy.
- JWT validation for a dedicated mobile issuer/audience, signature, realm and
  expiry. Explicit role picker issues no token. A role switch retains the device
  session ID and exact JWT expiration. Grants are checked against current rows.
- Revoked formal roles cannot access operational context or select another
  formal role; the user may deliberately recover their active same-phone fan.
- Management FC/Academy assignments, explicit selection when required, and
  persisted sole-organization activation. Empty assignments grant no scope.
- `/app/login`, `/app` and existing navigation/deep-link aliases consume the
  generated API client. OTP stays in controller memory; browser storage retains
  only the existing token/role keys. The server never trusts cached roles.
- Header role changer, notifications/profile actions and role-specific bottom
  navigation. Parent, coach and management have no composer. Unsupported
  destinations say **Under Developments**; profile home shows actual own fields.

## Database and local containers

The initial EF migration creates identity, organization and audit tables with
phone/role uniqueness, canonical phone checks, OTP bounds and relational keys.
Active admin organization references an actual assignment. Assignment mutations
must lock the owning admin row before changing grants. Runtime code cannot make
those mutations. See [runtime grants](postgres-runtime-role.md).
An owner import must insert an admin with no active organization, insert its
assignments, then select the active organization; the composite FK enforces that
ordering.

For a fresh **dedicated development database**, configure `.env` from
`.env.example`: distinct strong owner/runtime passwords and independently
generated base64 signing/OTP keys (at least 32 random bytes each). Set the proxy
IP within the configured app subnet; change both if that subnet is occupied.

```sh
docker compose build
docker compose up --wait postgres
docker compose run --rm migrate
docker compose exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v app_role="$DATABASE_APP_USER" -f /opt/lefteria-postgres/create-app-role.sql'
docker compose up --no-build --wait
```

The migrator is an explicit tools-profile command, using separate migration
credentials. Ordinary API startup has no DDL or migration behavior. The API role
cannot create formal identities, alter grants, change role/status/phone links,
read or edit audit history, or change schema. The SQL setup hardens PUBLIC
CREATE/TEMP privileges in this dedicated database; never apply it to a shared DB
without a separate plan. Reapply grants/policies after schema recreation.

Only Caddy publishes ports. App and data networks remain internal; a separate
API egress network permits outbound SMS HTTPS. No database port is published.
Twilio settings are optional for startup; missing settings cause OTP delivery
to fail, never an authenticated session. Configure real providers only through
an approved staging integration exercise.

## Verification and recovery

The dependency-light HTTP test executable uses the real ASP.NET pipeline and
PostgreSQL under the restricted runtime role. Only its test assembly injects a
capturing SMS fake. Its fixture guard requires the named disposable loopback
database; no product fake flag exists. See
[test commands](../../backend/tests/LefteriaFc.Identity.IntegrationTests/README.md).

The initial migration was applied, reverted to zero and reapplied successfully
on the empty disposable PostgreSQL 18.6 fixture. A down migration drops identity
and audit data; this drill does not authorize destructive rollback after real
data import. Production recovery must restore a verified backup or use a
reviewed forward migration. GitHub Actions includes the same isolated migration
and HTTP checks, plus container startup checks. Docker is unavailable locally;
no local container pass is claimed.

Observed local results: API/test Release builds have zero warnings/errors;
26/26 policy checks, 18/18 HTTP/PostgreSQL groups and 29/29 frontend tests pass.
Svelte check reports zero errors/warnings; production build and production-only
web packaging smoke pass, including `/app/login`, `/app` and `/app/calendar`.
The API smoke verifies 200 liveness, 503 unavailable database, bearer metadata
and both OTP verification result variants. Both deployment YAML files parse.
The restricted-role SQL probes and idempotent setup checks pass.

GitHub check annotations for foundation head 1adc20a report jobs were not started
because recent account payments failed or the spending limit needs increasing.
This is an account-side CI blocker; no GitHub pipeline pass is claimed.

## Remaining compatibility gates

Legacy JWT acceptance, reviewed account/role import, real child/team/group
relationships, admin authentication/permissions, last-active-super-admin
invariants, provider integration, push unsubscribe on logout, PWA/TWA behavior
and operational workflows remain incomplete. The new client preserves legacy
storage keys but cannot accept old tokens yet. Profile and social/media privacy
contracts must be ported before those screens can expose data. Browser visual
parity is not verified by type checks or HTTP smoke.
