# Parent pending reservation cancellation in the Children screen

This is a user-facing SvelteKit continuation of OS PR #37. The original Lefteria-FC repo remains read-only.

- Adds an explicit two-step Cancel → Confirm flow to each current pending reservation shown under Parent → Children.
- Reuses the already-verified parent-only `POST /api/mobile/family/invitations/{invitationId}/cancel` API and the existing PostgreSQL owner/guardian-checked security-definer cancellation function.
- The client permits cancellation only for a current linked child and current invitation ID already received from the guarded private outbox. It includes the active bearer token and rejects guessed/replayed invitation IDs client-side.
- Confirmation, loading, rate-limit/error and revoked-access states are visible. On success only the cancelled reservation is removed. On HTTP 403/404 from changed access the family/outbox cache is invalidated; on logout all in-flight results are discarded.
- No SMS has been sent and no child login is activated. The interface still states this explicitly. The child receives no invitation or notification.
- Adds controller/actual Svelte SSR tests including unrelated ID, no-auth, cancellation success/failure, stale response after logout and confirmation-state wiring.

**Not included:** inviting a target from the UI, OTP recipient acceptance, active player grant provisioning/revocation, notification delivery or full family settings. These require separate security review and implementation.

Five-job GitHub CI is mandatory before merge to Lefteria-FC-OS/main. No live deployment, production database write or source repository modification.
