# HUB AI-generated content and fan subscription — implementation boundary

**Date:** 2026-11-10. **Branch:** `feat/hub-approval-subscription-gates-2026-11-10` (stacked on PR #158 → PR #157). Development only; no deployments or provider charges.

## Content and visuals: AI proposes, administrator approves

1. An authorized editor chooses organization, age range, parent/player target audience, topics, tone, language, imagery needs, and sources. The AI generates **suggested copy and image requests/assets as DRAFTS**, never directly published pages. Include AI provenance, prompt/version fingerprint and moderation status.
2. Stage the text and actual generated image files in **quarantine/private storage**; record immutable media IDs, content hashes, licenses/provenance, alt text, dimensions and a safe preview. Disallow arbitrary HTML/scripts, external hot-linked private photos, real minor likenesses without explicit appropriate consent, and secrets in prompts.
3. Editorial actions: `ai_draft → review_required → approved → published`. Require authenticated, fresh organization-scoped admin permissions, **separate text and visual review**, audit and optimistic DB revision checks. Any edit after approval resets both checks. Rejected drafts cannot appear in the app, website or member APIs.
4. The AI is **not** an administrator and cannot approve, publish, change its own permissions, charge customers, or send messages. Approval must be an explicit user action after reviewing final text and **all actual visuals**, including captions and rights.
5. Public/Hub readers receive only the approved, published, audience-eligible revision through a server-side API. AI jobs, approval/publish and audience lookup must be PostgreSQL transactional and audit logged. The pure TypeScript policy introduced in this PR documents expected state transitions; it is **not the API authorization layer**.

## Fan HUB is a separate subscription

- Fans retain the `fan` role. Fan selection `parent`, `player`, or both means **content interests**, **not a parent/guardian or athlete identity**, and provides no permission to see any child's records, invoices, files or group messages.
- Fan article bodies are **currently hidden** behind a truthful subscription-required screen; no subscribe button is shown until the actual signup and authenticated entitlement API exists.
- Persist fan interest choices only after verified identity and consent. A subscriber record needs customer ID, plan/price in EUR cents, status, provider ID, audience entitlements, verified period end, receipt/consent references, cancellations and revocations.
- If paid, Payabl **sandbox** settlement/capture/webhook evidence, idempotent expiry/cancel/refund processing, product eligibility and exact organization scope are prerequisites for `active` entitlement. An HPP redirect or selected content preference does not confer access.
- Every **API read** validates status `active`, provider confirmation, user binding, requested audience and expiration. A frontend selector or local TypeScript check must never grant paid article access.
- Payment handling must remain age-appropriate and require proper authorization for any underage account; fan subscribers cannot access youth private media.

## What's in this draft PR

- Fail-closed **fan content gating UI** and an accurate subscription notice.
- Separate pure policy for the proposed editorial transition and provider-verified fan entitlement.
- Source-based regression tests for AI cannot self-approve, text and visual review required, stale revision conflicts, role separation, expired/revoked/unverified entitlements.
- Planned admin navigation entries **not advertised as live**.

## Next API and database implementation

- Create EF Core Hub draft, revision, media, moderation and audit tables, plus endpoints for upload/generation request, editor review, text review, visual review, admin approval and publish; authorize org and record on each call.
- Add a provider-controlled background job for AI-generated text and images, with safe input filtering, moderation, retry limits, model/cost limits and staging. No autonomous publish.
- Add fan subscription+preferences tables, Payabl sandbox product/checkout/verified webhooks, revoked/expired reads, and optional notification opt-in. Use backend-only entitlement checks for fan Hub data.
- After these endpoints pass SQL/role/privacy integration tests, replace static education content and the fan locked state with approved CMS content and real entitlement.
- Run exact-head TypeScript/.NET/PostgreSQL/browser CI; PR remains draft until verified.

**No real AI generation, stored image production, subscription creation or payment processing occurs in this slice.**
