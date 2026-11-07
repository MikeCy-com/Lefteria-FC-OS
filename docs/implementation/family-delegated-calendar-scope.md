# Delegated family calendar scope — security boundary

Status: **foundation only**. The real player calendar endpoints, role switching, RSVP, and any minor's private data remain unavailable to a fan holding a family grant. No client should treat acceptance or a true scope check as a player identity.

## Implemented DB predicate

The restricted application account can execute `identity.current_family_calendar_scope(identity, phone, role, expires, player, organization)`. It returns one Boolean for **exactly** the requested child and organization. It reuses `identity.read_current_family_delegation` to verify, at query time:

- The OTP-verified, current **fan** recipient identity and normalized phone.
- The accepted invitation, active grant, and nonrevoked history for exactly this recipient.
- The issuing parent's active canonical guardian link and binding to the same child.
- An active, nonarchived, nontransferred roster child and initial import readiness.
- No conflicting canonical player ownership or formal player/parent role on the recipient phone.
- Exact player ID **and** organization ID, not a caller-chosen team, account, or club-wide scope.

Missing or unrelated scopes return false. Invalid/expired fan proof raises a generic denial. No names, phone numbers, grant IDs, or raw roster data are returned. The function is SECURITY DEFINER with a pinned search path and narrowly granted EXECUTE only; the runtime cannot read the private grant table.

## Mandatory integration rule

**Do not call this as an application-layer preflight followed by a separate SQL query.** Revocation can occur between two statements. Instead, a future calendar read must join or call the predicate in the *same SQL SELECT* that reads the private event:

```sql
-- Illustration only. NOT a replacement for event visibility, RSVP ownership,
-- soft-delete, club/organization selection, or event/team/group eligibility.
SELECT ...
FROM club.calendar_items c
WHERE c.organization_id = @organization
  AND identity.current_family_calendar_scope(
    @identity, @phone, @role, @expires,
    @player, c.organization_id)
  AND ... -- event visibility for the exact delegated player
```

The application's mobile principal supplies `identity`, `phone`, `role`, and `expires`; never accept these values from browser input. Caller-provided player IDs alone convey zero authority. The future query must also enforce the *existing* linked-player event eligibility and avoid returning staff-only views or team-wide records.

## Verification

The integration suite uses the restricted runtime role against real PostgreSQL. It checks the correct recipient/player/organization, unrelated players and organizations, other recipient phones, mismatched phone claims, invalid roles, expired sessions, deactivated guardians, and immediate denial on revocation. The existing negative tests continue to deny fan-to-player role switching and regular private player/calendar APIs.

## Next release gate

Only after a **separate** end-to-end implementation and review of calendar event eligibility, child consent/guardianship rules, concurrency and revocation races, response minimization, and client behavior may a delegated calendar read be exposed. RSVP writes and full player-role provisioning require independent review. This migration alone authorizes no new HTTP routes.
