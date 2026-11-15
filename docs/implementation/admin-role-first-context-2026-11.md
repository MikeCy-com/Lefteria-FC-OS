# Lefteria FC OS: role-first administration (2026-11-15)

## Accepted product behavior

The admin application is **not** divided by a permanent global Club/Academy
toggle. The authenticated role, issued capabilities, organization grants and
assigned team/group permissions define the workspace.

| Person | Intended access |
|---|---|
| Super Admin | All modules and all organizations explicitly granted to the owner. The founding account must have Club and Academy grants. |
| Academy Manager | Academy business, football, staff and finance functions according to assigned capabilities; cannot see First Team-only records without a Club grant. |
| Marketing | News/editorial, sponsors, media, social and campaigns only in granted organizations. Private CRM enquiries require a separately approved consent/role policy. |
| Coach | Assignment-scoped sessions and teams only, never organization-wide roster, finance or CRM. The dedicated assigned-group UI is still pending. |
| Accountant | Finance read/write only; no Club roster, Academy child documents, messages, staff or user management. The current financial APIs remain sandbox-only wherever identified. |
| Editor and matchday staff | Existing narrower source-backed capabilities preserved. |

The navigation is merely a presentation of current role capabilities, never
a security boundary. Server APIs must re-read the session and verify the
database-backed role and organization grants for each request. Coaches must
also be checked for the specific assigned team/group, not just an organization.

## Content destination is an action property, not session context

When a content administrator has multiple organization grants, the News CMS
offers an explicit **content destination** (Club or Academy). This is scoped
only to listing/editing/saving articles and private drafts, without changing
other admin pages. The Club Profile editor likewise selects which *public
profile* is being edited when both are granted. Each save continues to send
the exact target organization to a protected API.

Unpublished drafts remain in the private `news_drafts` table. There is **no
new publication API in this increment**, and an editor cannot publish to two
organizations simply by toggling the UI. Publication to Club, Academy or
both will require a separate atomic, audited approval flow, with each target
checked against the publishing account's fresh grants.

## Implementation scope and remaining work

- Shared admin header selector removed; localStorage preference no longer
  controls page availability.
- Admin navigation returns all permitted modules across granted organizations.
  Overview loads separately verified metrics for each authorized owner.
- Academy-specific deep links check Academy grants/capabilities, not a
  browser-selected Club context; First Team trials use the Club grant and
  its existing protected role policy.
- New accountant role with role-constraint migration and explicit finance-only
  capabilities; marketing role can use authorized content tools.
- Some existing content/finance/group pages still consume an exact-owner API
  and default to their first permitted owner. Convert those multi-owner
  workflows to consolidated, separately verified views, without reintroducing
  a global context switch.
- Assigned-coach group discovery, User & Permissions administration, audited
  role assignment, accountant reports, consented marketing CRM and approved
  article publication are **not yet complete**. Never claim these are live.

Before merging: all six repository CI checks including PostgreSQL privilege
audits, migration apply/revert, role tests, API startup and browser viewport
checks must pass. No Hostinger staging or production deploy is included.
