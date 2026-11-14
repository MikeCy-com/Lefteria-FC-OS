# Public website and protected content import

Source behavior was checked against original GitHub main
`380b4022f0ab0db95fbc52a8613711160a5037a5`, including `memory/PRD.md` iteration 93,
`frontend/src/club-site/`, public route declarations, source models and handlers.

This slice implements the Greek public shell/homepage, news list/article,
first-team overview/schedule/results, Academy group overview/fixtures, club
history and recorded contact information. It preserves the source crest and
Sideline campaign artwork byte-for-byte, verified against GitHub blob hashes.
The public stylesheet is scoped; mobile and admin layouts are separate.

The source's remaining shop/customer/payment flows, voting, live-match detail,
standings, birthdays, sponsors/partners, galleries, trials/registration and
contact form submission are not implemented by this slice. It does not display
invented products, statistics, registrations or successful submissions.

## Public boundary

`GET /api/public/site` returns an explicit safe projection of club, team, Academy
group, news and fixture content. `GET /api/public/news` supports bounded paging
and exact category filtering; `GET /api/public/news/{id}` serves an article.
Only `lefteria_fc` and `lefteria_fc_academy` organization selectors are accepted.
The site endpoint defaults to FC. News list/article requests without a selector
read the global public feed across both organizations, matching the original
anonymous homepage and news URLs. Shared articles appear once by logical source
ID, ordered by creation time descending, before category filtering and paging.
Explicit news selectors keep their exact visibility filter and out-of-scope
articles return 404. No third organization is added. Public queries do not use CMS cookies, active admin context,
mobile roles or arbitrary browser organization values. Academy groups and
first-team teams remain distinct. Content marked `both` appears in both news
scopes; legacy Academy category classification is preserved.

No public response serializes a source club/player/staff document wholesale.
Club source objects may contain provider credentials and banking information;
only the declared public DTO fields enter `public_content.documents`. No player,
guardian, medical, squad/RSVP or admin credential fields enter these DTOs.

Public content has its own initial readiness state, independent of the private
calendar's quarantined RSVP rows. An unimported/unavailable snapshot returns
503, while a complete empty snapshot returns an honest empty result. A known
empty scope can show a Greek empty message; backend failures do not become empty
successful lists. Missing articles return 404 only after readiness is confirmed.

SSR uses the generated OpenAPI client and the server-only dynamic
`INTERNAL_API_ORIGIN` variable, defaulting to `http://api:8080`. Native server
fetch omits cookies and Authorization and never goes through the public preview
Basic gate. The variable is declared private through SvelteKit 3 `src/env.ts`.
The homepage requests global news separately from its FC club/team/fixture data.
Shared navigation obtains Academy groups through a separate explicit Academy
public request; FC site responses retain their own scoped group collection.
News URLs offer global, FC and Academy tabs, with the four original categories;
pagination and scoped article links retain the selected news scope/category.

Source news/history text is rendered through escaped Svelte nodes, with headings,
paragraphs, bullet lists, quotes, emphasis and safe links. Raw HTML is inert;
dangerous link protocols, private API links and credential-bearing URLs are not
rendered as links. Inline Markdown media/full Markdown extensions are outside
this first renderer. Article covers and other explicit image fields retain only
known public image paths or safe HTTPS URLs. Unsafe legacy media/contact links
are omitted from the public projection; their exact source remains retained.
Relative legacy media may show the unchanged crest fallback until approved
public files have separate verified delivery. The full private uploads restore
must never be mounted publicly as a shortcut.

## Explicit owner import

The `public-content` kind of `LefteriaFc.RosterImport` consumes a UTF-8 manifest
with `schema_version:1`, source repository/40-hex source commit, and
`complete_collections` exactly
`[club_profile,news,teams,academy_groups,fixtures]`, followed by the five arrays
of whole original source objects. Input is bounded to 32 MiB/depth 32 and each
collection to 20,000 rows. Duplicate JSON keys/IDs, invalid organizations and
invalid required field/date types fail before reading DB credentials.

Dry run validates and reports only counts/hash. Apply requires the exact manifest
SHA-256, explicit nonproduction database and `development`/`staging` assertion,
using the existing owner-target guard. Its shared transaction/advisory lock
atomically creates public projections, initial state, private original rows,
byte-exact manifest evidence and audit event. Startup does not import data.

Only the public documents and nonsensitive readiness metadata receive runtime
SELECT. `migration.public_content_source_documents` and
`migration.public_content_import_evidence` remain owner-only. The normal API does
not register owner-import connections or expose evidence endpoints.

Same-snapshot replay compares original bytes, exact source rows, public JSON
projections and counts; it refuses a different snapshot or modified projection
rather than overwriting later edits. The [club profile editor](club-profile-editor.md)
keeps versioned public overrides separate from that base. Public reads merge them
without changing the initial counts or private evidence. Replay still checks the
immutable initial snapshot.

## Integration and checks

The coordinator wires `PublicContentModel.ConfigurePublicContent(modelBuilder)`,
`services.AddPublicContent()` and `app.MapPublicContentEndpoints()`, generates EF
migrations/OpenAPI and provisions the public runtime SELECT boundary.

New integration hooks are `PublicContentPolicyAsync` and
`PublicContentEndToEndAsync` on the existing partial HTTP checks. They verify
source retention/public secret exclusion, strict validation, owner apply/replay,
public selector isolation from CMS cookies/JWTs, unavailable versus missing data,
runtime evidence/write denial and refusal of a changed target. The frontend has
dedicated actual-render and URL/Markdown safety checks.
