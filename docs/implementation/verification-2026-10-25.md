# Foundation verification — 2026-10-25

This records the foundation checkpoint at 1adc20a3d720cc9e819d450d10c448dc6ee142e8.
Later identity work is described in [mobile identity implementation](mobile-identity-implementation.md).

Implementation was authored in an isolated checkout of the GitHub rewrite branch
starting at a29e3560bd4d5d2af4004d143235ec3f832ee85e. Existing local application
working files were not treated as production. Source contracts were inspected on
GitHub at 5111f06af5d8b17f855ceed9637caca02db117e2; during the transient network
outage, navigation/scope evidence was also read from the immutable cached Git
object with that exact remotely-verified commit ID.

## Checks observed

| Check | Observed result |
|---|---|
| .NET SDK | Official SDK 10.0.402 archive downloaded to isolated tooling; Microsoft release-manifest SHA512 verified before extraction. |
| API Release build | Passed, zero warnings and errors. Strict NuGet restore remained enabled. |
| Policy acceptance executable | 26/26 passed on .NET 10: device expiry, role consistency/revocation/fan recovery, OTP expiry/attempt/consume state and organization boundaries. |
| Node navigation/client tests | 16/16 passed on Node 24.22.1. |
| Svelte check | Zero errors and warnings. |
| SvelteKit production build | Passed on Kit 3.1.0, adapter-node 6, Vite 8.3.5 and TypeScript 6.0.3. The local tool environment prints a benign NO_COLOR/FORCE_COLOR conflict notice. |
| API runtime smoke/export | Liveness 200; unavailable loopback database 503; typed OpenAPI exported. Export also passed with ambient DOTNET_ENVIRONMENT=Production and a conflicting DOTNET_URLS, confirming the child uses controlled local settings. |
| Production web package | Started and returned 200 using build, package.json and an offline npm ci of production-only dependencies. Disposable runtime directory removed afterward. |
| NuGet advisory gate | Initially failed on vulnerable Microsoft.OpenApi 2.0.0. Updated to patched 2.13.0 plus ASP.NET OpenAPI 10.0.13 and Npgsql provider 10.0.3; final restore/build passed without suppressing advisories. |
| npm audit | Reports zero vulnerabilities after the stable SvelteKit 3 migration; the temporary cookie override was removed. API generation uses an isolated TypeScript 5 tooling package to respect its upstream peer requirement. |
| Git whitespace | git diff --check passed before publication. |

## Limits

- No production data, credentials, providers, VPS or traffic were changed.
- Identity/OTP persistence, JWT verification, phone delivery, ownership relations
  and authenticated mobile screens are not yet implemented by this policy slice.
- Policy tests do not prove concurrent database OTP consumption; that requires a
  transaction/row-lock integration test when persistence is added.
- Docker is not installed on this host. The workflow includes an isolated Compose
  startup/health check, but no local container pass is claimed.
- Prior GitHub Actions runs failed before receiving a runner or executing steps;
  local builds provide the observed implementation results above.
