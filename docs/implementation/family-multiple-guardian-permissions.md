# Family Settings — multiple Player and Parent/Guardian permissions

**2026-11-10 · Draft PR #154 · NOT COMPLETE · no production enablement**

## Requested behavior

Within the parent-role Family settings, show a **Give Permission** form
with a child selector and two distinct permission types:

- **Player:** keep existing phone-targeted player invitation; the player
  should see only their own player profile and explicitly allowed player
  features. Never obtain parent data via a player identity.
- **Parent/Guardian:** allow multiple separately identifiable recipients for
  each child. A verified, accepted guardian should see the **same
  child-scoped parent information and actions** as the existing parent;
  for parents with multiple children, each child must be granted explicitly.
- **Permissions list:** show pending, accepted and expired/revoked statuses
  per child and permission type. A parent can revoke an individual grant.
  Deleting one grant must not remove siblings or unrelated child permissions.
  An accepted recipient may also need to be able to decline/leave.
- Keep the original black/gold Fira Sans parent mobile design, session guards,
  Greek wording and accessible confirmation dialogs.

## Implemented on this draft branch

- Database: `20261110173000_MultipleGuardianPermissions.cs` adds a separate
  invitation table keyed by permission UUID; there can be multiple live
  guardian recipients per child. Current canonical guardian link is
  revalidated at request time. A single recipient/child duplicate is denied,
  a replayed UUID with changed recipient fails, and a 20-live-per-child
  bound exists. Recipient phone is stored privately, not sent to parent
  list responses or logs. No PUBLIC raw-table grants.
- Backend: `GuardianPermissionService`, `PostgresGuardianPermissionRepository`,
  and mobile endpoints to **issue**, **list** and **revoke** guardian
  permissions. The service checks the active parent role/identity,
  throttles requests and maps faults without exposing child PII.
- UI: `MobileFamily.svelte` now exposes **Player / Parent–Guardian** and
  the selected child's phone-targeted invitation request. The guardian
  permissions list is keyed by independent permission UUID with an explicit
  revoke confirmation. Existing player invitation/outgoing/revocation UI
  remains intact. The session-aware controller validates responses and
  invalidates state after logout or role change.
- Security: staged manual, review-only `guardian-permission-grants.sql`,
  disposable-database `guardian-permission-boundary.sql`, source-level tests
  `mobile-multiple-guardian-permissions.test.mjs`, and a CI probe.
  No grants have been executed. No real phone was contacted.

## CRITICAL: This is NOT full shared-parent access yet

**A pending guardian permission grants zero data access.**
The new permission table deliberately has no public recipient acceptance,
SMS delivery, guardian role promotion or authorization expansion.

The existing codebase uses canonical guardian contact bindings to establish
the parent role in `MobileAuthService`, `ParentFamilyService`,
`PostgresParentFamilyQueries`, `OperationalScopeSql` (calendar),
messaging, payments, announcements, and other child-scoped endpoints.
Adding a row or setting `status='active'` without verified acceptance and
current authorization across *every* parent data endpoint would give a false
sense of access or cause unintended disclosure. The legacy
`family_delegated_player_grants` explicitly represents a different, limited
player-delegation model and cannot simply be used for co-guardian access.

To complete the feature safely:

1. Implement an OTP-verified recipient **inbox + explicit accept/decline**,
   with current identity/phone verification, conflicts for existing roles,
   row locking and recipient audit. Never auto-activate after parent entry.
2. Establish a distinct **delegated guardian parent role** (not a player's
   login), with authorization for exactly the accepted children. Support
   existing parent accounts and new eligible recipients, without overwriting
   canonical/primary guardian contacts or taking over an identity.
3. Update **all** protected parent reads/writes, not just the Family page:
   children listing, calendar/attendance/RSVP, billing/issued months,
   announcements/messages, documents, evaluations and any other parent
   capability; classify features requiring additional consent, including
   medical or high-risk data. Revalidate source guardian authority,
   active roster, recipient account and grant on every operation.
4. Allow accepted guardians to grant further guardians **only with current
   child authority and a verified source grant**. Record the grant lineage,
   revoke descendants safely if the source guardian loses access, avoid
   cycles and prevent a recipient escalating access to siblings.
5. Provide a **unified permissions list** with individual cancel/revoke,
   accepted recipient display name only when legitimately available, no raw
   phone leakage, and correct status from a fresh authorized API.
6. Test with seeded disposable parents, recipients, siblings, revoked
   guardian, transferred player, duplicate phone, expired invitation,
   concurrent accept vs revoke and long-lived sessions. Verify no player
   identity ever gains parent-only access.
7. Run actual SvelteKit, .NET, PostgreSQL migrations, auth integration
   and browser role tests at the exact SHA; maintain PR as draft until
   runner and security gates pass.

**NEVER call this feature operational or merge/deploy while those steps
are incomplete.** Original Lefteria FC repository and live environment
must not be modified.

## Actual verification checkpoint

GitHub Actions workflow `42806695867` at SHA
`81b3cf1dd84afd13e2e40772e22e9d7dbaf51d88` completed the
**ASP.NET Core API** job successfully: locked restore, Release API build,
and policy checks all ran and passed. The **SvelteKit web**, **mobile identity
with PostgreSQL**, **Docker Compose**, and **API contract/startup**
jobs failed with zero executed steps, meaning those failures provide
no feature-level test results. This run does NOT verify the unmerged
PostgreSQL migrations or mobile UI. Later commits after this SHA need
their own exact-SHA verification.

## Recipient consent implementation — 2026-11-10

Added migration `20261110174000_GuardianRecipientConsent.cs` with
three strictly scoped, private functions:

- `identity.read_recipient_guardian_permissions` returns only invitation
  UUID, organization, pending status and expiry to the **currently verified
  target phone** with a live Fan or Parent identity. It revalidates the
  original source guardian and active child; it does not return child or
  guardian private details.
- `identity.accept_recipient_guardian_permission` rechecks the recipient
  identity/phone, current source guardian and child, locks rows and records
  the recipient's explicit consent in the
  **`accepted_pending_access` state** with an audit event. It never mints
  a parent identity, changes a role, creates a canonical guardian contact,
  permits a player login or unlocks child data.
- `identity.decline_recipient_guardian_permission` lets the currently
  verified recipient decline their own pending invitation; private audit
  and row-state transitions are retained.

Added service/repository methods and authenticated mobile endpoints
`GET /api/mobile/family/guardian-invitations`,
`POST /api/mobile/family/guardian-invitations/{permissionId}/accept`,
and `POST /api/mobile/family/guardian-invitations/{permissionId}/decline`.
`MobileGuardianConsent.svelte` is shown under the authenticated Fan/Parent
profile, with two-step confirmation. It accurately reports that the
consent is recorded **but full parent access is not yet active**.

The issuing parent can distinguish multiple permissions for one child by
the recipient's masked final four phone digits and revoke each permission
individually. The app also groups Player and Parent/Guardian permission
entries in Family settings. The requested complete shared-parent experience
is still **BLOCKED** on delegated-parent role recognition, cross-feature
parent access authorization and actual end-to-end tests.

The latest structural tests include
`mobile-multiple-guardian-permissions.test.mjs` and
`mobile-guardian-recipient-consent.test.mjs`. They test source contracts,
not actual .NET/UI/SQL execution. All new function grants remain
operator-reviewed and disabled by default. No real SMS or live account
was used.
