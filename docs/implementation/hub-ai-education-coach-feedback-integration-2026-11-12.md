# Lefteria FC OS — AI education agent and coach feedback authorization

This integration is **not** an autonomous content publisher or a coach impersonation system.

## Audiences

- **Parents** receive guidance intended for guardians: emotional support, age-related development, communication, wellbeing, and positive youth-sport parenting. The content is addressed to the parent rather than written as instructions to the child.
- **Players** receive child-friendly material targeted to their age and development: confidence, teamwork, resilience, learning through mistakes, and wellbeing. The tone should be encouraging, never punitive or comparative.
- An authenticated fan who selects "parent" or "player" as a *topic interest* is not granted a guardian or player identity. Paid fan entitlements and minor privacy boundaries remain separate.

## AI provider integration contract (next dependency)

1. Editors queue structured requests with one exact audience, 6–18 age bounds, one allowed topic and explicit language. No names, team rosters, contact details, diagnostics, performance statistics, private coach notes or free-form prompts are accepted in this first request interface.
2. A separate worker must be granted only explicitly approved queue claim/update functions, with server-side provider credentials, per-organization quota, per-job/token cost limits, safety screening, bounded retries, model identification, prompt-template version, source provenance and complete failure/retry audit. **No worker has been added in this change.**
3. Responses must be held privately as `provenance='ai'` drafts, subject to age suitability, reviewer inspection and separate visual/text moderation. The worker must not receive admin session tokens, raw athlete data, or database table privileges. It cannot approve or publish.
4. The API must eventually deliver only approved, currently published revisions with database-checked parent/player role and permitted age-band context. Neither a frontend audience selector nor a fan interest can bypass these checks.

## Coach-authored encouragement and development feedback

The user-facing example may read:

> Hello [player first name], I've noticed your progress and I'm proud of the effort you're putting in. One thing I'd like you to focus on in training is [coach's specific coaching point]. Keep going — [verified coach display name].

The placeholders must **not** be filled from user input and sent as though a real coach spoke. A coach must be authenticated and currently assigned to the player's organization/team. The coach reviews and explicitly signs off on the final text, coaching point, recipient identity and language **before** a message can be attributed to them. A generated suggestion must remain clearly marked as an AI draft until coach authorization. Enforce exact recipient binding and fresh active parent/guardian and player access checks server-side; use existing app communications permissions for delivery. No public profile, fan Hub feed, or cross-organization access.

Additional guardrails: default to periodic rather than automated high-frequency messages; avoid inappropriate pressure, comparisons with teammates, medical/psychological diagnoses, or revealing confidential evaluations. Age-appropriate wording and a reporting/escalation path to Academy staff are required. Never fabricate training observations or coach opinions; coach provides the grounded observation and focus point.

## Deployment

No deployment follows automatically from the queue. Hostinger test environment must be positively identified and isolated from the working live product, its database, DNS, secrets, ports and persistent volumes before any test deployment occurs.
