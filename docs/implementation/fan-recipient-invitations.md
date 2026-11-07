# Fan-profile recipient pending invitation status

This UI follows the backend migration in [PR #39](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/39). Original `MikeCy-com/Lefteria-FC` remains strictly read-only. Implementation applies only to OS.

- A holder of a current OTP-verified fan session can see their read-only, pending invitation reservations at `/app?tab=profile`. Club/Academy and expiry are displayed. The section never displays a player's identity, name, guardian phone, target phone or audit fields.
- The mobile controller uses the generated typed client for `GET /api/mobile/family/invitations`. It verifies role=fan, active current identity and token, allows at most one pending invite, validates organization and timestamps and **projects only the known five metadata fields** even if an upstream response has unexpected private keys.
- In-memory results are discarded on logout, role/identity switch, revoked access, tab refresh, invalid responses and stale HTTP completion. The new Svelte section revalidates data on mount and offers a manual refresh.
- The UI clearly explains that **no SMS was sent and no player access is active**. Acceptance, decline, role provisioning, grant revocation and related notifications remain *Under Developments*.
- Synthetic controller and actual Svelte SSR tests verify session isolation, invalid records, stale responses, field redaction, roles, empty/error states and safe route wiring.

No new database migration, API write, notification dispatch or provider integrations. Merge exclusively to Lefteria-FC-OS/main after all five CI jobs pass. Production deployment is a separate approval.
