# Public SEO route parity and index-safety policy

**Source reference:** `MikeCy-com/Lefteria-FC` at `91eafcc9efd01ad177b4f13a869cd5af7041e5b2`, `backend/routes/seo.py`.
**OS implementation:** SvelteKit `/robots.txt` and `/sitemap.xml` server routes; typed public-projection aggregation only. The source repository is read-only.

## Public versus isolated preview

- Only HTTPS `https://lefteriafc.cy` and `https://www.lefteriafc.cy` are recognized as indexable origins, and canonical sitemap links always use `https://lefteriafc.cy`.
- On an unrelated preview, local host, insecure HTTP host, or nonstandard port, `robots.txt` replies with `Disallow: /` and non-cacheable noindex headers. `sitemap.xml` responds 404 with noindex, not with a list of production content.
- This policy depends on the **trusted SvelteKit event URL**, which production Docker derives from its configured `ORIGIN`. Never use arbitrary user-supplied Host or forwarded-host values as sitemap authority. Verify Caddy/Hostinger routing and the real production domain before deploying.
- Robots directives are **not authentication or authorization**. Keep previews access-gated and private records protected independently; merely excluding a URL from indexing does not prevent an attacker from visiting it.

## Contents and privacy

- Include only currently implemented public OS paths (not pending shop, registration, checkout or polling workflows).
- Include public news with bounded pagination (first 500 published records from the safe, anonymous public news endpoint).
- Include permitted Academy **group** detail pages, currently visible public fixtures with unambiguous IDs, and published events/partner/sponsor/season details.
- Only index **First Team** players with an explicit age of at least 18, and first-team-scoped public staff records. No Academy youth player profile or player with unknown age enters the sitemap even if a public DTO exists.
- All data comes through the same generated, anonymous, credential-free `/api/public/site`, `/api/public/news`, and `/api/public/sections` projections already used by the OS website. No direct CMS/MongoDB/production database access.
- On missing/invalid public projections, return a non-cacheable 503 instead of silently publishing an incomplete or empty sitemap. The sitemap contains only public IDs and safe URL slugs, never family records, phone numbers, protected account identities or entire article objects.
- Generated sitemap items are unique, canonical, sorted, XML escaped and guarded against malformed opaque IDs.

## Verification and release gates

The frontend test suite covers strict domain gating, crawler policy, source parity, XML, disallowed player records and ID collision handling. SvelteKit typechecking/build and existing disposable Docker CI must pass before merging.

Hosted validation still required before **production deployment**:

1. Check `https://lefteriafc.cy/robots.txt` and `/sitemap.xml` under the exact running revision; verify 200 content types and canonical links.
2. On the isolated preview, assert `Disallow: /`, no private leaks, and `/sitemap.xml` status 404. Do not remove preview authentication.
3. Verify all listed routes resolve publicly with the correct organization scope; check that no youth/unknown-age profile is listed.
4. Verify the expected public projections are fully imported, otherwise sitemap intentionally remains 503.
5. Review canonical, Open Graph, Twitter card and structured-data coverage separately; this PR is **only** robots/sitemap parity.
6. Confirm production's canonical hostname choice; changes to the public domain require an explicit reviewed update.

**Status:** Code and tests only. No hosted deployment, search-console submission, site-crawl validation, or automatic indexing request is made by this PR.
