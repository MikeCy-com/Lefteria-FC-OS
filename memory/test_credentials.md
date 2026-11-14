# Test credentials — Lefteria FC OS preview workspace

All phones are made-up fixture numbers (`+999…`) generated once by the repo's own
preview tooling and persisted in `/app/.runtime/state/demo-state.json`. They stay the
same across restarts. No SMS is ever sent.

## Mobile app (`/app/login`)

| Role | Phone | Code |
|---|---|---|
| Player / fan (role picker: Παίκτης, Φίλαθλος) | `+999508474740001` | `123456` |
| Parent (3 linked children: 1 FC, 2 in different Academy groups) | `+999508474740002` | `123456` |
| Coach (management role DEFERRED, see below) | `+999508474740003` | `123456` |

- Calendar fixture month: **October 2026** (`/app?tab=calendar`). FC fixture/event on Oct 12,
  Academy events on Oct 20, staff-only draft on Oct 21, published training Oct 30,
  December 1 has 201 events (pagination).
- Wrong code (e.g. `000000`) must fail with the app's safe error.
- Non-demo phones (e.g. `+35799000000`) get the existing safe "delivery unavailable" failure.
- Management role for `+999508474740003` is added only after the owner account exists:
  `bash /app/scripts/workspace/seed-demo-management.sh` (refuses before owner setup).

## Admin (`/admin/login`)

**Admin owner setup is one-time and belongs to the user. Testers must NOT complete it. They may only submit a WRONG setup code.**

- Owner setup is rate limited; submit a wrong code at most once or twice.
- No admin test account exists. Signing in with any made-up username/password must show a safe error.
