# Mobile identity HTTP/PostgreSQL integration checks

This console executable hosts the real ASP.NET Core application with
Microsoft.AspNetCore.Mvc.Testing 10.0.13, matching the API's Microsoft package line.
It uses the real PostgreSQL repository, shared throttle and JWT middleware. The
only provider replacement is an in-memory ISmsSender registered by test DI; the
product has no simulated login switch. Captured OTPs/tokens are never printed.

Run after applying the migration to the isolated fixture and creating its
restricted application role:

    dotnet run --project backend/tests/LefteriaFc.Identity.IntegrationTests --configuration Release -- --fixture <path-to-identity-db.json>

Alternatively set LEFTERIA_TEST_FIXTURE to the credential document path and run
the project without executable arguments; this is the CI entry point.

The fixture document must explicitly say disposable=true, purpose='Lefteria
rewrite identity integration tests only', host=127.0.0.1, port=54329 and
database=lefteria_rewrite_identity_tests. It requires connectionString/username
for the fixture owner and applicationConnectionString/applicationUsername for a
different restricted application role. The fixture owner must be lefteria_tests.
Both parsed connections and the actual connected server identity are validated
before any SQL mutation. A mismatched document is rejected with exit code 2.
Do not commit this credential document.

The suite never migrates, drops or truncates a database. It generates distinct
test phones/identities, uses only exact owned-row cleanup, and leaves security
audit events append-only. TEST-NET remote IP customization exists only in the
test host, so product forwarded-header trust and shared throttles remain real.

Checks cover HTTP contracts, role picker/no-token state, BCrypt-independent phone
OTP proof, atomic consumption, invalid roles, expiry/attempts, exact device expiry,
revoked formal-role fan recovery, staged fan profile writes, organization grant
checks and durable sole context, PostgreSQL FKs/checks/uniqueness, restricted-role
permissions, concurrent destination throttling, old-delivery cleanup races and JWT
signature/issuer/audience/type/role/lifetime rejection.
