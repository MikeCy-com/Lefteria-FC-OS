# HUB: verified player learning specialties

**Status: staging-only source change; no production deployment or automatic publishing.**

## The problem

The canonical imported roster accepts exactly four broad positions: `Goalkeeper`, `Defender`, `Midfielder`, and `Forward`. The weekly football quiz catalogue already permits `fullback`, `winger`, and `striker` positions, but previously no trusted roster evidence could distinguish them. Automatically interpreting every `Forward` as `striker` would misclassify wingers.

## Implemented workflow

At **Administration → HUB → Weekly player quizzes**, a **Super Admin** with an organization grant can select a player from the already authorized roster directory, read their current canonical position and version, and provide an independent reason when verifying a more specific learning position. The setter accepts only:

| Canonical imported position | Optional supervised learning specialization |
|---|---|
| Goalkeeper | goalkeeper |
| Defender | defender or fullback |
| Midfielder | midfielder or winger |
| Forward | winger or striker |

Leaving specialization unset uses the verified canonical defaults (goalkeeper, defender, midfielder), with broad Forward remaining `all` until a supervised specialization is recorded. The editor may revoke or reset a specialization without changing `club.players.position`.

All updates are compared against both the current canonical roster label and the last approved version. The writer refuses stale edits, incompatible role assignments, inactive/transferred players, cross-organization references, revoked sessions, and managers/non-authorized Super Admins. A private immutable event is appended for each real change; no history is deleted by resetting a role.

The existing player-only `hub.assert_current_quiz_player` checks live mobile phone identity, canonical player binding, untransferred player record and exact organization on every read. It uses a specialization **only while its stored canonical roster label still matches the current canonical record**. Otherwise the older label immediately stops being effective and the administrator must verify again. Changing a specialization does not reset previously completed weeks, attempt history or approved lesson progress.

There are no new mobile database grants, no raw table grants to web admins, no role chosen by the player or their parent, and no automatic new content publication, billed payment or notification.

## Validation / release gates

- Disposable `ops/postgres/hub-player-learning-position-checks.sql`: Week 1 winger quiz; Defender→Fullback, Forward→Winger, incompatible assignment denial; manager, foreign organization, stale version, stale canonical roster, revoked session; supervisor audit and safe reversal.
- Frontend test compiles the Svelte 5 editor and verifies exact token-scoped API ingress, versioned write request and role visibility.
- Restricted web admin SQL grants are verified by `ops/postgres/web-admin-auth-permissions-check.sql` and direct raw source table reads remain denied.
- Exact PR head must pass Rewrite Build, PostgreSQL, Docker and Browser UI visual audit before merging; separately decide whether to deploy to staging.

## Next LMS slice

Add approved course curriculum mapping with supervised module-to-lesson sequencing and age/position policy only after canonical birth date/eligibility can be proven; do not infer children's ages from user-supplied selectors. Expand educator-led assessments and measured progress, keeping the Week 1 personal start, separate audience/organization ownership, and independent human editorial approval.
