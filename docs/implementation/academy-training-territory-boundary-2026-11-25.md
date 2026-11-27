# Academy training and attendance territory enforcement — 25 November 2026

Stacked on draft #478 (6752e3cab807ea48fcaa6159687e73f7f4a73251), itself dependent on #477 and #476. The shared planner changes overlap #475. No main/staging/production changes.

## Security findings and implementation

- Eight existing native training operations authorized manager/super-admin organization membership but did not uniformly require the exact Academy location. Migration 20261125093000 replaces those functions in place with current session/grant plus active group/city/location checks for draft list, create/edit/archive, approval list/approval, publication preview/publish/list and cancel. ID-addressed operations resolve group, lock authority, then lock the exact plan. Full drill review's own group check remains in place. Unknown plan IDs and missing/inactive assignments fail closed. Even Super Admin must reconcile unmapped historical groups before training operations; this is a deliberate security-first rollout decision.
- Corrected the previous complete-review summary trim expression (two literal backslashes in SQL E-string could treat n as a trim character rather than LF). Use explicit chr(10) instead.
- Three existing Academy attendance operations (including imported sessions) also had organization-only manager authorization. Migration 20261125094000 requires the selected active group and all explicit host group territories for a roster read/write, filters inaccessible multi-site events out of session lists, and rejects unscoped/non-group events. This is deliberately conservative: a manager who controls only one of a shared session's sites cannot see all children in the combined attendance roster. Explicitly invited guests remain eligible under the existing event-specific guest policy; no general cross-group roster access is introduced. No new public/mobile grants. Existing RSVP and attendance version history untouched.
- New private event authorization helper is denied to both admin/mobile runtimes; existing public-facing API contracts are unchanged. Security migrations prohibit destructive downgrade; audited forward recovery is required.

## Tracker and evidence

| ID | Priority | Status | Evidence / acceptance |
| --- | --- | --- | --- |
| TRAINING-TERRITORY-09 | P0 | Coded; source checks pass | 2b538e3: eight native plan operations; 60072d3: rollback-only two-city, sibling-site, unassigned, inactive and revoked-grant fixtures. |
| ATTENDANCE-TERRITORY-10 | P0 | Coded; source checks pass | 0660d3d: three existing attendance operations and exact host group helper; c8879e3: private grants; e5f782c: sibling/multi-site/inactive probes. |
| REVIEW-TEXT-11 | P1 | Coded; SQL not run | 4c1516b: remove escaped character-set ambiguity. |
| COACH-WORKSPACE-12 | P1 | Pending implementation | Confirmed canonical web-admin link identity.admin_staff_links -> active club.staff, memberships in club.staff_academy_groups and club.academy_group_coaches. Native coach registration writes the link. Current coach role already has training:read_assigned and training:write_assigned; it MUST NOT acquire training:write. Next use separate draft authority, exact current group membership and active site, lock staff link and membership rows, never broaden manager-only approval/publish/cancel. Dedicated assigned-group picker (no organization-wide roster) and draft-only controls needed. |
| RELEASE-13 | P0 | Blocked | Runtime database/permission checks, CI, compiled API/frontend and staging acceptance must pass before release. |

Four source assertion test bodies from 0793449 passed in isolated JavaScript harness against reread committed files, no TypeScript stripping or Svelte calls required in this test. This is not PostgreSQL execution. Existing disposable PostgreSQL lifecycle scripts extended rather than adding unwired scripts; not executed here. Full regression suites and authenticated UI/API checks remain outstanding. No CI manually rerun. No deployment. New migrations fail closed on unassigned groups and shared multi-site attendance; validate actual legacy group mappings in isolated staging before enabling. All prior stacked draft dependencies remain unmerged.
