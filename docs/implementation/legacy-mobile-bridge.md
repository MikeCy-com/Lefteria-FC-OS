# Bounded legacy mobile login exchange

`POST /api/mobile/auth/exchange-legacy` accepts an original mobile Bearer token
only when the separate legacy bridge is explicitly enabled within its configured
UTC window. It returns the existing `MobileLoginResult` shape with a rewrite JWT,
current profile and available roles. It takes no body, sets `no-store` and has
its own shared 10-request-per-minute client bucket. It does not accept CMS,
customer or rewrite JWTs, generate accounts, send OTPs or call providers.

This implements the exchange option in the
[audited legacy-session contract](legacy-identity-migration.md). Original GitHub
source was rechecked at `a52d66b8e01c010cfe95ce53fa139e126bae4d84`:
[current factory](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/routes/mobile_auth.py#L155),
[older no-jti factory](https://github.com/MikeCy-com/Lefteria-FC/blob/8cf31e8726980b1e489d3d9042447bc9bd6d58d1/backend/routes/mobile_auth.py#L117),
[stored browser session](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/frontend/src/mobile/MobileAuthContext.jsx).
Repository evidence does not prove deployed revisions, actual token populations
or a completed data migration. No production key, token or account was sampled.

## Separate configuration and validation

The default is disabled. Compose and `.env.example` expose these server-only
settings without supplying credentials or an acceptance window:

| Environment variable | Meaning |
| --- | --- |
| `LEGACY_MOBILE_BRIDGE_ENABLED` | Explicit enable switch; default false. |
| `LEGACY_MOBILE_SIGNING_SECRET` | Exact literal UTF-8 source `JWT_SECRET`; never base64-decode it merely because it looks encoded. |
| `LEGACY_MOBILE_FINGERPRINT_KEY` | Independently generated base64 secret with 32–128 decoded bytes. |
| `LEGACY_MOBILE_BRIDGE_STARTS_AT` | Explicit UTC acceptance start, inclusive. |
| `LEGACY_MOBILE_BRIDGE_ENDS_AT` | Explicit UTC acceptance end, exclusive, at most 90 days after start. |

ASP.NET configuration uses the corresponding `LegacyMobileBridge` section:
`Enabled`, `SigningSecret`, `FingerprintKey`, `StartsAt`, `EndsAt`. Enabled
configuration fails startup when required fields are invalid or any two of the
source, fingerprint, rewrite signing and OTP key byte values are reused. The
source key is preserved as literal UTF-8, including historical shorter keys;
this compatibility path does not alter new-token signing requirements. Source
key input is limited to 4096 UTF-8 bytes. Fingerprint input is bounded to 180
encoded characters. Disabled configuration needs no legacy secrets or dates.

Options are captured at startup. Change or disable configuration through the
reviewed restart procedure; this implementation does not hot-reload keys. Do
not enable the bridge until consistent protected imports, staging checks and
the cutover/retirement window have been approved. This code does not authorize
production configuration changes.

The validator supports only the two audited mobile claim families: exact
`alg=HS256`, `typ=JWT`, required `sub`, `phone`, `role`, `type=mobile_access`,
integer `iat` and `exp`, and an optional UUID-D `jti`. Both jti-bearing and
historical no-jti tokens are supported; neither family invents a source `sid`.
Unknown headers/claims, key-discovery URLs, duplicate JSON names, invalid UTF-8,
noncanonical base64url, malformed values, future issuance, expired tokens and
lifetimes exceeding 90 days are rejected. Compact tokens are bounded to 4096
characters. HMAC verification uses the BCL and a fixed-time signature comparison.
No token-supplied URL or key identifier is followed.

These application-specific rules separate the old mobile token from CMS and
new rewrite token realms, following the algorithm and cross-token validation
principles in [RFC 8725](https://www.rfc-editor.org/rfc/rfc8725.html). New bearer
validation still requires its own issuer, audience, signature, type and sid.
Legacy acceptance is confined to this explicit exchange endpoint.

## Subject, permission and session authority

Migration `20261027134652_LegacyMobileBridge` creates only the
`identity.imported_mobile_subjects` view. It exposes two columns:
`legacy_subject` and `identity_id`. Rows must come from an actual mobile import
mapping, agree with its target ID and match the current identity's exact legacy
ID. Native accounts, fan-registration evidence and quarantined source rows do
not acquire a mobile subject mapping through a phone match.

The view uses owner permissions and a security barrier to expose this restricted
projection without granting runtime access to the migration schema or original
mapping/source tables. This is a deliberate reviewed view boundary, consistent
with [PostgreSQL view permissions](https://www.postgresql.org/docs/18/sql-createview.html).
After migrating, reapply the configured runtime grants and permission probes
before starting the updated API. The runtime can read the two view columns;
view writes and private raw mapping access remain denied.

Exchange resolves the exact signed source subject, then locks and reloads the
same-phone profiles and requires matching target ID, full phone, role and fresh
canonical availability. It cannot fall back to another same-phone identity or
restore a revoked child, staff, admin or phone binding. Management organization
access continues to use current grants, including explicit empty grants. A
valid management login does not itself grant any organization. The token is
validated again after the database read so a slow request cannot cross its
deadline or the configured acceptance-window end.

The replacement keeps the exact original `exp`. Its server-controlled sid is a
namespaced HMAC of the original compact token under the independent fingerprint
key, encoded as a version-8 UUID. This is an idempotent token-bound mapping,
without storing raw tokens or a new session registry. Repeated/concurrent
exchanges of the same compact token retain the same sid and expiry; the new JWT
may have a fresh issuance time and jti. Distinct compact tokens get independent
token-bound session provenance. Identical old no-jti tokens from different
devices cannot be distinguished; this does not reconstruct original devices or
claim to preserve an unobserved source device ID.

Successful exchange appends an audit with the target identity ID, derived sid
and fixed reason. Original token text, keys, raw phone and credential hashes are
not stored in the audit. Exchange does not update account/profile timestamps or
registration mirrors. Subsequent role switches retain the same sid and original
expiry. Normal fresh authorization continues on every private operation.

## Browser handoff and recovery

The new Svelte client keeps the existing `mobile_token` storage key. Bootstrap
first calls `/api/mobile/auth/me`. Only a 401 attempts the bodyless legacy
exchange with that same saved Bearer value. A valid current rewrite session does
not call exchange. A successful, validated response replaces storage and updates
roles; late responses after logout or another restored session are discarded.

A definitive exchange 401 clears an invalid saved session. Disabled/outside-
window service, missing endpoint, throttling, network errors, server errors and
malformed responses retain the saved token with unavailable state and no private
profile data. Retry uses that saved value. Existing unavailable screens offer
Retry and an explicit New login/session-clear action to return to phone OTP.
Cached role lists never establish authorization, and the browser does not decode
JWT claims to establish trust.

The handoff requires the new client on the same origin to access existing local
storage. A different origin needs a separate authenticated transfer design.
Cached old React/PWA bundles do not call this endpoint, and the old source cold-
load handler clears storage on a failed `/me`. An approved cutover must therefore
verify client/service-worker update ordering and session retention before traffic
moves. This slice does not establish that old installed bundles have upgraded,
implement a cross-origin handoff, or accept legacy Bearer calls directly across
unported API routes. Those remain explicit compatibility gates.

Retiring the legacy key stops new exchanges. Already exchanged rewrite sessions
keep their original absolute deadline and remain subject to current permissions.
View-only rollback removes the exchange lookup without deleting imported users,
source, mappings or audits. Reapply the migration and runtime view grants to
restore that boundary. EF can complete a later view-only Down before an earlier
populated account migration guard refuses a deeper rollback: restore the bridge
view/grants after that refusal. Do not bypass the account-data guard or delete
imported/verified records. Keep protected backups and the source import checksums.

## Verification

The test executable supports an isolated crypto/configuration command without
opening a host, database or provider:

    dotnet run --project backend/tests/LefteriaFc.Identity.IntegrationTests --configuration Release -- --legacy-token-policy

It tests the actual options/DI validation, all six key-reuse pairs, strict JWT
validation, both historical claim families, malformed signed UTF-8, duplicate
names, NumericDate/lifetime/window boundaries, canonical base64url, deterministic
session provenance and safe error output.

The guarded PostgreSQL scenario additionally exercises all five imported roles,
unknown/native/evidence/quarantined subject denial, signed phone/role mismatch,
CMS/wrong-signature/rewrite token rejection, original-expiry replay and role
switch, concurrent exchange, fresh canonical revocations, current organization
scope, throttling, safe audits and no account/SMS effects. Populated view rollback
and reapply must preserve exact account replay and runtime archive denials. The
current recovery probe executes the view migration's recorded EF Down/Up
operations directly and reapplies its grants, leaving global migration history
and later unrelated modules intact. The populated account-data guard is tested
separately through its actual Down operations in a rollback-isolated transaction.
These tests do not simulate or authorize a global populated-history downgrade;
the operational partial-downgrade recovery condition above still applies.

OpenAPI export asserts the separate legacy bearer scheme, absent request body
and response contracts; the generated TypeScript client drives the browser
handoff. Frontend checks cover successful replacement, retained storage on
temporary failure, malformed responses and stale-response races. Local and CI
results are recorded only after the actual checks complete.

Observed locally on 2026-10-27: all 40 real HTTP/PostgreSQL groups, 26 domain
policy checks and 70 frontend tests passed. The standalone legacy crypto/DI
checks passed. API, importer and guarded preview builds passed without warnings;
OpenAPI export/client generation and Svelte type checking passed. The bridge
view migration applied with no pending EF model changes; restricted permission
probes and populated view recovery passed. No real token, production data,
provider or deployment was involved.
