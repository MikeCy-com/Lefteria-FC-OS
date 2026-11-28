# Group match settings and attendance-based rotation — 28 November 2026

Baseline main 1bf7d8e941563d21629453ef81548f588eb56a53 (PR #500 merged after six successful CI checks).

| ID | Priority | Status | Acceptance / evidence |
|---|---|---|---|
| ROT-CONFIG-01 | P1 | Coded | Per exact Academy group or First Team squad, persistent total playing minutes and format 5/7/9/11 (First Team 11 only). Admin settings tab, versioned writes, no global city defaults or per-match reentry. |
| ROT-SCOPE-02 | P0 | Coded | Existing manager/super-admin session and exact city/location/team permission guard; runtime table access revoked, narrow functions only. Rollback-only SQL tests added for sibling denial, stale version, separate formats, First Team invalid format. |
| ROT-ATT-03 | P1 | Coded | Assigned host coach confirms actual attendees from persisted selected squad on match day, Cyprus local date. No RSVP inference. Versioned revisions, attendance corrections before completion, freeze configured duration/format on Academy attendance; subsequent group changes do not change frozen timing. |
| ROT-PLAN-04 | P1 | Focused algorithm verified | Circular balanced cycles using current attending children and format, with equal planned playing opportunity. Repeated cycles target maximum ten-minute rotation periods when substitutes exist; whole-second rounding distributed across complete cycles. Short-handed/no-reserve scenarios explicitly indicated. 1,200 parameter combinations passed after rounding correction; max observed difference one second. |
| ROT-STAT-05 | P1 | Coded | Existing stats submissions enforce actual recorded attendance and configured duration when attendance is present; no automatic statistics population. |
| ROT-VERIFY-06 | P0 | Not verified | Real PostgreSQL integration, API contract regeneration, frontend compilation, role-specific staging acceptance required. |
| ROT-LIVE-07 | P1 | Outstanding | Live timer, actual substitution entry/exit event log, handling injuries and late arrivals mid-game with elapsed-time-aware recalculation, persistence of individual actual playing intervals, same-day tournament fairness. Current computed plan is not a live clock or evidence of actual minutes. |

Implementation uses existing protected football territories API and mobile match workspace API. No new public routes. Existing unconfigured groups remain explicitly unconfigured. Old completed matches retain unknown timing rather than inheriting a new value. The suggested on-field order preserves present planned starters first and excludes absent players; it does not automatically rewrite actual historical starting selections. Group settings edit is manager/super-admin only, with existing territory checks; coaches confirm attendance. Proposed intervals are not enforced substitution rules.

Commits: migration 9ed6f83/82a05b7/d805d31; admin API d58a143/31856c2; DTO and store e8cb2cf/c6e372a; grants 555c968/5cb4d84; planner 73dda41/18baac2/784bdb3; mobile validation a7023b2; group UI 67fd7f6/8a06087; mobile UI 3df08df; contract 5e89d8f/9523087; tests aea65b9/1795787/8b6d6f8/64f195e/a1bdf4b.

Focused execution: fetched committed TypeScript, removed annotations, ran deterministic JS invariants over formats 5/7/9/11, attendance 1–60, and durations 1/37/45/90/180 minutes. First pass failed 4 rounding spread checks; adjusted distribution and reran 1,200/1,200 passed, max spread one second. Actual Svelte compilation, SQL integration and generated OpenAPI comparison are pending CI. No staging/production modifications.
