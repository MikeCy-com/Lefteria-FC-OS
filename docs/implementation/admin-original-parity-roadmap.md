# Lefteria FC → Lefteria FC OS: admin functional parity inventory

**Scope:** original `MikeCy-com/Lefteria-FC` read-only main; replacement is `MikeCy-com/Lefteria-FC-OS` main. The OS backend is .NET 10/PostgreSQL, frontend SvelteKit. This inventory is a **live migration map, not a declaration that all original functionality is ported**. The original administration has dozens of interactive controls and workflows that cannot be reproduced by merely copying a menu or static UI.

## One canonical location for each feature

| OS section | Canonical module and source components | OS status | Required backend/consumer |
|---|---|---|---|
| Website | Club profile / public settings (legacy club profile) | **Working:** scoped versioned club editor with audit | Current org, brand, visible public content |
| Website | Articles / news (legacy admin content CMS) | **Published read only** | Private draft/editor/publish, safe media, revisions still pending |
| Website | Sponsors, partners, media library, gallery and reels (`SponsorsTab`, `PartnersTab`, `MediaLibraryView`, `ReelsTab`) | **Not ported** | Owner-isolated asset/publisher APIs, storage sanitation, publish history |
| Website & marketing | Social Control Centre, social accounts, scheduling, community, analytics (`SocialControlCenter`, `SocialOfficialAccountsAdmin`, `SocialCalendarView`) | **Not ported** | Explicit connector credentials, verified owner, audit, safe publication |
| People & CRM | Registrations, guardian/lead intake, `PlayersCRM`, player documents (`RegistrationsTab`, `PlayersCRM`, `PlayerDocumentsView`) | **Not ported** | Child privacy/guardian consent, closed roster import, access by role and retention |
| Communications | Contact inbox, private participant chats, team chats (`MessagesTab`, `backend/routes/admin_messages.py`) | **Not ported** | Linked mobile identity, participant-only content, explicit team join + audit |
| Communications | Announcements and wall, groups, polls, suggestions (`WallTab`, `PollsTab`, `SuggestionsTab`, `backend/announcements.py`) | **Not ported** | Audience targeting from current canonical roster, per-parent/player/coach read gate, delivery audit, scheduling/push |
| Football & Academy | Team and Academy group directory (`TeamsTab`, `EnhancedAcademyTab`, legacy groups) | **Live directory + guarded manager-only group creation + manager-only paginated active player/group picker**; not yet player assignment writes | Single real teams/Academy groups view with deduplicated active-player membership counts. No youth PII |
| Football & Academy | Full player, team and Academy CRUD, season rollover and transfers (`PlayersTab`, `TeamsTab`, `SeasonRolloverWizard`) | **Not ported** | Lifecycle + owner grants, multi-group membership consistency, eligibility, atomic roster version/audit |
| Football & Academy | Training calendar / attendance / assignments / drills (`CalendarTab`, `AttendanceTab`, `TrainingSessionsPanel`, `DrillsTab`) | **Mobile calendar exists; admin controls not ported** | Separate coach assignment proof, session attendance audit, rules by age, dynamic group membership |
| Football & Academy | Fixtures, matches, officials, guest players, match analytics (`FixturesTab`, `MatchEventPlayers`, `VideoAnalyticsPanel`) | **Public fixtures imported; admin mutations not ported** | Match state machine, participants/roster, officials, permissions, history |
| Football & Academy | Evaluations, development and compliance (`PlayerEvaluationsPanel`, `PlayerDevelopmentPanel`, `StaffComplianceView`) | **Not ported** | Guardian scope, staff assignment, secure documents, minimization and retention |
| Football & Academy | Trials (`TrialsTab`) | **FC private state read only** | Public applicant/guardian gates and submissions remain deliberately off |
| POS & shop | Categories + products + variants (`PosTab`, `ShopTabs`) | **Working owner-scoped category/product editor; no checkout** | Audited versioned catalog, fixed EUR cents, independent sales release gate |
| POS & shop | Locations, operators, sales, tickets, orders, inventory/loyalty (`PosTab`, `LoyaltyView`, `ShopTabs`) | **Not ported** | Physical register scopes, price snapshots, idempotency, transaction ledger, permission/test gates |
| Finance | Charges, fees, expenses, reports (`ChargesTab`, `ExpensesTab`, `FinancialDashboard`, `FinancialReportsTab`) | **Not ported** | Double-entry reconciliation, role/access, invoices/refunds, payment boundaries |
| Facilities & resources | Resource management and venues (`ResourceManagement`, `EventsView`) | **Not ported** | Venue/asset calendars, conflicts, age-safe visibility |
| People & access | Staff and admin accounts (`StaffTab`, `SecurityTab`, `backend/routes/admin_users.py`) | **Admin login/role/org checking works; management CRUD not ported** | Super-admin-only identity lifecycle, credential rotation, invitations, 2FA gate |
| System | Backups, storage status, file management, changelog, APK (`BackupRestoreTab`, `StorageFiles`, `StorageStatusTab`, `ChangelogTab`, `AppApkTab`) | **Not ported** | Least-privilege operations, tested recovery, signed artifacts, separation from content editors |
| Insights | Analytics/dashboard, reviews, referrals and campaigns (`AnalyticsDashboard`, `MarketingTab`, `ReferralsView`, `ReviewsView`) | **Not ported** | Derived metrics from real scoped entities; no invented performance or transaction figures |

