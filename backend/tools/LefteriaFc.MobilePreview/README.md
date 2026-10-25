# Local authenticated mobile preview

This **test-only** executable provides browser access to the real mobile API pipeline against the disposable PostgreSQL identity fixture. It is not a deployment component and must never receive production database credentials. It does not migrate a database or call Twilio.

The fixture JSON must be supplied through `LEFTERIA_TEST_FIXTURE`. The linked integration-test guard requires the exact disposable purpose, database `lefteria_rewrite_identity_tests`, server `127.0.0.1:54329`, owner `lefteria_tests`, and a separate restricted application role. Both actual connections are checked against live server metadata before seeding. The existing identity migrations and application-role grants must already be applied.

Start the built production SvelteKit Node server separately on `127.0.0.1:4188`. For example, from `frontend` after its normal build:

```powershell
$env:HOST = '127.0.0.1'
$env:PORT = '4188'
$env:ORIGIN = 'http://127.0.0.1:4190'
node build
```

Then, from the repository root with the .NET 10 SDK available:

```powershell
$env:LEFTERIA_TEST_FIXTURE = 'C:\path\outside-repository\identity-db.json'
dotnet restore backend/tools/LefteriaFc.MobilePreview --locked-mode
dotnet run --project backend/tools/LefteriaFc.MobilePreview -c Release --no-launch-profile -- --port 4190 --web-origin http://127.0.0.1:4188
```

Open the printed `http://127.0.0.1:4190/app/login` URL. Use the printed fictional `+999...` phone and code **123456**. The fixed code exists only in this executable's test DI; challenge digests still use the real HMAC implementation with a new random key per process. The fake SMS sender accepts only that generated phone. Product code has no preview authentication flag.

Five role identities share the fictional phone. The management identity has an explicit admin relationship and grants for both club organizations. The fan identity has fictional editable names, birthday, email, and boolean preferences. No players, children, teams, statistics, financial records, or other operational data are seeded. These role labels verify authentication and navigation only.

All API traffic uses the real API TestServer and the restricted PostgreSQL application connection. Only exact fixture seeding and cleanup use the owner connection. Random signing and OTP keys stay in memory; tokens and request/response headers are never printed. The outer Kestrel listener binds only `127.0.0.1`. `/api/*` goes only to TestServer; other paths go only to the validated HTTP `127.0.0.1` Node origin. There is no arbitrary upstream URL or automatic redirect following. Bearer authorization is forwarded only to the API; cookie and hop-by-hop headers are stripped. Request bodies are streamed with a 32 MiB Kestrel limit; the API's stricter mobile-body limits still apply where supported by its host.

Both hosts discard inherited application/environment configuration providers. The API receives only fixture settings; the proxy receives only its explicit loopback listener. Ambient `Kestrel:Endpoints` and `ASPNETCORE_URLS` cannot add public listeners. Startup checks the actual server-address feature for exactly the requested `http://127.0.0.1:<port>` listener before printing any fixture URL or phone.

Stop gracefully with Ctrl+C or:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:4190/__fixture__/stop
```

The stop endpoint requires loopback, the exact local Host, and a matching browser Origin when supplied. Shutdown deletes only this run's generated identities, admin assignments, OTP rows, and rate-limit keys. Security audit events remain append-only in the disposable fixture. A forcibly killed process cannot run cleanup; use a disposable fixture and inspect it before reuse after an abnormal exit.

The HTTP smoke mode needs PostgreSQL but does not need the Node server:

```powershell
dotnet run --project backend/tools/LefteriaFc.MobilePreview -c Release --no-launch-profile -- --smoke --port 4191
```

It checks readiness, anonymous rejection, real OTP issue and role picker, login, own-profile writes, role switching with unchanged expiration, management organization selection, local-host protection, cross-origin stop rejection, and graceful shutdown/cleanup. No live SMS is sent.
