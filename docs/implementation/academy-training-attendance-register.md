# Academy training attendance — source-preserving OS implementation

**Development branch only. No live deployment, no SMS/push and no change to
`MikeCy-com/Lefteria-FC`.** This is one Academy manager slice, NOT the
complete original attendance and training module.

## Original source reviewed

Read-only `Lefteria-FC`:
- `frontend/src/pages/admin/AttendanceTab.jsx`
- `frontend/src/pages/admin/InlineAttendance.jsx`
- `frontend/src/pages/admin/TrainingSessionsPanel.jsx`
- `frontend/src/mobile/components/AttendanceView.jsx`

The original distinguishes **availability / RSVP** `going`,
`not_going`, `maybe`, `no_response` from physical attendance
`present` and `absent`. The OS's prior calendar import already preserved
the original `club.calendar_responses` with separate source collections.
Do not overwrite or reinterpret those original events.

## Ported in this slice

- Academy manager training browser from verified
  `club.calendar_items` and exact `club.calendar_item_scopes`; bounded
  93-day window, no fabricated sessions, current Academy membership only.
- Bounded private attendance roster: current Academy players linked to
  training via explicit guest or current group membership, name only,
  imported RSVP separately, imported historical attendance separately,
  manager's latest actual attendance and provenance/version shown independently.
- Actual manager-controlled `present` / `absent` register with distinct
  `club.academy_training_attendance` and immutable
  `club.academy_training_attendance_events`. It **never updates**
  `club.calendar_responses` or imported event data. History survives
  corrections, and the global admin audit records committed changes.
- Guard against marking a future, cancelled or archived session; unauthorized
  or transferred/unassigned players are not eligible. The manager's
  `expected_version` rejects stale conflicting updates. Repeated identical
  updates do not insert extra audit events.
- No direct raw youth table access to web-admin or mobile DB roles. Privileged
  operations require exact Academy owner grant, active session and manager
  role, both at HTTP and in a `SECURITY DEFINER` SQL function. Revoked session
  lock failures map to a controlled access-denied response.
- Responsive Greek admin page `/admin/attendance` inside the existing
  branded OS admin navigation. The page validates server-provided event and
  roster records, and uses explicit staff actions. Coaches/players/parents
  do not receive global roster access through this screen.
- Disposable SQL lifecycle checks seed synthetic club/group/player/calendar
  facts, test source attendance vs RSVP, mark a change, replay, conflict,
  correction and revocation, then **ROLLBACK**. Security-policy probes and
  frontend source-invariant tests accompany them.

## Explicitly still missing

This implementation does not create/edit/delete/bulk-create training sessions,
drills, training plans or fixtures. It does not grant assigned coaches narrow
access, permit parent mobile attendance changes, compute season-wide attendance
analytics or reconcile all historical staff notes. It does not automatically
send push/Twilio messages or mark an RSVP as physical presence. The existing
calendar importer must be complete, current and owner-reviewed before its
training sessions are shown. Existing historical attendance for children
without an eligible current roster link may require a separate archived
manager-only historical report to avoid falsifying present-day membership.

## Verification and release gate

All tests are on the separate development branch and require the official .NET
API, SvelteKit and PostgreSQL CI jobs. Those GitHub Actions jobs are currently
failing **before runner assignment**, including on OS `main`. No build or
database migration can yet be claimed green. Regenerate OpenAPI and typed
TypeScript contracts after jobs become available. **Do not merge this slice
until the complete suite executes on the exact intended commit**.

The broader original-to-OS parity review and all 20 blocked launch checks
are documented in `docs/implementation/go-live-gap-audit-2026-11-09.md`.
