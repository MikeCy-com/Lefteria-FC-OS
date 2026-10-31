# Public website and web-admin verification — 2026-11-01

This checkpoint describes the current uncommitted public/admin slice built
alongside rewrite main `6c25032` (separate Hostinger preview publication). It does
not record a commit, merge or deployment of this new slice. The coordinator must
record its release and hosted verification before it is described as deployed.

The [public content/import guide](public-content.md) and
[web-admin source contract](web-admin-source-contract.md) define the slice.
Public/admin behavior was inspected directly in original GitHub source at
`380b4022f0ab0db95fbc52a8613711160a5037a5`. The protected source backup and initial
roster/staff/account/calendar imports use baseline
`a52d66b8e01c010cfe95ce53fa139e126bae4d84`. The later source inspection does not
change that backup provenance or prove the copied data contains later changes.

## Implemented source

- Greek public homepage, news list/article, first-team overview/schedule/results,
  Academy overview/fixtures, club history and recorded contact pages. Original
  crest and Sideline artwork are retained byte-for-byte. Unavailable/unimported
  content is distinct from a complete empty result; no operational records or
  statistics are invented.
- Separate public content readiness, allowlisted PostgreSQL projections and an
  explicit initial owner import with exact private source/manifest retention,
  validation, replay reconciliation and runtime evidence/write denial. There
  is no startup import or source production API proxy.
- Default public news list/article reads aggregate FC and Academy. Shared logical
  IDs appear once, newest first; explicit known-organization filters retain their
  scope. The homepage requests global news separately from FC site data. Shared
  navigation reads Academy groups through an explicit separate public request.
  News scope, source categories and pagination are preserved in links.
- SSR uses the generated OpenAPI client with a private internal API origin and
  omits cookies/Authorization. Source Markdown is escaped, with bounded safe
  formatting and links; whole source objects/private uploads are not exposed.
- Web-admin login/session/logout and a permission-aware workspace, separate from
  mobile authentication and source JWTs. Supported imported bcrypt credentials,
  current roles/grants, strict same-origin/custom-header/JSON mutation boundaries,
  durable throttles, independent restricted credential access, revocable opaque
  Secure/HttpOnly cookie sessions and fresh authority checks are implemented.
  The workspace has no CMS business-write editor in this slice.

## Observed checks

The coordinator observed the following runs, including the final global-news
parity and opaque article-ID regression fixes.

| Check | Observed result |
| --- | --- |
| Complete HTTP/PostgreSQL executable | 44/44 core groups passed with zero failures after global-news parity additions. Includes public import/privacy/readiness/runtime grants plus existing mobile/roster/staff/account/calendar checks. |
| Web-admin HTTP/PostgreSQL groups | 7/7 passed: credential failures, cookie lifetimes, mobile/admin realm isolation, strict request boundary, current authority, cookie invalidation and durable throttles. |
| Domain policy executable | 31/31 passed, including the source CMS role/capability matrix. |
| Cross-language password checks | 18 frozen Python bcrypt4.1.3 vectors passed across 2a/2b/2y, with wrong candidates and byte-truncation boundaries; malformed/unsupported/excessive-cost rejection and bounded concurrency checks also passed. Uses fictional credentials only. |
| Web-admin configuration checks | 25 cases passed without database/provider connections, including distinct effective mobile/admin credentials, independent decoded keys and strict public-origin validation. |
| Public API/client contract | Release API build passed with zero warnings/errors; controlled OpenAPI export and TypeScript generation passed after news parity changes. |
| Public frontend regression checks | 4/4 passed after the parity change, including actual loopback generated-client calls, global Academy article rendering, separate Academy navigation, scope/category/page links and omitted credentials; safe Markdown/text rendering remains covered. |
| Svelte/TypeScript check | Passed after the parity change with zero errors and zero warnings. |
| Real-browser admin session/logout | Fictional local browser login, actual cookie-backed session and logout passed. This is not a hosted imported-account or real SMS/2FA journey. |
| Frontend suite and production web | 107/107 frontend checks passed; six focused public/admin checks passed again after the opaque-ID fix. Production build and production-only dependency smoke passed. |
| Published license notices | API and owner import publishes both retain byte-exact BCrypt and Unicode notices. |
| Migration and SQL boundaries | Empty down/up, no pending model changes, populated forward-recovery refusal and public JSON NULL/missing-field rejection passed. Restricted mobile/admin role probes passed against actual PostgreSQL. |

The HTTP fixture uses fictional records and guarded disposable PostgreSQL. The
loopback browser/test host has test-only identities; no fixed OTP or provider
bypass is deployed. Tests neither send real SMS nor authorize a real user's
access to imported private records. Loopback network access was required for
the actual SSR client test; its first sandboxed attempt was denied with EACCES,
and the approved isolated rerun passed all four public frontend tests.

## Existing hosted preview versus this release

The [2026-10-31 preview record](isolated-preview.md) already documents a separate
`lefteria-os-preview` Compose project, its TLS/access gate, independent database
and secrets, verified source backup/restore, four protected manifest imports and
same-snapshot replay. It identifies application images at
`3b0a8dcb2c0b5d66cec95013e863efdedcd0e796`; subsequent preview/edge fixes do not
establish deployment of this new public/admin source.

Copied roster/staff/accounts and all 783 calendar source rows were retained.
Two RSVP rows still reference a missing source event: quarantine remains and
calendar readiness is false. Account quarantine is also retained. The verified
171-file media restore remains private and is not mounted as a public upload
directory. The initial restore/import evidence is completed work, not an
unperformed blanket prerequisite; unresolved records and later workflow/cutover
verification remain outstanding.

Twilio setup is explicitly paused. The hosted mobile SMS adapter fails closed,
and this admin source does not downgrade accounts requiring SMS 2FA to
password-only sessions. Real imported-user SMS/2FA login is unverified. No new
provider account setup, SMS delivery or number transfer is implied.

The live original application and `lefteriafc.cy` remain authoritative. This
slice does not switch the domain, replace the live database or publish private
backups/media. It still needs GitHub CI, release recording and isolated hosted
verification.
CMS business edits, other operational modules, quarantine resolution and later
legacy/PWA/cutover acceptance remain separate work.
