# HUB Classroom Curriculum — reviewed lesson subjects

**2026-11-21, isolated source change only. No Hostinger production/staging deployment, real Payabl, external messages or live player records.**

## Purpose

The football classrooms previously showed per-topic approved quiz progress while lessons appeared only in a separate personal journey. An approved football subject assignment now connects a *previously human-reviewed and published* lesson to one classroom topic. The topic is educational metadata, **not** a new content approval, billing entitlement, push schedule or route to skip the next lesson.

## Editorial boundary

Only an **active Super Admin explicitly granted to the exact organization** can read or classify lessons via `/api/admin/hub/classroom-curriculum`. The visible editor is in the existing HUB editorial workspace, scoped by organization; the SQL rechecks the authority even if the client or API is bypassed.

The category selector lists only the *latest* revision of `article` or `course` content that has player audience, published status, reviewer approval, text/visual reviews passed, no age band, and no non-reviewed or non-image media. Parent-only, draft, withdrawn, future, age-ambiguous and other-club lessons never appear in the selectable catalogue.

Each classification requires the exact content ID, latest revision ID, expected mapping version, one of the 11 existing football topics (or an explicit unassign), and a human reason. Successful changes increment a version and append a private event and security audit. If the editorial revision changes later, the subject becomes **stale immediately** and the learner no longer sees the topic assignment until a Super Admin reviews the new revision. The old event remains available for authorized audit.

## Learner behavior

Each verified player sees, for each football subject:
- Count of currently eligible, published and classified lessons.
- Count of those same **current revisions** that **this specific learner** has actually completed.
- Approved quiz count, completed quizzes and their own marks.

The classroom never returns the full lesson body or an answer key, and a subject card is **not an entitlement to open a future lesson**. The same existing `hub.current_mobile_education_lesson` determines the next lesson by its publication order and the same existing `hub.complete_mobile_next_education` handles explicit sequential completion. The existing personal Week 1 quiz start is not reset or changed. Academy and First Team data are isolated; parent and fan audiences cannot read player classrooms.

## Tests and release gate

- `ops/postgres/hub-classroom-curriculum-checks.sql` is synthetic and `BEGIN/ROLLBACK`. It covers the published-only admin catalogue, protected roles, parent-only exclusion, private completion, changed/reviewed revisions, stale version conflict, withdrawn content and session revocation.
- `frontend/tests/hub-classroom-curriculum.test.mjs` checks reviewed edge GET/PUT, Super Admin-only Svelte mount, output validation and both frontend Svelte compilation paths.
- Explicit SQL grants allow only narrow admin functions; no raw table read/write privileges are granted to the admin or mobile runtime.
- Merge only after exact-head full Rewrite Build (ASP.NET Core, web, contract, Docker, PostgreSQL identity) and Browser UI Visual Audit succeed.

## Not yet implemented

This is **topic classification of existing approved lessons**, not a full learning management system. Separate future slices must cover supervised curriculum unit sequencing, trusted child age eligibility, content-specific position prerequisites, vetted drill/video playback, instructor cohort assignments and per-location anonymized analytics. Those changes must preserve human editorial approval and each learner's independent content progression.
