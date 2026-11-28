# SEO, performance and 500-concurrent-user release tracker

Source baseline: `1776e62c9b39d6938dea57d310b80469875941e9` (main, Rewrite build succeeded, hosted revision not confirmed).
Original SEO reference: MikeCy-com/Lefteria-FC at 91eafcc9efd01ad177b4f13a869cd5af7041e5b2, backend/routes/seo.py.

No release acceptance has passed. This change introduces instrumentation only; do not optimize or load-test a live/shared system before baseline and isolation evidence. Existing published links and branding are unchanged.

| ID | Priority | Status | Dependency / measurable acceptance |
|---|---|---|---|
| BASE-01 | P0 | Coded, not run on staging | Verify exact deployed image SHA, private networks, synthetic data, server inventory and headroom. Collect sequential HTTP baseline, mobile/desktop Lighthouse, route timing, DB plans and resource consumption. |
| SEC-01 | P0 | Open | Audit private/app/admin/API cache and robots headers, server authorization including role switching, OTP throttling, registration, upload and messaging rate limits. No private data in shared caches; regression tests across roles. |
| PERF-01 | P0 | Open | ReadNewsAsync currently materializes all news and edit overlays; pagination/detail selection happens after deserialization. Measure query plans, then move filters, stable ordering, owner deduplication and paging to SQL without changing visibility or edit semantics. Compare before/after. |
| PERF-02 | P1 | Open | Audit public site and CMS repeated requests. Measure repeated DB counts and JSON reads, duplicate calls, pool saturation, payload and cache hit rates. Preserve publication freshness, isolation and fixture clock behavior. |
| SEO-01 | P1 | Open | Replace first-500-news sitemap truncation, limited fixture projection and CMS reader's silent empty-on-failure behavior with complete bounded public index projections. Include only published content, honor privacy, ensure sitemap size limits. |
| SEO-02 | P1 | Open | Versioned public slug registry with explicit per-type/org/language uniqueness and collision handling; permanent 301 aliases from old numeric/opaque/sluggified links; preserve IDs and mobile deep links; tests for renames, old URLs, duplicates, reserved paths, rollback. |
| SEO-03 | P1 | Open | Inventory every public route's SSR metadata, schema, semantic headings, image alt, breadcrumbs and internal links. Edit SEO fields with existing CMS authorization and audit trail. hreflang only for actual published equivalent translations. Zero private/youth profile index leaks. |
| WEB-01 | P1 | Open | Establish desktop/mobile Lighthouse baseline; analyze bundles, fonts, media formats/sizes, lazy loading, cache behavior, navigation and startup. Preserve design. LCP <=2.5s, INP <=200ms (field or measured interactions, not Lighthouse proxy), CLS <=0.1. |
| DB-01 | P1 | Open | Capture query stats and EXPLAIN plans on synthetic representative volume. Assess Npgsql pools, indexes, lock waits, slow queries, retries, queues and notification/messaging throughput. Migration and recovery tests mandatory. |
| OBS-01 | P1 | Open | Internal metrics and alerting for active sessions/connections, CPU/RAM, API latency/errors, database/pool/cache, storage/network, queue failures and availability. No personal data or secrets in labels or logs. |
| LOAD-01 | P0 release gate | Blocked | Dedicated isolated staging with matching resource envelope, synthetic accounts, no external SMS/payment/push/backup calls. k6/Locust mixed workload (guest, auth/role switch, feed, likes/comments, live match, messaging, academy attendance, media and admin). Warmup, baseline, 500 active VUs for 60 min, controlled spike to 1,000, recovery, then data integrity audit. Define think times, RPS, workload mix, abort conditions and role credentials before run. |
| REL-01 | P0 release gate | Blocked | Functional/security/CI, staging SHA, rollback rehearsal and complete evidence. No production modification or deployment without owner approval. |

## Baseline collector

Only the reviewed preview origin is accepted. Operator must independently verify staging isolation and exact deployed SHA (the script records the supplied SHA but cannot independently attest it). Run from an authorized client, not the live VPS:

```sh
python3 -B -m unittest discover -s scripts/performance -p 'test_*.py'
python3 scripts/performance/preview-baseline.py \
  --target https://srv1649602.hstgr.cloud:8443 \
  --deployed-sha <verified-40-character-sha> \
  --confirm-isolated-preview > baseline.json
```

The collector is intentionally restricted to 30 sequential GETs, no credentials or redirects, 1 second delay, abort on first invalid response. It checks status, basic expected payload and preview noindex/no-store headers; does not record response bodies or credentials. Public CMS and news must already be populated. Small sample p95 is descriptive only, not a statistical acceptance result. Store reports privately with timestamp, revision and environment; do not commit real response data.

## Source configuration observations (not actual server readings)

Preview limits: edge 0.10 CPU/64m, web 0.15/192m, API 0.35/512m, PostgreSQL 0.20/384m. Production defaults: proxy 0.5/128m, web 1/512m, API 1/768m, PostgreSQL 1/768m. Verify active limits, CPU/RAM/disk/network, production headroom, DB size and pool settings before proposing allocation. No load or infrastructure changes on the shared VPS until isolated capacity and abort procedures are verified.

## Release evidence table (all outstanding)

| Metric | Required | Actual |
|---|---|---|
| Concurrent active users | >=500 | Not measured |
| Sustained workload | >=60 min at 500 | Not measured |
| Spike | 1000 concurrent | Not measured |
| Normal API p95 | <500ms | Not measured |
| HTTP 5xx rate | <0.1% | Not measured |
| LCP / INP / CLS | <=2.5s / <=200ms / <=0.1 | Not measured |
| Data loss/corruption or crashes | Zero | Not verified |
| Broken public URLs | Zero | Not verified |

Record per-role and endpoint p95/error/rate, response sizes, max CPU/RAM, DB pool/locks/queries, disk and network, queue backlog, achieved VUs and dropped iterations, integrity assertions and post-spike recovery. Report maximum verified concurrency only for the tested workload and capacity; no extrapolated guarantee. Establish tolerable degradation versus baseline before testing. Do not equate 500 registered accounts or 500 idle connections with 500 active users.

## PERF-01 candidate: exact-ID article reads (release hold)

Draft implementation only. The article endpoint passes its existing ID to the public repository, which filters both the imported safe projection and editorial overlays in PostgreSQL before materialization. The readiness/count guard remains in the same repeatable-read transaction. Scoped lookup and global .NET ordinal owner precedence are unchanged, as are HTTP routes, response schemas and no-store policy. The public feed and admin list still use the original unbounded list implementation. Existing index use must be confirmed by EXPLAIN; no new index or performance improvement is claimed.

Existing #491 PostgreSQL integration tests protect scope and shared edited article precedence; new checks cover case-sensitive lookup, unknown/overlength IDs, literal metacharacters, 404 payload and no-store. Detail isolation intentionally no longer deserializes unrelated articles, so unrelated malformed JSON DTOs need not make a valid detail unavailable; the import count readiness check still applies. No schema/grants change.

Keep this candidate draft and out of main until actual authorized pre-change baseline evidence is recorded. Then require exact-head CI, representative synthetic-volume query plans, before/after comparison (including missing/scoped/shared detail), and approved isolated staging verification. Full SQL list pagination, index tuning and cross-endpoint consistency remain separate work. Hosted measurements are blocked by G0 / issue #446.
