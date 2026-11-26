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

## Follow-up: family and private-conversation invitation actions (25 November)

UX-NOT-06 (P0): coded and simulated handler-tested; build and runtime not verified.

- Fan notification screen shows a bounded count of currently pending family invitations from the existing OTP recipient endpoint. The existing profile acceptance/decline workflow remains the only mutation path.
- Parent, coach and management notification screen shows incoming pending private-chat consent requests from the existing authorized conversation directory. Outgoing and accepted conversations are excluded; management counts reject unexpected organization scopes. Player and fan identities cannot open private chat through the query parameter.
- Direct navigation to private chat uses a role-checked URL mode. Invitation counts remain separate from announcement unread counts; neither message unread receipts nor unified persistent event notifications are implemented.
- Invitation reads retain only a validated count and clear on hidden document, failures and disposal. Family invitation refresh is blocked during acceptance or decline.
- Private-chat fixes: clear drafts on conversation change, refresh or hide; discard superseded contact queries including repeated same-room selection; require selected contact in returned directory; reject non-advancing page offsets, deduplicate page results and clear messages/draft on failed read or pagination. Does not guarantee immediate idle revocation or replace backend authorization.

Implementation commits: `38a21934ca409b98adb0aeac4db561d9642397c7`, `188f22fa5c25fd4f2c50f855dd02b21c53970f98`, `b6f1e430814fb3f9bf328459f7e9036bc5c7667f`, `7a466b0ffbe918a5f76d2eb8e746e7efc570f8e6`, `6a0238b797c57818ba90e4140c3855dc33b926b9`, `d87ee0b63cae1237fc03d9af402ca9aedd2a47bf`, `2a75717427e450a1c0bbe00e810f55228afbf867`. Regression tests: `aa5eb1c3b76740d1c18ba1cac8ae44a676bb81af`, `23e6795b9f7f8b537b5cc530588b50e614e22793`.

Verification: five functional/async cases and one source-assertion group passed from committed source after stripping TypeScript annotations and omitting Svelte compiler invocations. Initial failed pagination case exposed retained messages; corrected in 2a75717 and retested successfully. Full Node/Svelte build, authenticated role and DB verification, staging and CI still required. No provider, SQL grant, production or staging changes.

## Follow-up: real group-chat unread API and safe group navigation (25 November)

API-NOT-02a / UX-NOT-07 (P0): coded; focused JavaScript and source tests passed. SQL, compiled API, generated contract and authenticated browser not verified.

Original read-only API comparison: original notification centre returns unread counts by destination and supports conversation read and mark-all operations. OS group messaging already persists exact message-version read receipts, but previously exposed no member unread summary. Implemented an incremental, independently authorized group count, not a complete unified inbox.

- New GET /api/mobile/chat/unread-summary with bearer policy and private/no-store cache. SQL uses live roster directory and actor identity/role/expiry, selected management organization, messages notification preference, and current-version receipt for the same actor. Excludes self-authored and deleted rows. Count caps at 999; no message bodies, reader names, writes or additional raw-table privileges. Existing group read acknowledgement remains the source of truth.
- New migration 20261125070000_MobileGroupChatUnreadSummary and narrow runtime EXECUTE grant. C# DTO, store and endpoint are wired. Checked-in OpenAPI contract amended in canonical sorted format; regeneration from the compiled host remains required.
- Mobile notification workspace displays the group-message count separately from announcements, consent invitations and operational actions. Fans excluded; management scope remains required. Fresh foreground/manual read and error state; no private-message unread or device push implementation.
- Group chat clears drafts on group changes, denies non-directory room selections, discards delayed responses, deduplicates pagination, caps supported offsets and clears private rows on failed read. No claim of immediate revocation during idle or every possible mutation path.

Migration: e1c99f7; C# DTO/store/endpoint: a54e418, 2f3cf2e, a1cdf49; grants: 2431d8f; contract: bcfb300; validator/controller: 423533a, 66b0f30; UI/route: 05f595b, e73913b; group fixes: 73b97ec; HTTP integration assertions: 25feaaa; frontend tests: 9736b67.

