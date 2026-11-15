# Football operations: Academy city versus location scope

This is OS-only development. No original repo or Hostinger production updates.

## Source-of-truth ownership

| Entity | First Team | Academy |
|---|---|---|
| Opponent directory | Club team | CITY, shared across all locations in that city |
| Playing-field directory (name, Google Maps link, coordinates) | Club team | CITY, shared across all locations in that city |
| Training base | Club team | LOCATION |
| Youth group, players, staff, coach | Club team | LOCATION |
| Match draft | Club team | Group at LOCATION; city-level opponents and fields |
| City manager | N/A | Only assigned city and its locations |
| Location manager | N/A | Only assigned location, except shared city opponents/fields |
| Super admin | Authorized organization-wide | All Academy cities and locations |
| Payments and private statistics | Club scope | LOCATION; sum by authorized city; all only for super admin |
| Website | Club team | CITY → LOCATION → published groups |

Academy locations within the same city deliberately see the SAME saved opponent and playing-field lists when creating games. A location cannot see the other location's groups, staff, invoices, training bases, attendance or statistics just because they share the catalog. Different cities never share opponents/fields. The Club/First Team uses separate team-owned catalogs.

## Implemented in the safe initial migration

- Normalized Academy city and location names, stable slugs, Google Maps links and optional coordinates.
- One operational catalog scope per First Team squad OR Academy CITY. Academy opponent and playing-field tables link to this scope, not an individual location.
- Canonical Academy group-to-LOCATION assignment; composite DB keys ensure its location belongs to the city scope.
- Academy training bases are LOCATION-owned; they may reference the shared city playing field. Club bases are team-owned.
- Private draft matches require matching city-level catalogs and the exact group/location pair. No publishing, RSVP, notifications or calendar changes.
- Separate manager city grants, location manager/coach/staff grants and team manager grants are stored, but no unreviewed runtime gets direct table access.
- RLS enabled and PUBLIC access revoked on each table. Session-bound privileged APIs and grants are still required.
- Disposable PostgreSQL checks verify two Academy locations sharing one city's catalog, another city not sharing it, no cross-location roster assignments, and Club/Academy separation. Everything is rolled back.
- Frontend picker helper follows the same boundaries but is NOT a security enforcement substitute.

## Outstanding implementation

1. Create/edit/archive and list APIs with verified city manager, location manager and super-admin scope and auditable actions. Current organization manager role alone must not grant access to all cities.
2. Admin match editor: Club team → team opponent/field; Academy city → location → group → city-wide opponent/field. Server must reject wrong IDs in the same transaction.
3. Admin training planner: location-specific base dropdown, coach assignment and facility/time conflict checks.
4. Location-specific payment attribution, invoices and private operational statistics; authorized aggregation for city manager, full rollup for super admin only.
5. Public website city → location → group navigation, SEO, canonical slugs, maps, active/published visibility and verification of legacy group assignments (no guessing from free-text).
6. Data migration review, authenticated role tests and source screenshot comparisons at matched viewports.

Nothing in this migration should be deployed to the live site or treated as a complete administrative or financial feature.
