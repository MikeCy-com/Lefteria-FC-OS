# Role dashboard source contracts

Read-only GitHub audit on 2026-10-25, pinned to source main
`5111f06af5d8b17f855ceed9637caca02db117e2`. Evidence is repository code, not deployed
behavior or live data. This extends the [current-state inventory](../architecture/current-state.md)
and [accepted feature queue](feature-queue.md); it does not implement dashboard APIs.

The original repository's [PR #42](https://github.com/MikeCy-com/Lefteria-FC/pull/42)
was open at the initial inspection. A GitHub recheck on 2026-10-26 confirms it is
merged into source main at `7833daceceac1611c368e7bc4df2a82c64b45e1a`, from final
head `7e66096201cd502381562c905a77a329dfd25924`. The 15 changed files remain
frontend-only. Its layouts are now source-main behavior; no backend authorization
fix or deployed revision is established by that merge. Backend contracts below
remain pinned to the unchanged `5111f06` files; update frontend caller mappings
against the merged layouts when implementing each dashboard.

## Shared boundary

Each dashboard GET has no body or selector query: its mobile Bearer identity must
have the exact role named in the URL. A different available identity requires an
explicit switch. Team/group/guardian/organization scope must come from current
server relationships, not browser roles, a selected UI card or a screenshot.

Source responses contain Mongo documents without formal response DTOs. The field
lists below distinguish returned containers from fields the screens actually use;
they are not permission to serialize every legacy document into the rewrite.

## Parent

`GET /api/mobile/parent/dashboard` returns:

- `children`, `groups`, `group_players` keyed by group ID;
- `fixtures` ≤20, `training_sessions` ≤20, unified `events` ≤20,
  `announcements` ≤10;
- `financial_records` for linked children, and
  `attendance:[{player_id,attended,total,rate}]`.

The source reads `linked_player_id`/`linked_player_ids` from the mobile identity,
loads those players, derives their academy groups, and scopes fixtures by group.
Upcoming explicit guest invitations also contribute events/fixtures. Group/team
training comes through `member_session_view`, not staff drafts. For other children
in the same group, the roster allowlist is exactly `id,name,number,position,image_url,
team_id,team_type,academy_group_id,academy_group_ids`; own children retain full
source records. Announcements pass shared visibility plus parent-group filtering;
reaction summaries contain counts/mine/total and omit the raw voter lists.

Home uses actual child `id,name,image_url,number,position,academy_group_name`, the
next three sorted upcoming schedule items and announcements. The financial
destination uses the family wallet. Own-child detail uses `statistics.goals`,
`assists`, `minutes_played`.
The returned `attendance.rate` counts RSVP `going` over all `event_attendance`
rows; it is not physical present/absent attendance. `group_staff` is read by the
frontend but absent from this dashboard response. The optional own-child
`GET /api/mobile/players/{id}/report` caller swallows failure; no matching route
was found in the pinned GitHub search, so evaluations/notes are not a confirmed
working dependency.

