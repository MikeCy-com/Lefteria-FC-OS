namespace LefteriaFc.Infrastructure.ClubCalendar;

// Prefix for calendar reads and the final RSVP mutation statement. These sets
// come from the same PostgreSQL statement snapshot as the protected operation;
// /me's detached profile and singular team display field are not authority.
internal static class OperationalScopeSql
{
    public const string Ctes = """
        WITH requested AS (
          SELECT id,normalized_phone,role,legacy_id,management_admin_id
          FROM identity.mobile_identities
          WHERE id=@identity AND normalized_phone=@phone AND role=@role AND is_active
            AND @expires > statement_timestamp()
        ), current_management AS (
          SELECT a.id,a.active_organization_id FROM requested i
          JOIN identity.admin_accounts a ON a.id=i.management_admin_id
          LEFT JOIN identity.admin_staff_links link ON link.admin_account_id=a.id
          LEFT JOIN club.staff s ON s.id=link.staff_id
          WHERE i.role='management' AND a.is_active AND NOT a.invite_pending
            AND ((a.normalized_phone IS NULL AND i.legacy_id IS NULL) OR a.normalized_phone=i.normalized_phone)
            AND (link.admin_account_id IS NULL OR (s.is_active AND NOT s.is_archived))
        ), selected_management AS (
          SELECT a.id,g.organization_id FROM current_management a
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
            AND g.organization_id=a.active_organization_id
        ), linked_players AS (
          SELECT p.id,p.organization_id,p.team_id,l.is_primary
          FROM requested i JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role=i.role AND l.normalized_phone=i.normalized_phone
          JOIN identity.canonical_contact_bindings b ON b.collection=l.collection
            AND b.source_id=l.record_id AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
          JOIN club.players p ON p.id=l.record_id
          WHERE (i.role='parent' AND l.collection='players' AND l.kind='guardian'
              OR i.role='player' AND l.collection='players' AND l.kind='player_own')
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        ), coach_staff AS (
          SELECT s.id FROM requested i JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role='coach' AND i.role='coach' AND l.normalized_phone=i.normalized_phone
          JOIN identity.canonical_contact_bindings b ON b.collection=l.collection
            AND b.source_id=l.record_id AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
          JOIN club.staff s ON s.id=l.record_id
          WHERE l.collection='staff' AND l.kind='staff_own' AND s.is_active AND NOT s.is_archived
        ), coach_groups AS (
          SELECT DISTINCT g.academy_group_id FROM coach_staff s JOIN club.staff_academy_groups g ON g.staff_id=s.id
          WHERE g.from_plural OR g.from_singular OR (g.from_group_coach AND EXISTS (
            SELECT 1 FROM club.academy_group_coaches actual
            WHERE actual.academy_group_id=g.academy_group_id AND actual.staff_id=s.id))
        ), coach_teams AS (
          SELECT DISTINCT t.team_id FROM coach_staff s JOIN club.staff_teams t ON t.staff_id=s.id
        ), scope_players AS (
          SELECT p.id,p.organization_id,p.team_id FROM club.players p
          WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred AND (
            EXISTS(SELECT 1 FROM linked_players owned WHERE owned.id=p.id)
            OR EXISTS(SELECT 1 FROM selected_management selected WHERE selected.organization_id=p.organization_id)
            OR EXISTS(SELECT 1 FROM club.player_academy_groups membership JOIN coach_groups assigned
              ON assigned.academy_group_id=membership.academy_group_id
              WHERE membership.player_id=p.id AND membership.organization_id=p.organization_id)
            OR (p.organization_id='lefteria_fc' AND EXISTS(SELECT 1 FROM coach_teams assigned WHERE assigned.team_id=p.team_id))
          )
        ), scope_groups AS (
          SELECT 'lefteria_fc_academy'::text AS organization_id,g.academy_group_id AS id FROM coach_groups g
          UNION
          SELECT membership.organization_id,membership.academy_group_id FROM linked_players p
          JOIN club.player_academy_groups membership ON membership.player_id=p.id AND membership.organization_id=p.organization_id
          UNION
          SELECT g.organization_id,g.id FROM club.academy_groups g
          JOIN selected_management selected ON selected.organization_id=g.organization_id
        ), scope_teams AS (
          SELECT 'lefteria_fc'::text AS organization_id,t.team_id AS id FROM coach_teams t
          UNION SELECT p.organization_id,p.team_id FROM linked_players p WHERE p.team_id IS NOT NULL
          UNION SELECT t.organization_id,t.id FROM club.teams t
          JOIN selected_management selected ON selected.organization_id=t.organization_id
        ), bound AS (
          SELECT i.id,i.role,
            CASE i.role WHEN 'parent' THEN EXISTS(SELECT 1 FROM linked_players)
              WHEN 'player' THEN (SELECT count(*) FROM linked_players)=1
              WHEN 'coach' THEN EXISTS(SELECT 1 FROM coach_staff)
              WHEN 'management' THEN EXISTS(SELECT 1 FROM current_management)
              ELSE false END AS role_available
          FROM requested i
        )
        """;
}