Verification: three functional/async cases and two source assertion groups passed when executing committed source with TypeScript annotations stripped and Svelte compile calls omitted. Added HTTP integration coverage for fan denial, assigned coach unread count, GET not marking read, explicit receipt, repeated receipt and edit invalidation. Those database/HTTP tests have not been executed: local runtime has Node 22 but no dotnet or PostgreSQL client/server available. No CI or staging verification, no production change.

Remaining: generated-contract comparison, disposable database migrations/grants and actual integration tests, preference and role/organization revocation checks, private conversation read receipts, explicit long-message acknowledgement, generic durable notification events and cross-destination unread totals. Keep draft unmerged until mandatory checks pass.

## Follow-up: private chat read receipts and unread count (25 November)

API-NOT-02b / UX-NOT-08 (P0): coded; focused simulated receipt tests and source assertions passed, real database/API/browser verification pending.

- Added `GET /api/mobile/private/unread-summary` and `POST /api/mobile/private/conversations/{conversationId}/messages/{messageId}/seen`. Existing adult-role consent and current common roster checks remain authoritative, with management organization scope resolved by the canonical directory. No fan/player access, no named private reader list.
- New migration 20261125071000_MobilePrivateChatReadReceipts persists one receipt per message and recipient, bound to the current message version. Message lock and repeated authorization precede the insert/upsert; actual RETURNING is required for success. Repeated receipt keeps its timestamp; changed content requires the new version. Own, deleted and viewer-hidden messages are excluded. Count capped at 999, respects messages preference; GET does not mark read.
- Narrow runtime EXECUTE grants and raw receipt table denial are inside the existing provisioning transaction. New OpenAPI entries are hand-maintained in canonical format, not verified against compiled host.
- Mobile private chat supports focused 75% viewport observation and a manual accessible confirmation for long messages; duplicates suppressed while in flight, hidden/own/stale/deleted messages rejected, confirmed status only after exact response. UI confirmation is session-local; persistent truth is server receipts. Notification centre shows separate private unread card only for eligible adult roles, with existing private-chat navigation.
- Added HTTP integration assertions for scope denial, exact read receipt, idempotency, edit-version invalidation, own messages, hide history and membership revocation, with receipt cleanup before deleting test messages. No real integration run.
- Three functional/asynchronous simulated tests and two source assertion groups passed on committed files, TypeScript annotations stripped and Svelte compilation omitted. Build, generated contract, SQL migration, actual authorization and staging remain unverified.

Commits: migration 4fdafe1; DTO/store/endpoint e1548f8, 2df6e43, e1b481c; grants bf1fd64 and transactional correction 5971661; contract 22bcb19; controller b4d4689; private chat UI aeaa941; summary panel and routing 52ee9b9, 8d00346; integration tests fdf42ef; focused tests 34464b9.

Remaining: unified persisted notification feed, cross-destination count, preference parity, mark-all policy, actual tests and migrations. Existing private history without receipts will appear unread unless hidden/deleted/own; no retrospective read inference. Production and staging unchanged. Keep draft unmerged until mandatory checks pass.

## Follow-up: cross-destination unread summary (25 November)

API-NOT-02c / UX-NOT-09 (P0): coded; focused validation and source checks passed; database, API and UI runtime not verified.

