# Exact territorial authorization for app-only announcements

This migration closes the previous gap between the new Football Territory
delegation layer and the legacy web-admin announcement APIs.

## Enforced boundary

A manager may create, read, add/remove private draft media, publish,
list published notices, view named readers or withdraw an announcement ONLY
when their currently verified organization, account role, active group and
`football_operational_scopes`/Academy city+location assignment match a
currently granted **team_manager**, **city_manager** or **location_manager**
role. Super Admin sees all **canonically mapped** groups; unassigned groups
fail closed until explicitly assigned to a city/location or First Team scope.
Inactive Academy cities or locations fail closed as well.

The enforcement is in eight PostgreSQL SECURITY DEFINER functions, not just
the frontend or the tenant-level WebAdmin policy. The shared
`club.admin_app_notice_group_allowed` helper is not granted to runtime roles,
never enumerates recipients and reuses the locked current session plus
`club.admin_football_allowed(...,true)`. Private attachments, notice
reader names and withdrawn notice details are never exposed by cross-location
queries. An incoming URL containing someone else's group name does not
confer authority.

## Verification and deliberate limits

The isolated announcement HTTP fixture provides a real team with a grant,
a sibling squad without one and two Academy locations in the **same city**,
with an explicit grant only for the second site. It checks failed cross-site
writes, empty filtered lists for unrelated managers, and live denial after
revocation. Existing publication, audit rollback, version checks, opt-in push
and mobile canonical membership checks are preserved.

**Remaining follow-on:** the older generic football audience-directory
endpoint still needs tenant-wide manager visibility reduction so managers
cannot enumerate unrelated group names/counts through that unrelated reader;
use a territory-authorized selector before claiming complete directory
privacy. Until that is closed, no new manual push broadcast, retry or private
chat moderation is enabled. This migration does not deploy anywhere, send
messages or alter public website content.