## Remove duplicates, not data

- **One canonical roster directory:** `club.teams` for FC and `club.academy_groups` for Academy, plus `club.players` and `club.player_academy_groups`. Both organization group screens in the old React CMS map to **one** `/admin/groups` read surface; never maintain a second editable static list of chat audiences.
- **One communications service:** support/contact inbox is separate from participant-only team/private messages. An announcement is a publication with a verified audience; it is **not** an unrestricted private chat broadcast. Individual and group push notifications are delivery records of that publication, not another copy of the message.
- **One athlete identity/lifecycle:** player CRM, team roster, parent family links, attendance and evaluation reference canonical player IDs and scoped grants, not independent user-created duplicates.
- **One calendar domain:** practices, attendance sessions, venues and fixtures share scheduling primitives but retain distinct event types, permission gates and transaction semantics.
- **One catalog with distinct financial ledger:** product and price/variant data are not editable copies in CRM/finance. POS purchase snapshots and refunds must be immutable entries behind separate operator and payment gates.
- **No shared admin/mobile session:** linked management identities do not give an administrator an automatic path to private youth messages, families, or medical/evaluation notes.

## Dynamic UI contract

1. The admin navigation is generated from authenticated **current** `role + organizations + permissions`, while every API independently rechecks the session and owner grant. Modules not ported have disabled, clearly labelled controls, not invented results.
2. Every roster group and active count comes from the server's canonical records. Academy members may belong to primary and additional groups; membership is deduplicated and excludes inactive/archived/transferred athletes. An uninitialized roster returns **503**, never a fabricated empty result.
3. Inputs such as new team placement or player status must be audited, versioned transactions. Refreshed, role-aware data is re-fetched on owner changes, expired sessions, revocation, saves and conflicts. Until safe assignments exist, coaches have no global youth directory.
4. A future announcement writer validates the **current group** and recipient memberships again at send time; a preview does not authorize a send. Delivery is outbox/worker-driven with consent, retries, revocation, de-duplication and receipts. Private chat reads must require actual linked participant identity.
5. The parent/player/coach mobile homes consume their own scoped APIs. A CMS write cannot be considered complete until it is verified end-to-end from the intended role, with accessibility and Greek UI parity.

## Implementation sequence (verified slices only)

1. **Completed:** private roster-derived audience directory with FC/Academy counts and a unified parent-scale admin screen. **This slice:** manager-only audited creation of real FC teams and Academy groups, no player membership mutation.
2. **Now:** verified, manager-only private roster picker with player names, current FC team or deduplicated Academy group IDs, 20-per-page UI and no youth contact/birth data. **Next:** audited versioned assignment/removal writes and actual coach role assignments, with strict youth access and ownership tests.
3. **Then:** announcement drafts and explicit publish to current, deduplicated groups; mobile parent/player/coach read gates and outbox delivery. No direct broadcast from unverified CMS input.
4. **Then:** participant-only messages, deliberate team chat joins and safe attachments; permission revocation and youth retention tests.
5. **Remaining workstreams:** calendar/attendance, evaluations, CRM/registration, website publisher/media, POS locations/inventory/checkout, finance, staff/security, marketing and operational tools, each separately migrated and acceptance-tested.

**Release safety:** all features must pass the five-job Rewrite build CI, disposable PostgreSQL authorization/rollback tests and role-specific browser checks before OS `main`; live Hostinger production and the original Lefteria-FC remain untouched. This is a repository-level rewrite, not a production rollout.
