# HUB · Player Football Classrooms · first verified progress slice

**2026-11-15 — isolated staging only; not live production.**

## What the player sees

From their existing **HUB** tab, a verified player now sees **Η ποδοσφαιρική μου τάξη** above the next approved lesson, individual weekly quiz, and verified coach feedback. The classroom has a personal summary (approved/finished lessons, a link to the *single currently allowed* next lesson; elapsed quiz weeks and a link to the independent weekly-quiz page) and eleven visually grouped football skills:

- Positioning; defending; attacking; transitions; passing; movement;
- Teamwork; rules; decision-making; goalkeeping; football fundamentals.

These headings and short educational descriptions are **navigation and curriculum taxonomy only**. They are not claims that corresponding lessons have already been approved or published. A skill shows *no approved quiz* until a separately reviewed, published quiz exists for that topic and verified position. Player marks remain private; no leaderboard, rank, full answer key, health information, parent data or sibling records.

## Security and source of truth

- Read-only `hub.read_mobile_player_classrooms(identity,phone,role,expires,organization)` is a versioned, RLS-protected PostgreSQL `SECURITY DEFINER` function. It invokes the **existing** authoritative `hub.assert_current_quiz_player`, `hub.current_mobile_education_lesson`, and `hub.read_mobile_player_weekly_quiz` functions. This validates active mobile identity, canonical roster/phone, exact First Team/Academy owner and unexpired session. No player, parent or coach can request an arbitrary position.
- The only new mobile privilege is `EXECUTE` on the dedicated function. PUBLIC and direct table reads remain forbidden. No new database write permission.
- Lesson numbers use the **same current latest revision, approval, publication, age-unrestricted, reviewed-media** eligibility gates as the personal journey. A completion counts only when the current published revision was actually completed by the currently authenticated player.
- Quiz topic progress includes only reviewed, published 3–20-question quizzes for the player's **verified** canonical position; the same specialized-over-general precedence as the existing weekly quiz reader. Correct/total are personal aggregate marks, without exposing individual answers, question texts or unpublished quiz titles.
- A quiz attempt for a withdrawn quiz stays in the private historic learner record, but withdrawn material is no longer advertised as approved curriculum. `completed_weeks` represents the individual's durable week clock, not a count of currently published topic cards.
- When the role/organization/session changes during a request, the app rejects the response and clears the old classroom state. Nothing is cached in localStorage.
- The new `GET /api/mobile/hub/player-classrooms?organization_id=...` is protected by the existing JWT policy and separately audited as an exact hidden Mobile route at the preview edge. It is deliberately not added to the frozen public OpenAPI contract until formal API versioning/consumer approval; no prefix wildcard is enabled.

## Delivery and verification

- Existing approved lesson editorial and quiz admin approval stay unchanged. **There are no automatically generated or auto-published lessons, questions or scores** and no change to learner-specific Week 1 or seven-day notification cadence.
- A new rollback-only, synthetic PostgreSQL suite verifies first week, publication state, individual completion, player-specific marks, withdrawn content, parent/foreign owner/unlinked player/forged phone/deactivated player denial, and no leaked answer text.
- Node tests compile the Svelte player screen and verify strict payload validation, routes, private ingress, permissions and actual CI wiring.
- Both CI Rewrite Build and Browser UI visual audit must pass at the exact PR head before merge.
- No changes to Hostinger live, real Payabl, staff permissions, financial ledgers, or existing reference repository.

## Next classroom milestones

The classroom overview is the secure foundation, **not** a completed learning management system. To reach full classrooms: add a human-curated module-to-published-lesson/quiz curriculum map; approved videos and positional drills with verified assets; per-player position and age-banded eligibility only after canonical verification; teacher assignments and instructor feedback; mastery and retry policy; accessibility checks with young players and guardians; administrative cohorts and progress analytics that never disclose children across Academy locations. These require separately reviewed workflows and no direct learner bypass of the personal publication sequence.
