namespace LefteriaFc.Infrastructure.ClubCalendar;

internal static class CalendarAccessSql
{
    public const string SelectionCtes="""
        , ready AS (SELECT time_zone FROM club.calendar_import_state WHERE id='initial' AND is_complete),
        chosen_player AS (
          SELECT p.id,p.organization_id,p.team_id FROM linked_players p
          WHERE @player IS NULL OR p.id=@player
          ORDER BY p.is_primary DESC,p.id COLLATE "C" LIMIT 1
        )
        """;

    // Aliases c (calendar item) and p (current active roster player) are fixed
    // internal SQL identifiers. Guests can cross groups, never organizations.
    public const string PlayerEligible="""
        c.organization_id=p.organization_id AND (
          EXISTS(SELECT 1 FROM club.calendar_item_guests guest
            WHERE guest.kind=c.kind AND guest.event_id=c.id AND guest.player_id=p.id)
          OR EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned
            WHERE assigned.kind=c.kind AND assigned.event_id=c.id
              AND assigned.organization_id=c.organization_id
              AND assigned.scope_kind='group'
              AND (
                EXISTS(SELECT 1 FROM club.player_academy_groups membership
                  WHERE membership.academy_group_id=assigned.record_id
                    AND membership.organization_id=assigned.organization_id
                    AND membership.player_id=p.id)
                OR EXISTS(SELECT 1 FROM club.players current_player
                  WHERE current_player.id=p.id
                    AND current_player.organization_id=c.organization_id
                    AND current_player.is_active AND NOT current_player.is_archived
                    AND NOT current_player.is_transferred
                    AND current_player.primary_academy_group_id=assigned.record_id)
              ))
          OR (NOT EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned WHERE assigned.kind=c.kind AND assigned.event_id=c.id AND assigned.scope_kind='group')
            AND EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned WHERE assigned.kind=c.kind AND assigned.event_id=c.id
              AND assigned.scope_kind='team' AND assigned.record_id=p.team_id))
          OR (NOT EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned WHERE assigned.kind=c.kind AND assigned.event_id=c.id)
            AND NOT EXISTS(SELECT 1 FROM club.calendar_item_guests guest WHERE guest.kind=c.kind AND guest.event_id=c.id))
        )
        """;
    public const string HostAssigned="""
        EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned JOIN scope_groups permitted
          ON permitted.id=assigned.record_id AND permitted.organization_id=assigned.organization_id
          WHERE assigned.kind=c.kind AND assigned.event_id=c.id AND assigned.scope_kind='group')
        OR EXISTS(SELECT 1 FROM club.calendar_item_scopes assigned JOIN scope_teams permitted
          ON permitted.id=assigned.record_id AND permitted.organization_id=assigned.organization_id
          WHERE assigned.kind=c.kind AND assigned.event_id=c.id AND assigned.scope_kind='team')
        """;
    public const string VisibleItem="""
        (
          (@role IN ('parent','player') AND c.member_projection_json IS NOT NULL AND EXISTS(
            SELECT 1 FROM chosen_player p WHERE
        """+"\n"+PlayerEligible+"\n"+"""
          ))
          OR (@role='management' AND EXISTS(SELECT 1 FROM selected_management selected WHERE selected.organization_id=c.organization_id))
          OR (@role='coach' AND (
        """+"\n"+HostAssigned+"\n"+"""
            OR EXISTS(SELECT 1 FROM club.calendar_item_guests guest JOIN scope_players own
              ON own.id=guest.player_id AND own.organization_id=guest.organization_id
              WHERE guest.kind=c.kind AND guest.event_id=c.id AND guest.organization_id=c.organization_id)
          ))
        )
        """;
    public const string Metadata="""
        EXISTS(SELECT 1 FROM bound WHERE role_available),EXISTS(SELECT 1 FROM ready),
        (@role<>'management' OR EXISTS(SELECT 1 FROM selected_management)),
        CASE WHEN @role='management' THEN (SELECT organization_id FROM selected_management)
          WHEN @role IN ('parent','player') THEN (SELECT organization_id FROM chosen_player) ELSE NULL END
        """;
}
