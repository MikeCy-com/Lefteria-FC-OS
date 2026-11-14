# Live public-content mirror (preview only)

Scripts: `scripts/workspace/live_mirror.py` (fetch), `scripts/workspace/mirror_manifests.py` (manifests),
`scripts/workspace/import-live-mirror.sh` (one command: fetch → manifests → import). Raw responses, media and
manifests stay in `/app/.runtime/live-mirror/` (git-ignored). This file holds counts only, never mirrored content.

- Requests: GET only, no login, paced to at most 2 per second, every request logged to `requests.log`.
- Endpoints: the original public GET operations from `api-matrix.json` (no path parameters, no admin, no share/OG/export/webhook routes), plus detail GETs for news, players, staff, partners and events.
- Import: the repository's own `PublicContentImportValidator` / `PostgresPublicContentImport` and
  `PublicSectionsImportValidator` / `PostgresPublicSectionsImport`, called from the tool layer
  (`--workspace import-public`). It replaces only the workspace's fictional two-document public snapshot and
  refuses to run if any other public snapshot is present. No `backend/src` changes, no admin records, demo
  identities and the open owner setup are untouched.

## Counts (live → OS public API after import)

| Content | Live API | OS public API | Gap |
|---|---|---|---|
| News | 11 | 11 (`/api/public/news`) | none |
| Players | 33 | 15 (`/api/public/sections`) | OS drops the 18 players that have no `team_id` (academy / unassigned); live shows them |
| Academy groups | 6 | 0 | OS public sections returns no academy groups from the imported rows |
| Partners | 3 | 2 | one partner is filtered by OS publication rules |
| Fixtures | 13 | 13 | none |
| Standings / venues / seasons / sponsors / products / tickets / archived seasons | 4 / 1 / 2 / 1 / 6 / 1 / 1 | 4 / 1 / 2 / 1 / 6 / 1 / 1 | none |
| Media files mirrored | 83 found | not served | OS has no public media storage for imported images; cards show the club placeholder |

Import validation: 32 public-content documents, 73 public-sections source rows, 0 skipped or issue rows.

## Matrix gaps found by the mirror

- Public media storage/serving for imported images (Phase 3.3).
- Unassigned/academy players in public squad output (Phase 3.2).
- Academy groups in public sections output (Phase 3.2).
- Live public GETs without an OS equivalent were saved raw but have no import target: see `missing` rows for
  `posts`, `videos`, `reels`, `votes`, `predictions`, `reviews`, `loyalty`, `settings` in `api-matrix.md`.
