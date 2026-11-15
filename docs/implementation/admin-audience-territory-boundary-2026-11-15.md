# Admin audience selector: strict territory and youth privacy

The legacy `GET /api/admin/football/audiences?organization_id=...`
returned all First Team or Academy groups and the global count of
unassigned children to every manager with an organization grant. That
violated the newer First Team squad/Academy city/location delegation.

## Updated contract

The existing SQL SECURITY DEFINER reader is still the **only** private
source for that endpoint. Session, exact organization grant, active account
and role are locked and checked by the database. No runtime role is granted
a direct `SELECT` on roster, memberships, private teams or groups.

For a **manager**, each group is filtered by the same canonical
`club.admin_app_notice_group_allowed(actor,organization,group)` helper
used by app-only announcements (#370). That requires:

- The exact assigned FC team with a `team_manager` grant, or
- An active Academy city and canonical group-to-location assignment,
  plus a `city_manager` or exact `location_manager` grant.

A manager of another team, Academy city or sibling location receives no
group identity, name or player count for that target. A manager with only
an organization grant receives an empty group list, *not* a global
fallback. Revocation takes effect with the still-open admin session.

`unassigned_players` is returned as zero for managers; unassigned
children have no verified site owner and their global total should not
be disclosed to delegated personnel. A Super Admin with the exact
organization grant retains the original full roster overview, including
unmapped groups and organization-wide unassigned counts, so the
onboarding/assignment process remains available.

`editor` and `matchday_editor` cannot call this private directory:
there is currently **no** team/location delegation grant model for
these roles. Public content editor privileges do not extend to youth
roster counts. A future explicitly scoped editorial picker should
be introduced separately if that role needs club-group selection.

## Regression coverage

The isolated HTTP/PostgreSQL test seeds two FC squads and two Academy
locations *inside the same city*. It checks First Team manager isolation,
location manager sibling-site isolation, no-grant manager empty
results, city-manager access to both sites, no FC/Academy cross-access,
editor denial, immediate scope grant revocation, existing complete
Super Admin counts and live membership edits. Migrations are applied,
rolled back and reapplied by CI.

No change to mobile read receipts, parent/child identities, push
subscriptions, private messaging or production Hostinger.
