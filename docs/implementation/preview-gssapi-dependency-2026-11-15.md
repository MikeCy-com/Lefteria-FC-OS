# Preview: Npgsql 10 missing native GSSAPI library

## Diagnosis

The OS uses `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` and
`mcr.microsoft.com/dotnet/aspnet:10.0`/`runtime:10.0` Docker images.
Npgsql 10 probes Kerberos GSSAPI for PostgreSQL connection negotiation.
Modern .NET container images may omit the native
`libgssapi_krb5.so.2`, resulting in a warning or a connection-startup
error. This is a **native image dependency**, not a project asset and not
evidence by itself of a Hostinger infrastructure fault.

Reference:
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/krb5-libs-package
- https://github.com/npgsql/npgsql/issues/6360

## Fix

The multi-stage backend Dockerfile now installs Debian
`libgssapi-krb5-2` in BOTH the API/EF migration image and the roster
import utility runtime. Greek invoice fonts remain installed. The client
GSSAPI configuration is not disabled or silently downgraded.

The Docker Compose CI job builds the API and roster-import targets and
checks `libgssapi_krb5.so.2` is present in each image before allowing
merge. No service configuration, production data, live payments or
Hostinger production deployment changes.

## Staging-only verification after an approved release

Build a newly tagged preview API/import image from the exact tested
OS commit. Verify the resulting file in both images and run a
PostgreSQL connectivity/migration smoke against an isolated disposable
database. Only after a successful preview test should the approved
image tag be supplied to the **preview** compose environment.

`deploy/preview/compose.yaml` uses explicit prebuilt image tags,
`pull_policy: never` and a separate database; **do not** run
`docker compose up` on the shared production compose or reuse
production credentials. A Hostinger-side update is neither attempted
nor verified by this repository-only change.
