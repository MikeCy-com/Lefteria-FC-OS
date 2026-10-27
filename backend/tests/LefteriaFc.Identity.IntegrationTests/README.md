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

The suite never creates, drops or truncates a database. Its account scenario
attempts the guarded account rollback and verifies populated data refuses it
before any schema change. It generates distinct
test phones/identities, uses only exact owned-row cleanup, and leaves security
audit events append-only. TEST-NET remote IP customization exists only in the
test host, so product forwarded-header trust and shared throttles remain real.

Checks cover HTTP contracts, role picker/no-token state, phone OTP proof, atomic
consumption, invalid roles, expiry/attempts, exact device expiry,
revoked formal-role fan recovery, staged fan profile writes, organization grant
checks and durable sole context, PostgreSQL FKs/checks/uniqueness, restricted-role
permissions, concurrent destination throttling, old-delivery cleanup races and JWT
signature/issuer/audience/type/role/lifetime rejection.
Own-profile preference coverage validates favorite_team's allowed values and
explicit null, rejects missing/invalid input, ignores untrusted identifiers and
unknown fields, verifies current signed identity/active status, checks separate
same-phone role preferences, and confirms the security audit insert.

Avatar checks run the real native codec and PostgreSQL adapter for malformed,
MIME-spoofed, oversized, truncated and excessive-dimension inputs, metadata/EXIF
and alpha handling, public GET/HEAD and replacement, independent roles, and
fresh identity/admin revocation after decode. A randomly named temporary
failure trigger affects only its generated test identity and is removed in
finally, proving atomic rollback. The optional post-decode gate is test DI only.
No external images/providers or raw-upload persistence are used. Avatar FK
cascade permits the existing exact-owned cleanup.

The account scenario runs first against empty account/roster/staff targets. It
imports generated source-shaped records, checks owner-only review/apply/replay,
same-count mutation refusal, quarantine, atomic failure and rollback protection,
then exercises current player/guardian/coach/admin links, grant revocation with
historical evidence retained and pre-OTP fan evidence. It cleans only exact owned
IDs and generated fictional phones before the existing native identity cases.
