# UI/workflow and API parity checkpoint — 2026-11-25

Target main baseline: `ec138ef114fbfa4e754430b4439dfbb88c4c97ec`.
The original application is a read-only behavior reference; no legacy code,
private data or provider credentials are imported. This is a targeted audit,
not an exhaustive operation-level or runtime verification.

| ID | Priority | Status | Acceptance and dependency |
| --- | --- | --- | --- |
| UX-NOT-01 | P0 | Coded, not runtime verified | Replace accumulated private announcement rows with fresh authorized pages; disabled preferences, failed requests and invalid cursors clear history; retry from offset zero; previous/next controls and existing deep links. Commit 70fc72d645a2d31a00d68d94175891c1e5b339ee; source regression tests 053f9340df35c8e0c2266bf966a209929c070ea4. |
| API-NOT-02 | P0 | Partial | Unified notification list and read state for eligible events, canonical recipient checks on every request, validated deep links, idempotent read operations and preferences. Existing announcement inbox is not a unified notification API. |
| API-MATCH-03 | P1 | Needs verification | Match roster and authorized submission/review workflow, audited coach approval and persistent statistics. Depends on verified fixture and assignment scope. |
| API-DRILL-04 | P1 | Needs verification | Searchable drill library, revisions, session association and authorized publishing. Compare existing training-plan contracts before adding routes. |
| API-FAC-05 | P1 | Needs verification | Facility availability, reservations, conflict detection and territory authorization. |
| API-VOTE-06 | P1 | Known UI gap | Player-of-the-month page explicitly states voting is unavailable. Eligible voter, unique vote constraint, closing and result publication; no invented results. |
| API-ENG-07 | P1 | Needs verification | Moderated suggestions and polls; unique vote, eligibility, persistent results and administrative review. |
| API-REF-08 | P1 | Partial/needs verification | Referral administration, consent, status tracking and scoped reporting. |
| API-ENG-09 | P2 | Needs verification | Review moderation, loyalty transactions and reward fulfillment, fan prediction and leaderboard workflows. |

Existing draft PRs 467–473 must be reconciled before overlapping UI changes.
No new route or permission is added in UX-NOT-01. The API already enforces
the existing authorized announcement inbox; the UI requests one page at a
time, including when moving backward. It cannot guarantee immediate
revocation while idle and does not implement generic notification entities.

Required verification: full frontend test and check/build; authenticated
synthetic parent, coach, player and management flows; changed group access,
disabled preference and failed next-page scenarios; mobile narrow viewport,
keyboard navigation and deep link; exact-head CI and staging acceptance.
Source assertions do not establish browser behavior. No staging or production
deployment or external-provider request was performed in this checkpoint.

## Follow-up: authorized announcement navigation (25 November)

UX-NOT-03 (P0): coded and handler-tested, browser/CI not verified.
- Resolve notification deep links only inside the currently authorized directory group.
- Search up to five pages using the existing scoped endpoint; provide previous/next
  navigation when the target is older or no longer visible. Do not substitute
  another group when an explicitly linked group is missing.
- Use raw directory and selection state to preserve reference identity across
  Svelte state assignment; replace displayed rows after each authorized request.
- Reject non-advancing cursors, clear feed data/history on failure, and prevent
  reactions or attachment downloads from overlapping page/group transitions.

Implementation: `118ce3a323e8918743c533044f21948c9625ce46`.
Test updates: `e05944b78640d6d49f7efe3492355cd224c048ac`.
Executed the six directory/feed handler tests from committed source in an
isolated JavaScript harness, removing TypeScript annotations and substituting
microtask flushing for Node's setImmediate. All six passed with simulated API
responses. This is not the full Node/Svelte test suite or an actual browser.
Real API authorization, long announcements/read acknowledgments, live badge
refresh, scope revocation while idle, exact-head CI and staging remain open.
No generic notification endpoint, new SQL permission or production change.

## Follow-up: confirmed read receipts and unread badge (25 November)

UX-NOT-04 (P0): coded and simulated handler-tested, full build/browser and live API not verified.

- Long announcements now offer a deliberate "mark as read" action in addition to the existing focused-card observation; duplicate in-flight requests are suppressed and a confirmed indicator appears only after the existing authorized POST succeeds.
- The unread badge is refreshed from the server after a confirmed receipt, on foreground return and every 60 seconds on visible non-inbox tabs. No optimistic decrement or new provider is introduced.
- The active inbox owns its count while open; count callbacks carry the initiating identity/role/management organization scope. Old counts are cleared when scope/tab changes. Other tabs serialize and coalesce refreshes, discarding superseded responses and disposing the reader when scope changes.
- Deep-link parameter changes remount the announcement feed, so navigation between targets does not reuse an earlier target.
- Inbox focus/visibility events request a fresh authorized page; the inbox does not poll in place.

Commits: badge reader `ca9375298958f8f9c43a9bb53f786fb144aec0c0`; explicit read `9e3895de826458dd33e4bf6509bc972eec03f6e8`; route scope `743543fc3f90687673ed544cfba8184d4b4d57bb`; inbox scope `b0c276eb22d47ceedf45fb3db1ca4a2ecfcb0083`; tests `a1abb5725da2aca160ef6b26c9245772e2980009`.

Re-read the committed files and executed 11 asynchronous JavaScript tests (five new and six previous), using simulated API responses and stripped TypeScript annotations; all passed. Also executed 23 source assertions from the notification safety test file; all passed. Svelte compiler invocations, the full test suite, database persistence, actual role changes in browsers, idle access revocation and staging remain unverified. No new generic notifications API or SQL grants. Production remains untouched.

## Follow-up: calendar and finance actions (25 November)

UX-NOT-05 (P0): coded; scoped projection and simulated handler tests passed, complete runtime verification pending.

- Notification workspace now includes a separately labelled live actions section, using the existing authorized GET calendar and parent training-fees routes. No new event delivery, notification preference bypass, unread count, payment request, or persistence contract is introduced.
- Calendar read is independently validated against current identity, role, selected child, month and management organization. It does not change the main calendar store. Parents can switch among returned authorized children; players can see their own no-RSVP matches/training; staff see cancellation information but no player RSVP prompts.
- Current-month first-page coverage only, with up to three cancellations and three unanswered activities; additional records link to the calendar. No-RSVP does not imply mandatory attendance or overdue action.
- Parent-only fee summary separates issued outstanding, settlement pending and review required. No amount, checkout URL or payment initiation is exposed in this panel; paid fees excluded unless flagged for review. Existing finance preferences and payment permissions are unchanged.
- Data clears on refresh, hidden document, failure and stale-request rejection. Separate calendar/finance status prevents a failed source being shown as a verified empty source.
- Functional tests: six pure/async cases passed from the committed source using stripped TypeScript annotations and simulated responses; one additional source assertion group passed (compiler calls omitted). Full Svelte/Node test suite, API integration, database, actual permissions in browser and staging still outstanding.

Commits: scoped calendar reader `00e03718579e3857e7b254e6c4605aa749112d38`; projections `0bd1efe8b462e5e70e36af18f7cc045aaa617ecc`; component `965c61f10d620b511a35d98bda6b51478a31f14f`; route `349fe6684b58a9cdc1b983833208a62280b2d22f`; tests `8cee04dcaddf46d297dafc16f632cd17f9e853f3` and `5bd244db22b54c8fd3fbe633164f71d69b7a77c4`.

Remaining: unified persisted notification API, event delivery/preferences/read-state design, all-page calendar coverage, invitations/messages and role workflow acceptance. Production untouched.
