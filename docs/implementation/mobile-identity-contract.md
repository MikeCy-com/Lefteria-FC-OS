# Mobile identity contract and policy checks

Source: GitHub MikeCy-com/Lefteria-FC at
5111f06af5d8b17f855ceed9637caca02db117e2, especially
[mobile_auth.py](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py),
[session_policy.py](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/session_policy.py)
and [organization_scope.py](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/organization_scope.py).

## HTTP contracts to preserve

| Operation | Contract |
|---|---|
| POST /api/mobile/auth/request-otp | phone input; 5-minute challenge; 5 requests/minute/IP; no raw code in production responses or logs. |
| POST /api/mobile/auth/verify-otp | phone, code, optional role/requested_role and remember_days. Multiple roles without a selection return identities and no token; retain the challenge for the role picker. |
| POST /api/mobile/auth/switch-role | Bearer token and explicit role. Resolve current grants afresh for the same verified phone. Same-role is a no-op; switching cannot extend device expiry. |
| GET /api/mobile/auth/me | Return the selected identity and currently available roles. Never choose a different formal role by preference/order. |
| POST /api/mobile/management/organization | Explicit organization_id must be a known, current assignment. One assignment can become active automatically. Multiple assignments require selection before returning operational datasets. |

Mobile JWTs have type mobile_access and contain sub, phone, role, exp, iat and jti.
They use Bearer authentication. Admin/customer/mobile principals remain separate.

Mobile omitted remember_days retains the legacy 90-day lifetime. Explicit null
means 24 hours. Admin omitted or null means 24 hours. Only integer 30, 60 or 90 is
accepted when set; the HTTP boundary must reject booleans, strings and fractions.

## Implemented policy core

- MobileRole accepts only parent, coach, management, player and fan.
- MobileSession requires the current and target identity to remain active and
  share the verified full phone value. A switch retains exact device expiry.
- RecoverFanAccess is a separate path for a removed formal role. It accepts only
  a current same-phone fan grant, retains expiry, and cannot grant a club role.
- OtpChallenge stores only a digest, uses constant-time digest comparison,
  rejects expiry at the exact deadline, counts at most five verification calls
  and supports consumption only after explicit identity selection.
- OrganizationScope returns denied, selection-required or one selected scope.
  Without a selected organization, CanAccess returns false for both datasets.
  Unknown or unassigned organizations never grant access.

## Required integration boundaries

The HTTP layer must validate JWT signatures, issuer/type/expiry and reload grants;
the domain object is not an authentication mechanism. Use cryptographic OTP
generation and a keyed digest adapter. Lock the persisted challenge row while
verifying, incrementing attempts and consuming it. The in-memory policy alone
does not prove atomic concurrent consumption.

Materialize legacy super-admin organization defaults during reviewed import.
Do not invent runtime FC defaults or elevate the unresolved legacy admin role.
Parent/child, coach/team/group and management/admin links require relational
ownership checks before any operational endpoint is exposed.

## Commands

Run the dependency-free policy acceptance executable on the target SDK:

    dotnet run --project backend/tests/LefteriaFc.PolicyChecks --configuration Release

Run the typed navigation tests on Node 24:

    cd frontend
    npm test

These are focused acceptance checks for this core. Database concurrency, HTTP
authentication, provider adapters and end-to-end workflows still need their own
tests when those modules are implemented.
