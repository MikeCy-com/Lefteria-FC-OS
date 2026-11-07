# Mobile API response caching policy

All URL paths within `/api/mobile` are **private, non-cacheable**. The ASP.NET Core request pipeline sets these headers at response start:

```http
Cache-Control: private, no-store
Pragma: no-cache
```

The rule applies to successful account and role projections, OTP/session operations, family invitations and child delegations, member calendars, RSVP results, and all error responses (401, 403, 404, 429, 503). It protects responses in browser and intermediary caches even when an endpoint forgets to set its own caching policy. Read-only delegated child calendar responses already set matching headers directly.

The path segment rule excludes public club content, public avatar asset URLs, and health checks, so it does not accidentally disable normal caching for public static resources.

**This is defense in depth, not permission authorization.** Each sensitive endpoint must still revalidate its identity, canonical roster relationships and grant in the final database query; revocation cannot delete already viewed information from a user's memory or screenshots. Client logout and role switching must continue to erase cached application state.

Integration tests check the policy for both a successful authenticated `/api/mobile/auth/me` response and a forbidden `/api/mobile/calendar` response.
