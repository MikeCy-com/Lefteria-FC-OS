# Exact squad and Academy location authorization for existing group profiles

This migration closes the organization-wide manager authorization loophole in
the historical First Team and Academy profile editors and the Academy public
visibility toggle. Newly created Academy groups already require a verified
location (#386); existing group edits must meet the same conditions.

## Database permission boundary

The `club.require_admin_group_profile_scope(digest,owner,group,write)`
helper is private to the migration-owner security-definer call chain. It
rechecks the active admin session, account, exact organization grant and
manager/super-admin role on **every read or write**, before reading profile
fields, modifying a profile or changing the public visibility flag.

- **First Team:** a delegated manager must hold the exact
  `football_team_admin_grants` scope for the target team. A grant for
  another First Team squad cannot read or modify it.
- **Academy:** a group must be canonically assigned to exactly one active
  city and active location. Managers need the matching city-manager or exact
  location-manager grant, checked with `club.admin_football_allowed`
  receiving the **actual location ID**, not a nullable city shortcut.
- **Inactive site:** a previously mapped, inactive location or city cannot
  be read or changed through the delegated profile endpoints. Even Super
  Admin cannot bypass inactivity by treating that mapped group as unassigned.
- **Legacy unmapped groups:** an authenticated Super Admin with the exact
  organization grant retains controlled profile access for reconciliation.
  A manager never receives an unassigned-group fallback.

The six existing functions retain their request/response signatures,
versions and audited write contract:

`read_admin_first_team_profile`,
`write_admin_first_team_profile`,
`read_admin_academy_group_profile`,
`write_admin_academy_group_profile`,
`read_admin_academy_group_publicity`,
`set_admin_academy_group_publicity`.

The reviewed `web-admin-auth-grants.sql` now grants the six exact
profile/publicity SQL functions to the restricted admin login role. Previously
those grants were only in optional operator scripts, causing even authorized
HTTP calls to fail with 403. Both main security allowlists enumerate these
exact functions. The new internal territory helper stays private; **no
direct table privileges** or background notification rights are granted.
Unauthorized calls fail with SQLSTATE 42501, mapped to HTTP 403;
optimistic revision conflicts remain HTTP 409.

## Regression evidence

Disposable PostgreSQL + real HTTP tests verify same-city sibling Academy
locations, different First Team squads, allowed self-squad and self-location
profile reads/writes, publication toggles, stale revision, unscoped manager,
same-session revocation, city-wide Academy access without First Team access,
and inactive-site fail-closed behavior. Fixture profile rows are explicitly
removed before cleaning canonical group and territory dependencies.
CI also verifies migration apply, rollback, reapply, browser and Docker builds.

This does **not** grant managers access to parents' contact details, private
evaluations, child identity records, invoicing or private chats. It does not
publish content automatically. Existing public-only Academy profile content
has a separate publication and historical visibility review to complete
before production.

Do not deploy to live Hostinger.
