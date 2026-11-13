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


## Follow-up: same-group training collision protection

Database trigger `club.reject_academy_training_plan_overlap` now serializes
same-organization, same-group, same-day active draft writes using a transaction
advisory lock. It rejects overlapping ranges with a conflict (HTTP 409);
adjacent non-overlapping plans are allowed. Private archived drafts release
their occupied slot. Training cannot silently cross into the next day;
midnight-spanning drafts are rejected as invalid until a full overnight
scheduling model exists. This prevents concurrent managers from bypassing
the UI's availability checks and does not assert conflict-free **facilities**
across different groups: that requires a separate facility assignment model.

The PostgreSQL rollback-only lifecycle now covers overlapping sessions,
overnight rejection, version conflicts, archive and reuse of a free slot.
Neither this guard nor the private planner publishes events to member calendars.


## Human review of an exact draft revision (follow-up)

A manager can explicitly approve a private training plan after checking its
Academy group, local training day, starting time and duration. The API requires
`confirm_human_review=true`; PostgreSQL reauthorizes the manager, locks the
draft row and records an immutable `plan_id,plan_version,approved_by`
approval with a read-only snapshot. A repeated approval for that version
conflicts; an edit increments `version` and **invalidates the prior approval
for any future publication gate**. Approval of archived plans, stale versions
or plans dated before the current Cyprus calendar day is denied.

The frontend reads approvals only for the current authorized organization,
group and date range and checks the approval against each exact plan ID and
version. It must never infer approval from a client checkbox alone.

**Approval is NOT publication.** Nothing is inserted into canonical
`club.calendar_items`, none of the child/guardian RSVP or attendance tables
change, and no message or push is sent. Subsequent publication work must
atomically verify a current human-approved version *and* implement member
calendar reads, delegated and direct RSVP, real attendance eligibility,
cancellation, coach assignment and safe member notifications. This follow-up
adds the indispensable versioned human gate, not those remaining behaviors.


## Exact-revision publication distribution preview

A manager may inspect a human-approved draft's **proposed** player reach
without revealing any child record. The `GET
/api/admin/academy/training/plans/{planId}/publication-preview` endpoint
locks the live manager session, current Academy grant, draft and exact
human approval version in PostgreSQL before calculating the count of
currently active Academy players associated with the exact group. The
response contains day, time, duration, title, location, version, group,
`eligible_player_count`, `human_approved=true` and `published=false`.

It intentionally contains **no player IDs, phone numbers, names, emails or
private coach planning notes**. Counts are a live estimate; membership may
change before actual publication. The frontend rejects mismatched IDs,
groups, versions, changed schedules, unknown extra child collections and
any response claiming publication.

**This is read-only.** It does not establish recipient authorization for
individual read or RSVP, send a notification, generate a fixture, or write
to the member calendar. A future publication transaction must revalidate
all of the above against current authorization and rollout conditions.


## OS-native approved training: canonical publication

The manager may explicitly publish a currently human-approved draft through
`POST /api/admin/academy/training/plans/{planId}/publish` with
`confirm_publication=true` and the exact draft version. Publication is
irreversible in this slice: editing the published draft is blocked, the same
plan cannot be published twice, and the published canonical event has a
separate provenance record `os_training` and a unique stable
`os-training-<plan UUID>` event ID.

This **does** add a real `training` item and its exact Academy group scope to
`club.calendar_items` / `club.calendar_item_scopes`. Existing private
calendar endpoints and RSVP authorization consume those same tables; the
Academy attendance register also reads those canonical events. The mobile
projection deliberately omits internal notes, private user records and any
unverified attachments. Parent/player eligibility is checked against current
Academy group membership, including primary group, on each request. Source
documents imported from the original Lefteria FC are never rewritten.

The publisher requires an active manager session and Academy grant, a matching
human approval of the current draft version, an Academy group with at least
one active assigned player and a non-past training date. The admin UI requires
a separately confirmed distribution preview and a deliberate
publication-specific checkbox. Both database writes and the audit event
succeed or fail in one PostgreSQL transaction. Publication status can be read
back separately by authorized managers, which prevents an apparently editable
draft after successful publication.

**Not yet included:** automatic push/SMS/email notifications, coach-side
editing of the event, cancellations/reschedules of already published sessions,
recurrence, facility conflict checks across *different* groups, and provider
billing. A published event appears through existing member calendar and RSVP
paths but those additional management operations still require dedicated
implementation and staging acceptance.

**Important deployment gate:** this migration alters canonical source
constraints to allow separately identified OS-native rows. Before running
against any staging database with imported history, take a verified backup and
validate import/reconciliation behavior after an OS-native publication;
do not rerun a destructive source import over published records. No live
Hostinger deployment or original repository modification is authorized.


## Published OS-native training cancellation

Only an authorized Academy manager can cancel a **published OS-native**
training plan by explicitly confirming its exact published version and
providing a public-safe, trimmed reason (8–500 characters). The database
rechecks the current admin session/Academy grant, verifies the publication,
rejects duplicates and past-day cancellation, and records an immutable
cancellation row plus security audit. Imported legacy sessions cannot be
changed using this function.

The event remains in `club.calendar_items` with
`is_cancelled=true`, `status=cancelled` and a visible cancellation reason.
Parent, player, coach and management calendar readers still see the
cancelled event if currently authorized: the private OS-native primary
Academy-group predicate no longer hides cancelled events. Existing
RSVP/attendance history is preserved; no response is rewritten or erased.
The existing mobile availability write statements reject cancelled events
using `NOT c.is_cancelled`. The manager can see the cancellation in the
publication list and cannot cancel the same plan twice.

**No automatic push, email, SMS, or fan notification is sent by cancellation.**
A manager must use the separate announcement/messaging tools for urgent
communication until a verified Academy training notification pipeline is
implemented. This slice does not implement editing published session times,
recurrence, roster-based push delivery or cancellation of imported sessions.
