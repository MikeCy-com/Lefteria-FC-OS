# Extended source parity: Hub, audience subscriptions, marketing and CMS

Date: 2026-11-10. Original source reviewed read-only: `MikeCy-com/Lefteria-FC`, particularly `frontend/src/mobile/pages/EducationHubPage.jsx`, `frontend/src/pages/admin/DrillsTab.jsx`, `MarketingTab.jsx`, `backend/routes/seo.py`, and mobile role/profile entry points. Implement only in Lefteria-FC-OS.

## Delivered in this PR — user-visible Hub entry only

- `/app?tab=hub` is present in **parent, player and fan** allowed navigation, including More. Parent home shortcut opens Hub. Legacy parent `education` tab still resolves.
- Existing first-party, age-stage educational cards have audience selectors for **parent** and **player** content. This is NOT club-approved material or a paid publication system.
- Fan sees an explicit **preview**, chooses the interest category locally, and is **not** assigned the parent/player account role. No subscription is created, no delivery preference is persisted, no charge is issued, and no child data is fetched.
- Coach/management are unchanged. All private backend role and organization checks remain unchanged.

## Remaining build requirements (not implemented by this PR)

| Product area | Required backend and admin / app workflow |
| --- | --- |
| Hub content | Club-reviewed image, video, article, course and drill content; drafts, versioning, moderated publication, safe player-age segmentation, protected media, taxonomy and accessibility |
| Hub fan subscriptions | Fan identity with independently verified contact; optional **parent-interest**, **player-interest** or both, without granting guardian/player permissions; explicit consent to notification channels, unsubscribe/revoke, per-user delivery prefs and age-appropriate access. Subscription entitlement separate from account role |
| Hub monetization | Only if approved: plan catalog, EUR-cent pricing, Payabl sandbox checkout, webhook verification, cancellation, consumer-rights and refunds; no automatic charges on a content filter click |
| Marketing and fan controls | Organization-scoped campaigns, fan audience opt-in, privacy-safe analytics, fan-only public engagement, targeted delivery, moderation, audit and anti-abuse protections |
| Drill library | Coach/manager authored age-appropriate sessions, diagram/video assets, search/filter, assignment to scoped teams, revision history, licensing and moderation |
| Video analysis | Secure video upload/transcode/retention, consent for identifiable youth, restricted clip annotations and staff/parent/player visibility rules; no public youth video by default |
| Profiles | Separate first-team, academy/player, parent, fan and coach profiles. Independent edit permissions, visibility and age/guardian consent; never infer a role from an interest checkbox |
| Roles | Central identity/organization/group grants, manager-controlled grant/revoke, immutable audit, OTP proof and negative authorization tests; mobile role switching remains independent of Hub interests |
| CMS drag and drop | Schema-driven reusable block editor with save/draft/preview/publish, per-page content ownership, sanitization, revisions, optimistic concurrency, responsive breakpoints and publish audit. No arbitrary executable HTML |
| Page SEO | Unique page titles, descriptions, canonical URLs, Open Graph image, robots and sitemap settings, redirects, structured metadata, preview and validation per locale/page/organization |
| Google Business Profile | OAuth account connect, scoped location ownership, change review and API eligibility verification, limited edits/posts per actual Google API support, token encryption, revocation and full audit. Do not promise unsupported Google controls |
| AI agent | Deliberately deferred for an owner-led design discussion. No autonomous permission changes, publication, payment or messaging enabled |

## Acceptance order

1. Verify source feature routes/fields, enumerate authority across fan/parent/player/coach and admin, and reconcile old data.
2. Implement reviewed Hub content APIs and private admin publication first. The present educational cards are static and not a server-sourced CMS.
3. Add opt-in subscription and audience preference persistence; prove revocation, role separation, no cross-family access and provider-delivery consent. Only then expose Subscribe.
4. Implement drill/video authoring and carefully scoped profile administration, then marketing.
5. Build page-by-page WYSIWYG CMS and SEO contracts behind role-checked APIs, then integrate Google Business Profile after OAuth/API review.
6. Execute SvelteKit, .NET/PostgreSQL, contract, privacy, keyboard and browser tests on the exact PR head. Keep draft without executed CI. No production deployment.

The original repository and live infrastructure remain untouched.
