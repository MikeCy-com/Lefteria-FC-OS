# Education AI worker — isolated preview activation gate

**Status:** source implementation only, disabled by default. Production must not be changed. The source repository `MikeCy-com/Lefteria-FC` remains reference-only.

## How the agent operates

- Editors submit structured briefs using the existing authenticated /api/admin/hub/ai/education-briefs API. A brief has a single audience (parent OR player), age range 6–18, age-appropriate topic and language (Greek or English). It contains no child identities, profiles, phone numbers or coach observations.
- The worker claims one private brief at a time with a 90-second lease and skip-locked transaction. A worker crash cannot reuse the old lease. Maximum three attempts per brief, **10 claims per club per UTC database day and 20 claims globally per database day**. Claim counts include retries, intentionally bounding provider spending.
- The OpenAI Responses API receives only the structured, non-personal topic/age/language brief with a versioned educational prompt. No tools or retrieved personal records are available. The response format is constrained to title/body. Max 1,200 output tokens and a 25-second request timeout. A separate moderation API call must succeed and explicitly return unflagged.
- The model output is converted to plain text, checked for length and markup, and only then committed as a private `provenance='ai'`, `status='ai_draft'` Hub article with **pending** text and visual reviews and a recorded model/prompt version. No view, member feed, push, chat or public publishing path is created. An administrator must review the actual text and any visuals through separately audited approval operations.
- On provider failures, the lease is failed/retried with bounded attempts. Moderation blocks and invalid outputs end in failed state without drafts. System logs never include provider keys, player details, prompt content or generated text.
- This does **not** generate personalized player feedback. Coach messages require a verified coach/player relationship, explicit coach-authored observation and final coach signoff. PR #180 provides the private, unsent foundation only.

## Explicit preview setup — **do not run against Hostinger production**

1. Verify a separate Hostinger preview hostname, Compose project name, network, environment file, PostgreSQL database and volume; ensure no shared mounts, production DNS or production credentials. Do not run any migration on the live database.
2. Provision a **separate unprivileged PostgreSQL LOGIN role** only in the isolated preview database. Its database and schema rights must not include direct Hub table access, admin role membership, DDL or session management. Revoke database TEMP and CREATE; use TLS for remote PostgreSQL.
3. After applying the migration to the preview database with an approved dedicated migration user, execute `ops/postgres/hub-ai-worker-grants.sql` as an authorized operator, passing `-v ai_worker_role=<worker role name>`. The script refuses privileged roles and checks effective table/function privileges.
4. Provide these variables **only** to the isolated preview API, via Hostinger secret configuration—not in Git, logs, frontend variables or Compose source:
   - `LEFTERIA_AI_ENABLED=true`
   - `LEFTERIA_AI_API_KEY` — dedicated API project key with budget caps
   - `LEFTERIA_AI_MODEL` — reviewed Responses API model identifier, explicitly chosen for the task
   - `LEFTERIA_AI_WORKER_CONNECTION` — connection string for the **separate unprivileged AI worker role**, not the web-admin, app or migration role
5. Start with one non-personal test brief per audience. Verify no outputs reach user APIs, no outgoing push/chat message, all records remain `ai_draft` and `pending`, and moderation/lease failures do not publish anything.
6. To stop spending instantly, set `LEFTERIA_AI_ENABLED=false` in the **preview** environment and restart that isolated service. Database queue remains intact.

## Pending before production readiness

Trusted editorial text/visual approval and publishing APIs, authoritative audience/age-scoped mobile reads, coach sign-off/delivery, explicit provider cost agreement and telemetry, privacy/data-protection review, robust structured-output and moderation integration tests against provider sandbox/mocks, and an independently tested Hostinger staging deployment and rollback plan.

**No production Hostinger environment is connected or modified by this PR.**
