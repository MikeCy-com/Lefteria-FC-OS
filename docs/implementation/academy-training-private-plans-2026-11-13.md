# Academy private training planning — safe first write slice

**13 November 2026 · OS only · no live Hostinger changes**

The original training planner can create and edit sessions. The existing
OS's canonical `club.calendar_items` is an immutable migration projection:
each row has a source-document foreign key. Replacing or directly editing
those records would risk breaking historic attendance, guardian RSVP,
calendar snapshots and audit provenance.

This slice adds **private manager-only session planning**, with explicit
drafts instead of pretending those rows are already published.

## Implemented

- Dedicated `club.academy_training_plan_drafts` storing Academy group,
  local training date/time, duration, title, location, notes, archive status
  and optimistic `version`.
- Manager-only list, create, update and soft-archive operations under
  `/api/admin/academy/training/plans` with exact organization and group
  authorization. Current signed-in manager role, organization grant and
  session are revalidated in SQL under the same transaction.
- Full immutable `club.academy_training_plan_events` records every
  accepted write with actor, version and snapshot, plus security audit.
- Row level security and revoked raw table privileges. Explicit, separate
  `ops/postgres/academy-training-plans-admin-grants.sql` is required for
  the isolated web-admin runtime; public and mobile runtime get **no**
  execute privilege.
- Private schedule editor on `/admin/schedule`, with bounded monthly
  list, strict response validation, version-conflict errors, confirmation
  for archive, and no optimistic publishing.
- GitHub identity CI checks migration apply/down/up, grants and rollback-only
  invalid-owner/invalid-duration/unauthenticated SQL probes.

## Explicitly not implemented in this slice

- **Publishing is not available.** Training drafts never join the member
  calendar feed, do not trigger a notification and cannot change a parent's
  RSVP or an existing attendance entry.
- Recurrence, bulk schedule generation, field/facility conflicts, coach
  assignment, player notifications and training plan/drill attachments
  remain separate implementation tasks.
- There is no automatic migration from legacy sessions to new drafts; it
  would duplicate original source events. Historical import remains
  independent and read-only.
- Live deployment is prohibited. Execute operator grants only against
  isolated staging after database readiness, and validate full workflow
  with real assigned Academy groups and revoked sessions.

## Release acceptance

1. All five GitHub CI jobs green **on the exact PR head**.
2. Disposable PostgreSQL migration apply, rollback and reapply green.
3. Explicit admin grant script passes with admin/mobile role separation.
4. Manager create, edit and archive work with confirmed versions; stale
   version returns 409; unauthorized roles/owners are denied.
5. Verify new drafts never appear in mobile player/parent calendar or
   attendance, and verify imported calendar row counts unchanged.
6. Full real-environment stage tests and visual comparison before enabling
   any future publish capability.