- New GET /api/mobile/app/notifications/unread-summary combines existing authorized announcement, group-chat and private-chat count functions under the bearer policy and no-store cache. Returns only counts and enabled flags, no notification bodies, names or IDs. Private counts suppressed for players and valid empty directories; fan excluded. Total capped at 999, independently respects administrative/messages preferences.
- Migration 20261125072000_MobileNotificationUnreadSummary, DTO, store, endpoint and narrow EXECUTE grant inside provisioning transaction. Hand-updated canonical OpenAPI contract requires compiled-host regeneration.
- Mobile shell badge is now exclusively sourced from the combined summary, including while inside the notification screen. Announcement-page counts invalidate the combined snapshot rather than overwrite it. Identity/role/organization scope key, foreground refresh, 60s polling, disposal and stale-response coalescing preserved; hidden document clears badge. Confirmed group/private receipts and private history hide trigger refresh. The destination cards still fetch their individual APIs; simultaneous snapshots can temporarily differ.
- Four focused checks passed (two validator cases and two source assertion groups), with stripped TypeScript and omitted Svelte compilation. Updated prior regression assertions and receipt harness for new callbacks. No full suite, compiled backend, PostgreSQL or authenticated browser run. SQL wrapper uses existing inbox function internally (including first page) and repeated roster resolution; performance and concurrency need real DB verification.
- Commits: migration a48d171, DTO e5ffa0b, store cfdb962, endpoint 26d513a, grants a63193f, contract 170cac4, validator 76fcdf6, controller 3917c10, badge 1366344, group/private callbacks dd7a1ab / 75821f4, existing test updates 053fed7 / 72bca47, focused tests 6f94af9.

Remaining: generic persisted notification event feed (not merely counts), safe mark-all semantics (do not fabricate message visibility receipts), DB/API integration including combined preference and scope checks, mobile acceptance, performance and original feature parity beyond messaging. No deployment, no production changes. Keep draft unmerged until required checks pass.

## Follow-up: chronological unread activity and authorized navigation (25 November)

API-NOT-02d / UX-NOT-10 (P0): coded; focused validation, navigation and source checks passed in simulated harness; compiled backend/database/browser not verified.

- New GET /api/mobile/app/notifications/activity?kind=all&offset=0 projects unread activity chronologically from existing durable announcement events and message tables. Filters all/announcement/group_message/private_message; 30-item pages, bounded offset, stable sort and one extra row for next page. Edited message timestamps and version-specific receipts determine current unread status.
- Current directory, active identity, notification preferences, exact accepted private pair, participant and hidden-history checks are enforced in SQL. No private or group message bodies or sender names in feed. Player private filter returns empty server-side; client does not offer it. Fan denied. Counts and feed are separate requests and can differ momentarily.
- Mobile feed uses Greek filters and fresh authorized pages; stale filter responses, hidden document and failed reads clear displayed rows. Notification links open the announcement or current group/private conversation. Explicit inaccessible room links never fall back to an unrelated room; older message may require paging in destination. No implicit read on feed GET/open; existing in-conversation visibility/explicit receipt behavior remains.
- New migration 20261125073000_MobileNotificationActivityFeed; DTO/store/endpoint, narrow EXECUTE grant in provisioning transaction, hand-updated canonical OpenAPI contract. No new generic event ledger or dismissal storage. Bulk mark-all-as-read intentionally not implemented because it would conflate notification dismissal and actual message visibility; separate persisted per-recipient alert state remains future work.
- Verification: five focused functional/simulated checks (validation, URL navigation, group isolation, stale room response, unavailable explicit target) and two source assertion groups passed. Harness strips TypeScript and uses minimal URL API stand-ins; compiler calls omitted. Initial harness errors due to residual TS generic and unavailable URL global were corrected in harness. No full suite run. Added authenticated integration assertions for feed filter validation, fan denial, group scope, private projection, player exclusion and receipt removal; not executed.
- Commits: migration 44f5f9b, DTO b06df40, store 80f59a7, endpoint 0ece39a, grants 3e1c6fa, contract 93518c7, validator 5491672, controller f2aa60c, feed fc418c5, route ad122cf, group/private links d7cb6de / 613f9e9, previous test fixes accad9c / d87e440, new tests 206cbe3, HTTP assertions 3dc82ad / 4c06b70.

Remaining: verified migrations, runtime-role grants, generated contract and database/HTTP/browser tests; feed query performance and mutable offset pagination (refresh returns to first page); explicit old-message deep links and separate notification dismissal/mark-all state. Then prioritize Academy drill library and coaching/match workflows. No staging or production change; keep draft unmerged until mandatory checks pass.
