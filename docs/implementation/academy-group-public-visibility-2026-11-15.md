# Academy group public visibility — review and release gate (2026-11-15)

**Development only. No live Hostinger deploy, no original repository edits.**

## Behavioral contract

- Public Academy navigation, directory and individual group URLs derive from canonical `club.academy_groups` joined to an existing manager-administered `club.academy_group_profiles`. Imported-only records remain hidden.
- The group profile now has `is_public boolean`. Existing profiled groups default to `true` during the migration to preserve their previously displayed state. **This is not retrospective evidence of publication approval**; administrators must audit existing profiles before external release.
- A manager or super admin with an active Academy organization grant and `academy:write` can change public visibility from `/admin/groups` in the Academy profile panel. It is a separate, explicitly confirmed action from editing the original metadata.
- Unpublishing excludes that group from the anonymous directory and hides its Academy group detail link on subsequent public reads. It does not delete private roster, family, attendance, training, financial, source import or coach records.
- All writes are guarded by `club.authorize_admin_academy_profile`, compare the exact profile revision, update only visibility/revision/audit fields and return an independently checked new revision. Stale revisions cause HTTP 409; missing profile or revoked Academy manager grant causes deny.
- The visibility endpoint only accepts `PUT /api/admin/football/academy-group-profiles/{groupId}/visibility?organization_id=lefteria_fc_academy`, a two-property JSON `{ "expected_revision": 1, "is_public": false }`, and the existing protected admin session. Duplicate/unexpected properties, wrong type/owner and over-1KB request bodies fail closed.
- The public/mobile runtime can call only the narrow, read-only public projection. It must not receive EXECUTE privileges for either management publication function nor any direct SELECT on private profile tables.

## Before isolated staging use

1. In an isolated disposable or controlled staging OS database, apply the new migration as migration owner with the existing operator-only migration procedure. Do not run migration as application runtime on startup.
2. Review and apply the updated `ops/postgres/admin-academy-profile-grants.sql` to the **separate web-admin authentication role** as an explicitly approved operator action. This script is not automatically activated against the live system. Apply restricted runtime grant/permission probes after migration using the standard operator procedure.
3. Run the rollback-only `ops/postgres/public-academy-directory-checks.sql` and `ops/postgres/admin-academy-profile-boundary.sql`, plus the API and frontend CI. Verify the generated OpenAPI and TS schema exact match.
4. With authorized disposable test identities, test that toggling a profiled group on/off shows/hides the Academy listing and detail; confirm manager audit, wrong owner/role, stale expected revision and no alteration to child memberships or group staff.
5. Capture original-vs-OS screenshots on populated staging with reference images at identical viewport/DPR. The PNG comparator will report **UNVERIFIED** until reference screenshots are supplied; a successful build alone does not establish pixel parity.

## Deliberate limits

This covers public group visibility, not a new youth photo/guardian-consent workflow; public player/gallery publication and age-appropriate privacy must be independently reviewed. Contact form backend submission, event RSVP and full role-specific functional parity remain separate unresolved gates. Do not confuse a development main merge with a production release.
