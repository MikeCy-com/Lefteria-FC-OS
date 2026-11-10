# App and public website — workflow closure register (2026-11-10)

**State:** development/backlog; NOT a declaration of completed work.
This register is based on the original Lefteria-FC's admin/public/mobile modules
and the OS pre-live gap audit. Live production must not be modified.

## Public website and CMS
- Club profile and published news already have partial OS routes. Complete
  editor-to-public propagation, multi-language canonical URLs, SEO, image
  uploads, accessibility, content moderation, previews and cache invalidation.
- Add manager-scoped sponsors/partners, gallery, events/fixtures and campaign
  publishing; treat upload rights and scheduled publishing separately from
  public read permissions.
- Academy groups, registrations and First Team trials must use verified
  canonical roster and private intake state. No anonymous access to minors'
  private documents, evaluations or enrollment health disclosures.
- Online shop requires distinct inventory-safe basket, checkout, order
  lifecycle, payment-provider confirmation, pickup/delivery, receipt/refund and
  customer consent. POS operator tokens may NEVER authorize the website.

## Private app role workflows
- Parent: child-linked financial balances and real receipts, upcoming training,
  secure RSVP/attendance, guardian invitations, educational articles and
  appropriate consent-bound athlete-development summaries.
- Player and coach: assigned team schedules, evaluations, attendance, drills,
  role-scoped communications and player-facing permissions; a coach cannot
  discover arbitrary minors outside their assignments.
- Fan: published non-private announcements and approved club media only;
  no Academy guardian charges or player documents.
- Management: mobile in-app message edit/delete/long press/reactions,
  read receipts with authorized names, private chat deletion-for-self,
  announcements with images/attachments, admin-only delete, targeting and
  notification outbox/retry. The web admin acts as a control plane, not as a
  replacement for actual mobile interactions.

## Existing draft dependencies
- #143 / #146 — finance, original charge types and settlement ledger.
- #147 — Academy attendance manager register.
- #150 — private registration intake staging.
- #151 — both-organization private evaluations, encrypted document quarantine.
- #152 — location-specific POS and stock, private sale journal foundations.

## Acceptance before merge/go-live
1. For every route, verify real data and **deny by default** for anonymous,
   wrong organization, wrong group, revoked session and unassigned staff.
2. Test dynamic groups and memberships after new team creation, assignment,
   removal, archived/transfer states and role change.
3. For app messaging, test real devices: edit/delete/hold, emoji reaction,
   seen-by policy, media attachment quarantine, push retry and offline state.
4. For website publish, verify admin edit→preview→publish→public response,
   SEO, media safety, cache purge, accessible desktop/mobile views.
5. For POS and finance, prove exact cents and provider settlement independently
   of redirects; rehearse refunds, stock races, receipt numbering and audits.
6. Reconcile original/source data in disposable staging first; protect youth
   data. Run .NET/Svelte/Postgres CI, generated OpenAPI diff, browser snapshots
   and reviewed production deployment checklist on the exact SHA.

A module labelled `live` in a development admin catalog is not evidence of a
production-complete workflow. Block all unimplemented transitions rather than
returning fake successful state.
