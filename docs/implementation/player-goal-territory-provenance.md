# Immutable player-goal territory origin (staging foundation)

This migration captures the **verified source territory at goal creation time**, not
the player's current location when somebody later opens the goal. It is a
prerequisite for precise location- and coach-scoped private development access.

- First Team: one canonical team operational scope; no Academy city/location.
- Academy: includes the canonical membership table **and** the optional legacy
  primary group. Every membership must be mapped to the **same active location**;
  every city and operational scope must agree. Where one group is verified, its
  ID is captured; when several groups share a site, the location is captured
  but the group stays null.
- Unassigned groups, multiple cities/locations, inactive/unmapped sites, missing
  team scopes and goals that existed before this migration stay **unscoped**.
  We never infer a historical assignment from current rosters.
- A child's subsequent transfer, group change, or season change cannot mutate
  the origin. A PostgreSQL BEFORE UPDATE trigger enforces immutability on every
  goal update, including progress updates. Foreign keys enforce exact geography.
- The existing append-only goal events and idempotent creation contract are
  unchanged. All new fields are private columns on the existing canonical goal.
- No new API route, broad table grants, role permissions, manager/coach reads or
  guardian views are added here. Current goal access remains Super Admin-only.

## Required next gate

Before exposing site-scoped goals, add database-verified actor scope checks
plus explicit goal-specific visibility approvals; restrict current membership,
original site, assigned coach/group and historical notes independently. The
default for older or ambiguous goals is always **deny**. Test consent, transfer,
revocation, cross-site and multi-group edge cases with synthetic data.

Verification runs in disposable CI PostgreSQL with rollback; this migration is
**not deployed** to live Hostinger production.
