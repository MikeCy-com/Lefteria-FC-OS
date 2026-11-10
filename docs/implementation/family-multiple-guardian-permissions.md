# Family Settings — direct phone access for players and parents

**2026-11-10 · Draft PR #154 · unmerged, untested end-to-end, no production activation**

## Required parent experience

The parent opens **Family → Give Permission**, selects **Player** or **Parent/Guardian**, and enters the recipient's telephone number. There is **no recipient invitation or acceptance step for Parent/Guardian**. The person must still verify ownership of the number using the existing OTP login.

When the parent has one child, the child's name is displayed and selected automatically. When the parent has multiple children, the child selector appears. **Parent/Guardian** offers **All my children**, creating independent permissions so each child's access can be revoked separately. **Player** remains restricted to the selected player's own profile; a single phone does not turn into multiple player identities. Multiple guardians and grants are supported. A permission list shows granted guardians (with only the recipient's last four phone digits) and player grants with per-record revoke confirmation.

## Current implementation in OS draft PR #154

- `20261110173000_MultipleGuardianPermissions`: direct `granted` phone-scoped rows, independent by organization/child/recipient, with audit, idempotent request IDs, duplicate protection, restricted table, issuer identity and recursive revocation lineage. No invitation acceptance, recipient status transition, or expiry.
- `20261110175000_GuardianVerifiedPhoneScope`: stable SQL functions for current grants. The root must remain an active **canonical guardian** of that child, and each onward delegated parent must be an active parent identity using the preceding granted phone. Only active, non-transferred children qualify; the lineage is bounded and cycle-safe. Revocation breaks descendant access.
- Existing OTP verified login gets a **Parent role option** when the number has a current valid parent grant; the verified recipient's parent identity is created or reused *after* OTP proof. An existing inactive parent profile is not silently reactivated. Player, Coach and Management identities are not promoted to Parent through a phone grant.
- `CanonicalMobileProfiles` and `PostgresParentFamilyQueries` incorporate currently granted children; `OperationalScopeSql` extends parent calendar/attendance scope and `20261110176000_DelegatedGuardianChatDirectory` updates the chat group directory. Source authorization is checked again in SQL, not trusted from stale client state.
- `MobileFamily.svelte` now contains the requested two-type selector, automatic one-child name, multiple-children dropdown, All my children option for parent sharing, telephone input and consolidated revocable permission list. App controller keeps parent identity/session, child and request controls.
- SQL function grants are restricted, operator-reviewed, never applied automatically. No legacy repo or production database changes.

## Remaining release requirements (not optional)

- Confirm **all** child-related parent-only APIs use current delegated child scope, not solely canonical contact bindings: Academy fees and invoices, protected media/files, announcements, messaging read/write, attendance, evaluation summaries, notifications, parent educational content and any other role endpoint. Where the original parent has privileged private information, verify policy and least-privilege. A parent grant must never expose siblings or unrelated children.
- Run real PostgreSQL migrations and positive/negative tests with at least: two children, two recipient parents, multiple grants, original guardian revoke, delegated parent onward grant, OTP replay, invalid/inactive phone identity, transferred/archived child, expired session, concurrent add/revoke, partial bulk write and parent-to-player role isolation.
- Confirm exact frontend/backend DTO shape and compile SvelteKit, .NET, API contract tests, mobile UI interactions and authorization tests at the **same Git commit SHA**.
- Check recipient identity UX: if a phone already has a Fan role, the verified account should be able to select Parent; it must not take over or overwrite a role.
- Review the no-recipient-consent requirement against the club's privacy/legal policy, but **do not add a recipient acceptance screen**. Identity proof occurs through login OTP only.
- Do not merge or deploy before all checks pass. The original Lefteria FC is read-only.

**Working distinction:** A successful parent permission write is a stored **direct grant**. The second adult can use it only after logging in with the matching verified phone and selecting Parent. It does not create a usable session on someone else's behalf.
