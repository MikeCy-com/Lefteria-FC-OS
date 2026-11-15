# Private app communications control centre — 2026-11-15

This change introduces **Admin → Εφαρμογή μελών → Επικοινωνία εφαρμογής**
(`/admin/communications`) for the member app only. It does not change
public CMS, website news, CRM, original Lefteria-FC or production Hostinger.

## Verified controls and deliberately unavailable actions

| Area | In this change | Existing implementation and boundary |
| --- | --- | --- |
| Announcements | Direct navigation to the real editor; owner-scoped counts of saved drafts and published notices. | The existing app-only draft, publish, withdraw, readership and protected media API still owns every mutation. The new page provides no alternate writer. |
| Messages | Explain the member app's current group and consent-based adult private chat. | No web-admin transcript browse, moderation action or impersonation. Private messaging forbids player/fan identities. Group reactions/readers are not equivalent to private-chat reactions/readers. |
| Notifications | Distinguish the member inbox and opt-in Web Push infrastructure from administrative dispatch. | No new manual sends, delivery retry UI, device identifiers or provider receipts; provider enablement remains gated. |
| Attachments | Direct navigation to existing private announcement media controls. | One sanitized JPEG banner and up to three PDF files per draft, fetched only after a fresh member authorization. No general message media library or public objects. |

## Permission and privacy checks

The route requires an authenticated *web-admin* session. The centre appears
only for Manager/Super Admin with `teams:write` and First Team organization
grant, or `academy:write` and Academy organization grant. The client gate is
not authoritative; the existing announcement endpoints repeat those grants
in the API and SQL. Overview fetches are `same-origin`, `no-store`,
cancellable and scoped to the current organization; cross-organization,
malformed or stale responses are not displayed. No private message text,
member identifiers, read-receipt names or token/key material is fetched.

Academy city/location and group targeting must continue to derive from live,
verified group assignments; this page does not introduce a cross-location
broadcast or broaden inherited manager permissions. Future notification and
admin moderation APIs must independently verify organization, city, location,
current group membership and permission before any action, with logged
moderation reason and audit events.

## Remaining implementation work

1. Build an explicitly permissioned *metadata-only* administrative message
   moderation queue. An administrator must not gain access to unrelated
   family conversations simply by having an admin cookie. Provide an audited,
   purpose-limited procedure if moderation access is authorized by policy.
2. Add app message media in quarantine with content signatures, size caps,
   current conversation binding, auth-on-every-download and malware scanning.
3. Add an organization/group-scoped notification outbox management API and
   operator UI with retries, deduplication, dead-letter handling, opt-out
   checks and non-delivery status; verify real provider receipts on test devices.
4. Add targeted role and location privacy tests, mobile/browser E2E, plus
   isolated staging verification before enabling any new send operation.

No production deployment is part of this change. Automated tests and full CI
on the exact PR SHA are prerequisites before merge.
