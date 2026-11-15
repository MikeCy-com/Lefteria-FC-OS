# Public Academy hierarchy — verified city > location > published group

**Scope:** Public Academy directory only. OS rewrite branch; neither original
Lefteria FC repository nor Hostinger production is changed.

## Behavior implemented

- The `club.read_public_academy_group_directory()` SECURITY DEFINER reader
  now joins the exact canonical group-to-location assignment to the normalized
  active city and active location. It publishes only administrator-approved
  groups (`is_public=true`).
- The public Academy group DTO adds four **allowlisted** values:
  `city_id`, `location_id`, `city_slug`, `location_slug`. Their values
  come only from the canonical database assignment. City and location names
  for assigned groups also come from canonical records, not legacy import text.
- An unassigned but explicitly published group remains accessible under
  **unassigned territory**, with its historical presentation text preserved;
  its historical text does not create a false verified city/location.
- A group assigned to an inactive city or location is suppressed from public
  directory/detail publication rather than silently appearing under another
  location. Its private records remain unchanged.
- The Academy group directory is structured `City -> Location -> Group card`
  with named section headings, accessible city jump links, preserved group URLs,
  coach/training cards, published player previews, philosophy and registration.
- First Team and other organizations are unaffected; no private players,
  payments, attendance, guardian records, permissions or training bases are
  returned by this projection.

## Tests and release boundary

- `frontend/tests/public-academy-territory-directory.test.mjs`: exact-ID
  grouping, same-city/different-location, different-city, missing assignments,
  invalid slugs, First Team separation and accessible section markup.
- `ops/postgres/public-academy-territory-checks.sql`: rolled-back real
  PostgreSQL test for canonical-versus-stale imported location, sharing two
  locations in the same city, separate city, no invented legacy membership,
  public-field allowlist, inactive city/location and unpublished groups.
- CI applies/rolls back/reapplies migrations, then runs this SQL check.
- This does **not** add admin CRUD for assigning cities/locations, location
  finance, statistics, private session grants, or authenticated manager screens.
  Those must follow with server-side authorization before activation.
- No claim of full screenshot pixel parity: reference originals and real
  authenticated staging UI checks are separately required.
