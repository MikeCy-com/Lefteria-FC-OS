# Delegated family calendar scope — security boundary

Status: **read-only delegated member calendar API available after this slice**. The ordinary player role, player-role switching, staff calendar, and RSVP writes remain unavailable to a fan. Accepted delegation does not create a player identity.

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

## Read-only delegated calendar route

`GET /api/mobile/family/calendar?month=YYYY-MM&offset=0` accepts only a currently valid OTP fan token. It accepts **no player_id parameter**; SQL finds the one delegated player via the phone-verified grant. It reads the **member** calendar projection using the existing `CalendarAccessSql.PlayerEligible` eligibility expression (guest, group, team and unscoped event membership), and evaluates `identity.current_family_calendar_scope` **inside the same calendar SELECT**. The response contains the single authorized child and that child's own RSVP values, never staff workflow state or team-wide player responses.

Missing, revoked or invalid grants fail closed without event data. No API invokes the existing RSVP write endpoint as a fan, and this route offers no role switch. The integration suite checks exact parity with the parent's selected child calendar, cross-organization protection, absence of staff-only fields, guardian deactivation, and revocation immediately affecting the next read.

## Next release gate

The delegated calendar is read-only and is not player-role provisioning. Before exposing other player views, RSVP writes, or role switching, separately review each private query for same-statement grant checks, concurrency/revocation races, guardian-child consent rules, response minimization and client behavior. This is not permission to widen existing role-based endpoints.
