# Academy CRM and parent/role workflow refinements (2026-11-23)

## Scope and security boundaries

This is a **front-end/workflow change** with no new privileged endpoints, tables, automatic notifications or finance side effects. It builds on the original authorization contracts in main, not on unmerged PRs #470 and #471. It may be reviewed/merged independently.

### Desktop admin workboard — eight roles

The `/admin` homepage now prioritizes up to six task-specific entry points based on `adminSectionsFor(session.user)`, **restricted to live modules only**. Unknown, planned, unassigned and unauthorized operations never produce links. The board distinguishes:

- Super Admin: registration/guardian approval, roster, finance, territories, staff, user access (within actual granted modules).
- Manager: Academy and/or first-team operations only when granted, never organization-wide finance.
- Accountant: organization finance **read-only** oversight; no implied payment-mutating access.
- Cashier: scoped POS, without private Academy/player or billing access.
- Editor/Marketing: live and authorized editorial tools only; no assumed child records.
- Coach/Match-day Editor: no fake module access; the account access page clarifies missing or pending grants.

The dashboard does not show fake outstanding tasks or cross-city counts. Every URL is an existing, exact permitted destination and each target API performs its own authorization. An Academy manager with genuine Academy write permission can open the current player CRM view; otherwise a read-only manager enters only the groups directory.

### Registrations, children and guardians

`/admin/academy-office?queue=action` opens a **working inbox** with status filtering, search, accessible status labels, next-step guidance, and per-page summary. Filters only operate on the data already returned by the existing authorized endpoint. Counts say **current loaded page**, not global Academy numbers. No parent identity is inferred from a phone number or an approved registration; the existing Super Admin proof-review, parental consent and atomic enrollment checks are retained. No enrollment, external contact or guardian approval happens automatically.

### Mobile parent dashboard

The parent dashboard now has a concise unanswered RSVP queue for **the currently selected, verified child only**, using the existing guardian-authorized calendar and existing `onRespond` callback. A stale calendar, wrong role, different organization, absent/revoked child or different month produces **no action queue**. Parent replies are never optimistically marked as confirmed; they update only after the controller confirms and reloads the data.

The quick shortcut to Calendar now follows `activeChild.id` rather than always opening the first sibling's schedule. No new child info is exposed, and no personal/health/guardian contact data is copied into the workflow summary.

## Acceptance / review

1. Run `npm run check`, `npm test`, `npm run build` and existing .NET/DB/contract pipelines.
2. Verify Admin landing page for all eight admin roles with and without organization grants, including read-only manager and accountant.
3. Verify registration search, page-local summary, new/under-review/needs-info/approved/rejected filters, next-step copy and existing guardian proof form in staging.
4. Verify parent with two linked children: switching child changes which schedule/calendar shortcut is used. Confirm no stale RSVP queue for a different child or revoked guardian.
5. Confirm no extra API requests in the role task board and no pending/disabled feature advertised as a live action.

## Explicitly not covered

Official roster/COMET profile editing, photo permissions, financial cancellation and location-scoped private player records are handled in separate PRs. Manual accounting changes, parent document consent verification and new coach attendance endpoints are **not** opened by this UI work. This PR is not deployed.
