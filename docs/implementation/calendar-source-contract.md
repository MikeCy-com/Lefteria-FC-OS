# Calendar source contract

This slice was mapped against original GitHub source at
`a52d66b8e01c010cfe95ce53fa139e126bae4d84`. It covers calendar schedule reads,
parent/player availability and scoped staff RSVP reads. This source revision is
not proof of the deployed revision, current production records or the club's
business time zone. The [implementation and recovery guide](scoped-calendar.md)
defines the target import and API boundary.

## Distinct source collections

| Collection | Meaning | Target discriminator |
| --- | --- | --- |
| `fixtures` | Match schedule, status, teams and result metadata | `kind=match` |
| `events` | General calendar events, including legacy training-type events | `kind=event`; original `type` remains `event_type` |
| `training_sessions` | Staff training document with optional frozen member publication | `kind=training` |
| `availability` | Parent/player intention: `going`, `not_going`, `maybe` | RSVP row |
| `event_attendance` | Event RSVP mirror, also permitting `no_response` | Separate RSVP row |
| `attendance` | Physical participation: `present`, `absent` | Separate physical-attendance row |

An event ID alone does not identify its source collection. The target retains
`kind` in item, scope, guest and response keys. A historical response without an
event type can resolve only when its ID occurs in exactly one schedule kind.
Malformed or duplicate schedule competitors still count when testing ambiguity;
discarding a bad competitor must not silently change a response's destination.
The source alias `fixture` resolves to `match`.

The source availability handler searches the three schedule collections, checks
the selected owned child/player and assignment/invitation, then writes
`availability` and `event_attendance` separately. Physical attendance uses its
own handlers and collection. The target makes the two RSVP writes and their
audit atomic without writing physical attendance.
[Source RSVP handler](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/routes/mobile_auth.py#L1372),
[physical attendance handler](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/routes/mobile_auth.py#L1447).

## Organization and relationship authority

A known non-null source `organization_id` takes precedence over legacy
classification. Untagged Academy markers include group IDs and English/Greek
Academy team-type text; fixture competition markers are also considered. The
event writer documents an older `team_id` field containing an Academy group ID.
Only that verified group alias is normalized. Contradictory or cross-organization
references require quarantine, rather than reassignment to another club.
[Organization classifiers](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/organization_scope.py#L181),
[event normalization](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/server.py#L4717).

Guest invitations are explicit player IDs bounded to the event's organization.
The source guest writer validates active, unarchived, untransferred players and
allows at most 500 IDs. A guest relationship can cross groups within the same
organization; it does not grant a coach the unrelated host group's full roster.
[Guest validation and write](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/server.py#L4835).

The target derives authority from current full-phone canonical links and active
players/staff/admins. It uses every current coach group/team assignment, rather
than the profile's singular display team. Parent selection changes the owned
child, including children in different organizations; it grants no access to an
arbitrary supplied ID. Management requires its current selected organization and
actual current grant. These checks apply again in each final repository SQL
statement, including RSVP writes. See the broader
[role and attendance contract](role-dashboard-contract.md#shared-attendance-publication-and-privacy-conditions)
for the source management scope gaps that must not become target grants.

## Frozen member training publication

The source creates staff training as a draft. Review/approval can create a
`published_snapshot`; later staff edits leave that prior member snapshot visible.
The member projection uses snapshot schedule fields and the allowlisted
`member_summary`, `session_journey` and `preparation`. It never copies draft
notes, exercises, coaching descriptions or arbitrary source metadata.
[Draft writer](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/server.py#L4337),
[review and publication](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/server.py#L4508),
[exact member allowlist](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/session_views.py#L9).

An existing snapshot takes precedence even when the current root status is
`draft`. Its date can differ from the current staff draft's date. The target
stores separate staff/member projections and separate day ranges, then filters
the month using the audience's actual range. It does not select by the draft
month and subsequently substitute a snapshot from a different month.

Without a snapshot, draft/review/rejected training remains hidden from members.
Legacy schedule visibility exposes only the safe schedule fields, without root
member text or private training content. An invalid snapshot quarantines the
source row; the importer does not fall back to publishing the staff draft.
Staff calendar reads also use a curated schedule projection; this slice does
not implement the private training editor or approval workflow.

## Dates and complete export boundary

Source fields can contain date-only values, naive timestamps, offset timestamps
and separate local-time overrides. No authoritative club time-zone setting has
been established from this source review. Each target calendar manifest must
declare a resolvable IANA `time_zone`; its selection is an operator assertion to
verify during export rehearsal.

Date-only values remain calendar days. Aware timestamps convert to the declared
club time zone. A separate time field overrides the wall time on that converted
day. Naive values resolve only in that declared zone; DST gaps and folds require
quarantine rather than an invented instant. A supplied offset can disambiguate
its original instant, but cannot disambiguate a different overridden wall time.
Typed instants use PostgreSQL microseconds; exact finer source text remains in
the protected archive.

The source's `/mobile/my-availability` returns an array with a 500-row cap.
Source UI calendar aggregation is not a complete database export and is not a
permitted import source. The target manifest requires all six complete source
arrays, including explicitly empty arrays, from a consistent export boundary
bound to the exact roster digest and source commit.
[Source own availability](https://github.com/MikeCy-com/Lefteria-FC/blob/a52d66b8e01c010cfe95ce53fa139e126bae4d84/backend/routes/mobile_auth.py#L1684).

## Compatibility still outstanding

The new typed staff RSVP route does not replace the source
`/api/mobile/events/{id}/attendance` contract, which also contains going-player
and MVP behavior. Generic physical attendance reads/writes, match statistics,
read receipts, training authoring/review, admin calendar CRUD and notification
delivery remain separate ports. The target's month route is an additive typed
contract; preserving this slice's own-availability array does not establish
compatibility for every old client. Cached PWA clients, real export
reconciliation and live provider behavior remain staging/cutover checks.
