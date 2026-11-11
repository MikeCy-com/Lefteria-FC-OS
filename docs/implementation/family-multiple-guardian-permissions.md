# Family Settings — direct phone permissions for children and other guardians

**2026-11-10 · PR #154, draft only. No live deployment or permissions.**

## Final user-requested experience

The parent opens **Family → Give Permission**, selects **Player** or
**Parent/Guardian**, and enters a phone number. There is **no guardian
invitation, recipient acceptance, expiry or acceptance screen**.

- **Exactly one child:** show that child's name automatically; no child dropdown.
- **Two or more children:** show a child dropdown. Under Parent/Guardian
  it also offers **All my children**, which writes a separate permission
  for each selected child. Under Player, the parent chooses the particular
  child and enters that player's own phone.
- Parents may create permissions for each of their children and for
  multiple separate guardians, and list/revoke each separately. The list
  contains only masked recipient suffixes, not full phone numbers.
- Player permission remains the existing player-specific flow and must
  never authorize access to parent-only information.
- The original parent and every verified secondary guardian must see
  **the same authorized parent features for the child or children they
  have permission to access**, not other children on the same family
  account.

## Code implemented in OS draft

1. `MobileFamily.svelte`: Player/Parent–Guardian selector, automatic
   single-child selection, multi-child selector, guardian all-children
   option, phone entry, list of player and guardian permissions, individual
   revoke confirmation, and progress reporting for partial bulk success.
2. `20261110173000_MultipleGuardianPermissions`: no invitation status;
   stores revocable `granted` phone permissions separately per child,
   with immutable UUID, normalized phone, canonical source and optionally
   `source_permission_id`. Duplicate grants are rejected. Parent
   authority is revalidated. No direct table privileges are issued.
3. `20261110175000_GuardianVerifiedPhoneScope`: validates a bounded
   revocable grant lineage back to an active original canonical guardian
   for the same active child. Every ancestor must still be granted;
   revocation of a source invalidates descendants. Phone grants are
   disallowed for player, coach and management-role phone identities.
4. After the **recipient's normal OTP verification**, the auth
   transaction may create a Parent role for the granted phone. It
   does not create that role when the original parent types a phone.
   Inactive Parent accounts cannot be revived by a grant.
   `CanonicalMobileProfiles` dynamically confirms available
   child grants on subsequent session checks; login/creation is audited.
5. `PostgresParentFamilyQueries`, `OperationalScopeSql`, and
   `20261110176000_DelegatedGuardianChatDirectory`: include only current
   child-scoped grants in family listing, mobile calendar/RSVP and group
   chat directory. Revocation is checked via current database scope,
   not cached token claims. Original Player role remains independent.
6. Separate guardian issue/list/revoke mobile API and repository;
   existing Player issue/list/revoke flows are retained.
7. The unused consent/recipient-invitation migration, UI and endpoints
   have been removed from this draft. No extra action is required from
   a recipient apart from their normal app phone verification.

## Mandatory security and release gates

- All restricted database functions remain without runtime EXECUTE
  grants unless an operator reviews and runs the dedicated
  `ops/postgres/guardian-permission-grants.sql`. There are **no live
  permissions**.
- Parent data features outside current Family, calendar and group chat
  directory need a source-to-OS authorization audit, including finance,
  receipts, child documents, private chat, evaluations and notifications
  wherever implemented. An endpoint is not equivalent merely because
  its menu entry is visible.
- Run actual disposable PostgreSQL migration up/down, concurrent
  source-vs-descendant revocation, OTP first-time parent provisioning
  and inactive-account tests. Run multi-child and all-children browser
  tests, including partial failures, phone number reuse, revoked
  canonical source, sibling isolation and different account roles.
- Full .NET restore/build, SvelteKit check/tests, Docker and API startup
  and mobile browser checks must run at the exact PR SHA before main
  merge. No original Lefteria FC repo writes, no Hostinger/live deploy.

**Current status:** implementation staged in PR #154, *not yet production
verified*. The completion claim must wait for those release gates.

## 2026-11-10 — Delegated parent parity and safe retry checks

- A new `MobileFamily` bulk-request draft preserves the same request UUIDs
  for an unchanged recipient phone and child list. A lost HTTP response can
  now be retried without minting fresh, duplicate guardian permissions.
  Changing the phone, child or grant type clears the retry draft; successful
  verification also clears it.
- `PostgresRosterQueries` now recognizes a live, scoped verified-phone
  guardian grant even when an existing Parent identity has a non-null
  legacy identifier and no longer has a currently bound original child.
  It still refuses to grant a Player or Fan the Parent role.
- New `mobile-delegated-parent-scope-parity.test.mjs` checks source references
  to live guardian scope across the identity profile, Family children, roster
  picker, calendar and chat directory. This is structural proof only.
- **Remaining**: real .NET/PostgreSQL/HTTP tests must confirm actual SQL
  function grants and migrations, concurrent parent revoke versus child read,
  failed second-child bulk grant rollback, replay with the same request IDs,
  OTP recipient role switching, and all protected family finance/media/
  messaging endpoints that may use independent child authorization.

Grant existence must not be confused with a logged-in session. The recipient
still completes OTP before any Parent app request can pass.