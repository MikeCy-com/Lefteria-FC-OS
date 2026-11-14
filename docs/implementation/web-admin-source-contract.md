# Web administrator authentication source contract

Source inspected through the GitHub connector on 2026-10-31: original repository
`MikeCy-com/Lefteria-FC` main
`380b4022f0ab0db95fbc52a8613711160a5037a5` (PR46). This records repository
behavior; it does not assert the deployed production revision. The existing
production application, accounts and domain remain unchanged.

## Original authentication and callers

`POST /api/auth/login` accepts JSON `username`, `password`, and optional
`remember_days`. Username lookup is exact and case-sensitive. Password helpers
encode the original password as UTF-8 and use `bcrypt.hashpw/checkpw`;
`requirements.txt` pins bcrypt4.1.3. No password trimming, case folding,
Unicode normalization, pre-hashing or ASP.NET Identity hash conversion occurs.
The source library truncates bcrypt inputs at72 **bytes**, not characters.
Imported supported2a/2b/2y hashes must retain cross-language compatibility.

Missing or null remember choice means24 hours; only30/60/90 integer days are
accepted. Success returns `id`, `username`, `token`, `otp_required=false`, and
sets `access_token`: Secure, HttpOnly, SameSite=Lax, Path=/, with the same bounded
Max-Age. The JWT is HS256, `type=access`, with `sub` equal to the admin ID,
`username`, `role`, `exp`, `iat`, `jti`. The source login factory defaults the JWT
role to `admin`; actual authority comes from the current database account, not
that claim. The authentication dependency reloads the account, rejects disabled
accounts and removes the password hash before returning it.

For `otp_2fa_enabled=true`, password proof creates a five-minute SMS challenge
instead of a session. `POST /api/auth/verify-otp` takes `challenge_id`, `code`,
`remember_days`, allows five failed attempts, and rechecks account availability.
The rewrite must not issue a password-only session for those accounts while
provider delivery is paused. It does not import or execute old challenges or
invitation tokens.

`GET /api/auth/me` returns the authenticated account. `POST /api/auth/logout`
only clears the cookie: a copied source JWT remains valid until expiration.
`PUT /api/auth/me` updates self-profile; `POST /api/auth/change-password`
requires the current password and a new password of at least eight characters.
Administrative create/invite/reset endpoints are separate; there is no public
administrator signup. Account creation and role/organization changes require
super-admin authority, with a guard preserving the last active super-admin.

The original React AuthProvider stores the returned token in
`localStorage["token"]`, fetches `/auth/me`, refreshes on window focus, and sends
Bearer headers. The admin panel separately loads `/admin/organization-context`
and filters menus and deep links by role and trusted organizations. The rewrite
uses its own cookie session; neither mobile tokens nor copied legacy web JWTs
grant this new web-admin session.

