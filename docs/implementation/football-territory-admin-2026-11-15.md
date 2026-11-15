# Verified football territorial administration — 15 November 2026

Only in the OS rewrite. Neither the original Lefteria FC repository nor
Hostinger production is a release target.

## Shipped slice

- \`GET /api/admin/football/territories?organization_id=...\` reads **only**
  current session-authorized city, location, group and catalog records.
- \`POST\` to the same route creates an audited resource with \`kind\` and
  \`data\`: Academy \`city\`, \`location\`, \`group_assignment\`,
  \`opponent\`, \`playing_field\`, \`training_base\`, \`match_draft\`;
  First Team \`team_scope\`, \`opponent\`, \`playing_field\`,
  \`training_base\`, \`match_draft\`.
- Academy opponents and match fields belong to one **city** across its
  locations, whereas bases, group membership and match-draft access are
  **location**-specific. First Team catalogs belong to the team scope and
  cannot use Academy records. PostgreSQL composite foreign keys are the
  second layer of isolation even if a caller forges another resource ID.
- \`/admin/territories\` exposes all permitted catalogs with city, location
  and independent team selectors, safe Google Maps links, draft match creation
  and first-team catalog initialization. Inactive cities/sites are not
  writable; they are not silently mapped to a different one.
- The database calls \`identity.lock_admin_club_authority\` and validates a
  fresh session, active account, organization grant, global role and explicit
  city/location/team admin grants. City managers can see their city's
  locations; location managers can see only their site plus the city's shared
  opponent/field choices; site grants cannot update shared city catalogs.
  Super admins with an organization grant can see/manage the whole
  organization. Read and write permissions are decided in PostgreSQL, not by
  frontend filters. Manager grant rows are locked on writes to prevent
  concurrent permission revocation races.
- \`football-territory-admin-grants.sql\` must be approved and applied by the
  database operator on the isolated target database **after EF migrations**,
  using the existing independently credentialed web-admin and mobile roles.
  Only two narrowly scoped functions receive EXECUTE. Neither role receives
  direct territorial table permissions; the helper functions remain private.
- Rollback-only PostgreSQL tests create disposable accounts, sessions, team,
  cities and locations, validate same-city sharing and sibling-site denial,
  verify cross-organization isolation, revoked-session denial, and a
  valid location-bound match draft. CI also runs frontend isolation checks.

## Not yet released

Match drafts are private only; publishing fixtures, results, attendance,
payments and staff assignment to this normalized location hierarchy require
separate reconciliation/migration and authorization work. Existing invoices,
Payabl flows, mobile roster permissions and published fixtures are not
rewritten by this slice. Existing historical groups without exact geographic
assignments remain unassigned; no inferred location is generated.

The manager's location scope must be provisioned explicitly by an authorized
database administrator or a future delegated-grant review workflow; the
screen does not auto-grant access based on a legacy location label.
Live Hostinger production remains untouched. CI is not proof of staging
authentication, original-design pixel parity or production deployment.