Sources: [backend, lines 643–799](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L643),
[ParentDashboard callers](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/ParentDashboard.jsx#L43),
[schedule normalization](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/parentDashboardSchedule.js#L1).

### Parent finance and RSVP dependencies

| Route | Inputs and response used |
|---|---|
| `GET /api/mobile/my-availability` | No input; linked players' `{event_id,player_id,status,…}` rows. Client keys by event **and** child. |
| `POST /api/mobile/availability` | `event_id,player_id,status`; status `going/not_going/maybe`. Parent must target a linked child; player can target only self. Event must exist and admit that player by group/team/explicit guest invitation. Returns `message,status,player_id`; writes availability and legacy RSVP records. |
| `GET /api/mobile/charges/{player_id}` | Optional `month=YYYY-MM`, `all=true`; current month by default. Returns `balance,pending_count,charges,totals:{pending,overdue,paid},grace_period_days,month`. Overdue derives from due date plus grace. |
| `GET /api/mobile/charges/{charge_id}/receipt.pdf` | Charge ID; PDF receipt. |
| `GET /api/mobile/invoices/multi.pdf` | `ids` comma-separated, or `player_id,period_key`; family UI submits pending/overdue charge IDs; PDF invoice. |

Source charges/PDF handlers and their mobile router have **no authentication or
guardian dependency**; sending a token from the UI does not fix that source gap.
The rewrite must require a current canonical guardian relationship for every
charge and invoice line. The source parent dashboard itself also relies on stored
linked IDs without rechecking the player's current guardian phone. Do not treat
those stored links alone as a current grant. Finance request failure currently
falls back to an empty zero-balance object in the React UI; that is not evidence
of a zero balance and must not become fabricated success data.

Card payment is explicitly held by `STRIPE_UNDER_DEV=true`; the unreachable modal
names `POST /mobile/charges/stripe-quote` (`charge_ids`) and `/stripe-session`
(`charge_ids,origin_url`). This audit did not call either provider path or verify
them as working. Parent-to-parent chat uses
`POST /api/mobile/chat/with-parent-of/{player_id}` with `{}`; it rechecks current
group/conversation policy and returns a conversation with a safe other-user view.

Sources: [availability](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1372),
[charges and PDFs](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/charges.py#L734),
[wallet failure/payment hold](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/ParentDashboard.jsx#L744),
[chat policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L2179).

## Coach

`GET /api/mobile/coach/dashboard` returns `teams,groups,players,fixtures,events,
announcements,training_sessions`. Current staff is resolved by linked staff ID
plus matching canonical phone, or one unambiguous matching phone. Assignments
combine staff singular/plural group/team IDs and groups whose `coach_id` matches.
`assignment_query` permits assigned academy groups or team-only records without
academy markers; an empty assignment stays empty. It does not mean all teams.
The helper does not itself reject inactive/archived staff; a target grant must
also validate current canonical staff status rather than copy that omission.

Limits are teams/groups 50 each, players 500, fixtures 20, upcoming events 10,
training sessions 20. Coaches receive their scoped staff training documents;
parents/players receive the safe member projection instead. Home/roster uses group
and player counts, real roster identity fields, next fixtures, sessions and
announcements. Player detail uses stored `statistics.goals/assists/minutes_played`.
The label “this week” currently uses all returned sessions, with no week filter;
it cannot establish a weekly training total.

Reports additionally call `GET /api/admin/attendance/stats`, whose optional
filters are `team_id,academy_group_id,player_id`. Output is `total_events`,
`player_stats:[{player_id,player_name,going,not_going,no_response,total,attendance_rate}]`,
`overall:{going,not_going,no_response}`, and
`attendance_stats:[{player_id,player_name,present,absent,total,attendance_pct}]`.
The UI sums present/absent for its overall rate and charts the top six players.
This endpoint requires an **admin `type=access`** token; the dashboard supplies a
mobile `type=mobile_access` token. Do not weaken token realms to make that source
caller work; no successful mobile report was established by static code review.

Sources: [dashboard](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L802),
[current assignments](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/mobile_scope.py#L5),
[screen/report computation](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/CoachDashboard.jsx#L346),
[report endpoint](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L5219),
[admin realm](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/auth.py#L68).

## Player

`GET /api/mobile/player/dashboard` returns `player,stats,fixtures,events,
announcements,development_plans,evaluations,training_sessions`. It resolves only
the identity's `linked_player_id`; no player selector is accepted. Source fixture
queries use the player's academy groups (20 per group), then sum matching
`player_performances` into `stats:{goals,assists,appearances,minutes}`. Fixtures are
returned ≤10. This is a bounded fixture aggregate, not a complete season report:
there is no season/completed-status filter, and a first-team-only player without
an academy group gets no fixtures through this loop. Team assignment contributes
to training scope, not that fixture loop.

The hero consumes actual `name,image_url,number,position,team_type,
academy_group_name`; four metric cards use the four returned stats. Upcoming and
completed match cards filter the returned fixtures. Development cards use real
`title,status,progress` (first three); evaluations use `period/title,
overall_rating,notes` (first two). Training uses the safe published/legacy member
view. Neither a missing player link nor absent plans authorize sample entities or
invented progress. This is private own-player data, distinct from the approved
public player-card projection in [profile-contract.md](profile-contract.md).

Sources: [dashboard](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L839),
[actual screen fields](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/PlayerDashboard.jsx#L133),
[member training allowlist](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/session_views.py#L59).

## Management

`GET /api/mobile/management/dashboard` reloads a matching active admin assignment
and resolves one organization. `POST /api/mobile/management/organization` takes
`{organization_id}` and persists a currently allowed selection. A sole grant may
activate automatically; multiple grants without selection return organization
options and empty operational arrays, not both datasets. Source IDs are
`lefteria_fc` and `lefteria_fc_academy`.

Selected output: organization ID/name/options; `teams,groups,players,charges,
registrations,announcements,events,fixtures,training_sessions`; counts
`player_count,coach_count,parent_count`; `financial:{total_revenue,total_pending,
total_overdue}`; `attendance_summary:{present,absent,marked,attendance_rate}`.
Players exclude inactive/archived rows (limit 500); coach count uses active,
unarchived named coaching roles; parent count uses active parent mobile identities
matching the scoped players' parent phones. Financial sums come from paid/pending/
overdue **financial_records**, not the separately returned charges (both capped
at 2,000). Registrations are the latest 10. Events are upcoming ≤50; fixtures and
training ≤200 each. Attendance counts physical present/absent across scoped
players/events (≤10,000 rows), with null rate when no marked records.

FC returns first-team teams; Academy returns academy groups. Each collection uses
its organization helper, including legacy classification for records without an
explicit organization. Announcements add organization/audience filtering. Home
cards use those server counts/totals, not mockup numbers. Reports additionally
count returned `Completed` fixtures and sum stored player goals/assists. These
bounded lists do not establish historical growth, monthly trends or a collection
percentage. Team detail filters scoped players/charges by selected team/group.
Its `GET /api/events` sends no filters and filters the response in the client.
That source endpoint accepts `team_id,academy_group_id,month,organization_id`
(`month` is unused), returns ≤500 date-sorted events, and takes organization from
an optional admin session, otherwise query/default FC. A mobile token alone does
not supply the selected management organization to it; Academy detail can therefore
receive the wrong source dataset before client filtering.

`GET /api/mobile/management/players/{player_id}` requires the selected organization
and returns `player,charges,current_month:{period,charges,pending,paid},
totals:{pending,paid},stats:{goals,assists,appearances,minutes},
attendance:{going,not_going,total,pct},recent_events`. Here finance totals use
`amount_due`, while dashboard/charge views use other amount fields; reconcile this
source difference during actual data mapping. Snapshot attendance is RSVP, not
the dashboard's physical attendance. Archive/restore POSTs at the same player
prefix recheck organization ownership; archive optionally accepts `reason`.

The React mark-paid action posts `payment_method:"cash",paid_amount` to
`/api/admin/charges/{id}/mark-paid`; it requires admin finance authorization and
cannot be made valid by passing a mobile management token. Source main has
registration lists, not the pending PR's approval UI.

Sources: [dashboard and selector](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L914),
[organization policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/organization_scope.py#L100),
[snapshot/archive/restore](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1222),
[screen metrics](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/pages/ManagementDashboard.jsx#L342),
[event query/default](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L5013),
[finance router guard](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/charges.py#L145).

## Shared attendance, publication and privacy conditions

Parent/coach group detail also embeds `GroupSchedule`: it calls public
`GET /api/academy-groups/{group_id}/fixtures` (optional `season`, otherwise all
seasons; fixtures ordered by descending match date) and the `AcademyStats` component
calls `GET /api/academy-groups/{group_id}/stats` (optional `season`, otherwise the
group's season). Stats return
`group_id,season,total,friendly,competitive,other,players`; each record contains
`played,wins,draws,losses,goals_for,goals_against,unrecorded_results,recent_form`.
Player leaders derive `goals,assists,appearances` only from completed fixtures'
saved performances. Group ID bounds those source queries, but their public
registration supplies no private guardian/coach grant or youth-profile consent.
[Schedule caller](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/frontend/src/mobile/components/GroupSchedule.jsx#L13),
[fixture handler](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L693),
[group stats](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/academy_matches.py#L48).

`GET /api/mobile/attendance/{event_id}?event_type=event|training|match` returns
`event_id,roster,records,is_locked,summary:{total,present,absent,unmarked}`. Roster
fields are `id,name,number,position,image_url,attendance_status,marked_by`.
`POST /api/mobile/attendance/mark` accepts
`event_id,player_id,event_type,status:present|absent`; past events are locked.
`fixture` is a legacy alias for `match`. Parent/player scope checks linked players
and event assignments/invitations. Coach checks current assigned host scope and
explicit guests; an outside coach sees only invited players from their own scope.

The source management branches in this generic attendance read/write and
`GET /mobile/availability/{event_id}` do not enforce selected organization, unlike
the management dashboard and `/mobile/events/{id}/attendance`. That is a source
gap, not a grant to carry into the target. All roster/record queries need both
event and player ownership before data is exposed or attendance is written.
[Read/write handlers](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1447),
[event-specific management guard](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/routes/mobile_auth.py#L1101).

Member training exposes only safe schedule fields plus `member_summary,
session_journey,preparation` from the published snapshot. Draft/review/rejected
sessions without a published snapshot are hidden; legacy schedule views omit
private notes and exercise plans. Announcement visibility combines group/team
relationships; management is broad in the shared helper and therefore needs its
additional organization filter.
[Training projection](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/session_views.py#L9),
[announcement policy](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/announcements.py#L2).

## Pending layouts, accepted references and rewrite status

PR #42 adds team/player/approval/communication sections, compact coach/parent
cards, week-first calendar and public-profile layouts. Its approval action calls
`PUT /api/admin/registrations/{id}/status` with `status,admin_notes` and
`assigned_group_id` for approval, requiring a group **in the UI**. Source backend
still requires admin `type=access`, has no equivalent mandatory-group check, and
does not validate an organization-owned registration/group in that handler. The
frontend-only PR does not establish a safe working mobile approval contract.
[Pending caller](https://github.com/MikeCy-com/Lefteria-FC/blob/3411e01ed92aac8a6c55f6969f8ead99f9614ee5/frontend/src/mobile/pages/ManagementDashboard.jsx#L106),
[unchanged source handler](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/backend/server.py#L4004).

Pending PR placeholders explicitly cover historical player growth/monthly
attendance, staff/facility management, medical/documents/tracking, add-player/team,
child-specific messages and unsupported availability/season details. Its label is
`Under Development`; the accepted rewrite label remains exactly
**Under Developments**. The supplied mockups establish black/gold role layouts,
existing Fira fonts, global role/notification/profile header, explicit FC/Academy
selection and **no parent + button**. Their example numbers, names, charts and
“Super Admin” title are not datasets or a new mobile permission grant. Current
target composer eligibility remains fan/player only.

At rewrite checkout baseline `257b61dbaccc7b851bf9766390496616bf35c379`, real
implementation covers OTP/roles/session recovery, PostgreSQL identity grants,
organization context, own profile/preferences and the authenticated mobile shell.
The API has none of these four operational dashboard routes. The persistence
adapter still emits empty player/team/group links until real roster relationships
are migrated; the app renders **Under Developments** for those destinations.
No operational dataset, coach report, finance/PDF flow, attendance, approval or
public player profile is claimed implemented by the current identity slices.
See [identity implementation](mobile-identity-implementation.md),
[profile contract](profile-contract.md), and
[target navigation](../../frontend/src/lib/mobile/navigation.ts).

This document supplies source contracts and known gaps for the scoped data port
after browser verification and the identity import bridge. It adds no roadmap,
sample records, provider calls or production changes.