Sources: [models](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/models.py#L114),
[login, 2FA, logout and self-service](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/server.py#L419),
[hashing and authenticated lookup](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/auth.py#L29),
[session durations](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/session_policy.py#L6),
[React caller](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/frontend/src/App.js#L80),
[Python bcrypt truncation](https://github.com/pyca/bcrypt/blob/4.1.3/src/_bcrypt/src/lib.rs#L67).

## Exact canonical role capabilities

`backend/routes/admin_users.py` is the canonical source. A permission is matched
exactly, by `resource:*`, or by `*:*`. Missing/unknown roles grant nothing. Stored
public staff job titles and mobile roles do not grant CMS capabilities. No
per-user custom permission list participates in this source check.

| Role | Exact permission set |
| --- | --- |
| `super_admin` | `*:*` |
| `manager` | `teams:*`, `academy:*`, `fixtures:*`, `results:*`, `live_score:*`, `match_reports:*`, `events:*`, `attendance:*`, `news:*`, `sponsors:*`, `gallery:*`, `messages:*`, `wall:*`, `finance:*`, `shop:*`, `facilities:*`, `training:*`, `marketing:*`, `staff:read`, `staff:write`, `staff:archive`, `staff:compliance`, `settings:read`, `settings:write` |
| `editor` | `news:*`, `sponsors:*`, `gallery:*`, `marketing:read`, `teams:read`, `academy:read`, `fixtures:read` |
| `marketing` | `social:write` |
| `coach` | `teams:read_assigned`, `academy:read_assigned`, `training:read_assigned`, `training:write_assigned`, `attendance:read_assigned`, `attendance:write_assigned`, `messages:write_assigned`, `fixtures:read` |
| `matchday_editor` | `live_score:*`, `match_reports:*`, `fixtures:read`, `teams:read` |

The manager's broad resource wildcards are preserved as written; a comment
about permanent deletion is not a substitute for endpoint-specific checks.
In particular `staff:delete` and all `users:*` authority are absent. A coach's
`read_assigned` never implies organization-wide `read`.
[Canonical matrix and matcher](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/routes/admin_users.py#L45).

## Organization and assignment boundary

Current source PR46 distinguishes web access from mobile management. A web
administrator can read the union of current assigned organizations, independent
of mobile `active_organization_id`. Each write must identify one authorized
owner; a union is never stored as the record owner. Known IDs are `lefteria_fc`
and `lefteria_fc_academy`. Client headers, request bodies and cached menus do not
grant membership.

Explicit plural assignment is authoritative, including an empty list and for
super-admins. Singular assignment is the legacy fallback. Only a legacy
super-admin with both fields absent receives both organizations. A coach with
both fields absent is backfilled from assigned academy group before assigned
team. Unknown IDs grant nothing. Current grants must be checked after login,
not copied into a durable session as permanent authority.

The rewrite already imports active/nonpending status,2FA state, credential hash,
role and canonical organization grants. Historical `migration.imported_admin_grants`
is provenance; `identity.admin_organization_grants` is current authority. Exact
web admin team/group assignments are not yet typed operational grants. The login
slice must not convert organization membership into coach access to every team.
[Organization rules](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/organization_scope.py#L38),
[web union and single-owner writes](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/backend/organization_scope.py#L145),
[navigation](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/frontend/src/pages/admin/adminNavigation.js#L53).

## Bounded rewrite interface

The first interface is `POST /api/admin/auth/login`, `GET /api/admin/auth/me`,
`POST /api/admin/auth/logout`. Login accepts the original username/password and
null/30/60/90 remember choice. Login and me return `WebAdminSessionDto`:
`{user:{id,username,name,role,permissions,organizations:[{id,name}]},expires_at}`.
Logout returns204. Errors use `{detail,code}`; inactive/unknown/invalid credentials
are uniform401, while2FA requires503 `two_factor_unavailable` until implemented.

The session uses only the opaque256-bit HttpOnly/Secure/SameSite=Lax
`__Host-lefteria-admin` cookie. The client never receives, reads or saves a
session token. Browser requests use `credentials: same-origin` and no Bearer
header. Mutations require strict Origin equality to the configured public origin,
`X-Lefteria-Admin: 1`, and `Content-Type: application/json`; no CORS or simple
content-type mutation path is enabled. The browser supplies Origin; application
code does not invent it. These JSON API restrictions must be tested on the server
for login and logout as well as future writes. See the
[OWASP custom-header guidance](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html#employing-custom-request-headers-for-ajaxapi).

Logout revocation and password/availability checks are server responsibilities;
revoking a copied session is a deliberate improvement over the source logout.
The existing mobile database role must remain unable to select credential hashes.
CMS authentication needs a separately restricted access path, not the owner
connection, migration credentials or expanded mobile grants.

The Svelte client discards all private state on a definitive401, local absolute
expiry and logout. Network/unavailable errors hide private data and permit retry;
they do not claim server logout succeeded. Pending reads cannot reauthenticate
after logout. Focus refresh reads current server permissions. There is no signup,
invitation acceptance, credential reset or business-module write in this slice.
Unported modules are explicitly labelled **Under Developments**, without fake
statistics or enabled placeholder actions.

Public content remains independent of CMS sessions. Original public callers
omit cookies. The [public website slice](public-content.md) supplies imported
news, club, team, Academy and fixture reads. The [club profile editor](club-profile-editor.md)
updates public settings for existing profiles using settings capabilities and
organization grants.
[Public fetch boundary](https://github.com/MikeCy-com/Lefteria-FC/blob/380b4022f0ab0db95fbc52a8613711160a5037a5/frontend/src/club-site/publicApi.js#L1).
