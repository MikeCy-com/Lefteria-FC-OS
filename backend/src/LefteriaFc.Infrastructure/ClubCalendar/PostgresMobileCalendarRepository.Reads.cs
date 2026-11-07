using System.Text.Json;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed partial class PostgresMobileCalendarRepository
{
    public async Task<CalendarMonthSnapshot> GetMonthAsync(MobileSession session,DateOnly month,string? playerId,int offset,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            var sql=OperationalScopeSql.Ctes+"\n"+CalendarAccessSql.SelectionCtes+"\n"+"""
                , visible AS (
                  SELECT c.*,
                    CASE WHEN @role IN ('parent','player') THEN c.member_day ELSE c.staff_day END AS effective_day,
                    CASE WHEN @role IN ('parent','player') THEN c.member_end_day ELSE c.staff_end_day END AS effective_end_day,
                    CASE WHEN @role IN ('parent','player') THEN c.member_projection_json ELSE c.staff_projection_json END AS projection
                  FROM club.calendar_items c WHERE NOT c.is_archived AND EXISTS(SELECT 1 FROM ready)
                    AND EXISTS(SELECT 1 FROM bound WHERE role_available) AND
                """+"\n"+CalendarAccessSql.VisibleItem+"\n"+"""
                ), page AS (
                  SELECT * FROM visible WHERE effective_day<@end AND effective_end_day>=@start
                  ORDER BY effective_day,projection->>'local_time',kind COLLATE "C",id COLLATE "C" OFFSET @offset LIMIT 201
                )
                SELECT
                """+"\n"+CalendarAccessSql.Metadata+"\n"+"""
                  ,coalesce((SELECT time_zone FROM ready),''),
                  coalesce((SELECT jsonb_agg(jsonb_build_object('id',p.id,'name',roster.name,'organization_id',p.organization_id)
                    ORDER BY p.is_primary DESC,p.id COLLATE "C") FROM linked_players p JOIN club.players roster ON roster.id=p.id),'[]'::jsonb),
                  (SELECT id FROM chosen_player),
                  CASE WHEN @role IN ('parent','player') THEN EXISTS(SELECT 1 FROM chosen_player) ELSE @player IS NULL END,
                  coalesce((SELECT jsonb_agg(jsonb_build_object('id',c.id,'kind',c.kind,'event_type',c.event_type,'organization_id',c.organization_id,
                    'projection',c.projection,'status',c.status,'is_cancelled',c.is_cancelled,'cancel_reason',c.cancel_reason,
                    'content_status',CASE WHEN @role IN ('parent','player') THEN NULL ELSE c.content_status END,
                    'responses',coalesce((SELECT jsonb_agg(jsonb_build_object('player_id',r.player_id,'status',r.status,'updated_at',r.updated_at))
                      FROM club.calendar_responses r JOIN chosen_player p ON p.id=r.player_id
                      WHERE r.collection='availability' AND r.kind=c.kind AND r.event_id=c.id),'[]'::jsonb))
                    ORDER BY c.effective_day,c.projection->>'local_time',c.kind COLLATE "C",c.id COLLATE "C") FROM page c),'[]'::jsonb)
                """;
            await using var command=Command(sql,session,playerId,offset:offset);
            command.Parameters.AddWithValue("start",month);command.Parameters.AddWithValue("end",month.AddMonths(1));
            await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            var rows=ReadJson<JsonElement[]>(reader.GetString(8));
            return new(reader.GetBoolean(0),reader.GetBoolean(1),reader.GetBoolean(2),OptionalString(reader,3),reader.GetString(4),
                ReadJson<CalendarPlayerOption[]>(reader.GetString(5)),OptionalString(reader,6),reader.GetBoolean(7),
                rows.Take(PageSize).Select(Item).ToArray(),rows.Length>PageSize?offset+PageSize:null);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    // Never use an app-side grant preflight here: the current guardian,
    // recipient, child roster and revocation state are verified by the exact
    // scope predicate INSIDE the same PostgreSQL SELECT as calendar data.
    public async Task<CalendarMonthSnapshot> GetDelegatedMonthAsync(
        MobileSession session,DateOnly month,int offset,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            var sql="""
                WITH ready AS (
                  SELECT time_zone FROM club.calendar_import_state
                  WHERE id='initial' AND is_complete
                ), delegated AS (
                  SELECT current_grant.player_id,current_grant.organization_id
                  FROM identity.read_current_family_delegation(
                    @identity,@phone,@role,@expires) current_grant
                  WHERE identity.current_family_calendar_scope(
                    @identity,@phone,@role,@expires,
                    current_grant.player_id,current_grant.organization_id)
                  LIMIT 2
                ), chosen_player AS (
                  SELECT p.id,p.organization_id,p.team_id,true AS is_primary
                  FROM delegated d JOIN club.players p ON p.id=d.player_id
                    AND p.organization_id=d.organization_id
                  WHERE (SELECT count(*) FROM delegated)=1
                ), visible AS (
                  SELECT c.*,c.member_day AS effective_day,
                    c.member_end_day AS effective_end_day,
                    c.member_projection_json AS projection
                  FROM club.calendar_items c
                  WHERE NOT c.is_archived
                    AND c.member_projection_json IS NOT NULL
                    AND EXISTS(SELECT 1 FROM ready)
                    AND EXISTS(SELECT 1 FROM chosen_player p WHERE
                """+"\n"+CalendarAccessSql.PlayerEligible+"\n"+"""
                    )
                ), page AS (
                  SELECT * FROM visible
                  WHERE effective_day<@end AND effective_end_day>=@start
                  ORDER BY effective_day,projection->>'local_time',
                    kind COLLATE "C",id COLLATE "C"
                  OFFSET @offset LIMIT 201
                )
                SELECT
                  EXISTS(SELECT 1 FROM identity.mobile_identities i
                    WHERE i.id=@identity AND i.normalized_phone=@phone
                      AND i.role='fan' AND i.is_active
                      AND @role='fan' AND @expires>statement_timestamp()),
                  EXISTS(SELECT 1 FROM ready),
                  EXISTS(SELECT 1 FROM chosen_player),
                  (SELECT organization_id FROM chosen_player),
                  coalesce((SELECT time_zone FROM ready),''),
                  coalesce((SELECT jsonb_agg(jsonb_build_object(
                    'id',p.id,'name',roster.name,
                    'organization_id',p.organization_id))
                    FROM chosen_player p JOIN club.players roster ON roster.id=p.id),'[]'::jsonb),
                  (SELECT id FROM chosen_player),
                  EXISTS(SELECT 1 FROM chosen_player),
                  coalesce((SELECT jsonb_agg(jsonb_build_object(
                    'id',c.id,'kind',c.kind,'event_type',c.event_type,
                    'organization_id',c.organization_id,
                    'projection',c.projection,'status',c.status,
                    'is_cancelled',c.is_cancelled,'cancel_reason',c.cancel_reason,
                    'content_status',NULL,
                    'responses',coalesce((SELECT jsonb_agg(jsonb_build_object(
                      'player_id',r.player_id,'status',r.status,'updated_at',r.updated_at))
                      FROM club.calendar_responses r
                      JOIN chosen_player p ON p.id=r.player_id
                      WHERE r.collection='availability' AND r.kind=c.kind
                        AND r.event_id=c.id),'[]'::jsonb))
                    ORDER BY c.effective_day,c.projection->>'local_time',
                      c.kind COLLATE "C",c.id COLLATE "C") FROM page c),'[]'::jsonb)
                """;
            await using var command=Command(sql,session,offset:offset);
            command.Parameters.AddWithValue("start",month);
            command.Parameters.AddWithValue("end",month.AddMonths(1));
            await using var reader=await command.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            var rows=ReadJson<JsonElement[]>(reader.GetString(8));
            return new(reader.GetBoolean(0),reader.GetBoolean(1),reader.GetBoolean(2),
                OptionalString(reader,3),reader.GetString(4),
                ReadJson<CalendarPlayerOption[]>(reader.GetString(5)),
                OptionalString(reader,6),reader.GetBoolean(7),
                rows.Take(PageSize).Select(Item).ToArray(),
                rows.Length>PageSize?offset+PageSize:null);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    public async Task<CalendarAvailabilitySnapshot> GetOwnAvailabilityAsync(MobileSession session,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            var sql=OperationalScopeSql.Ctes+"\n"+CalendarAccessSql.SelectionCtes+"\n"+"""
                , own_rows AS (
                  SELECT r.* FROM club.calendar_responses r
                  JOIN linked_players p ON p.id=r.player_id AND p.organization_id=r.organization_id
                  JOIN club.calendar_items c ON c.kind=r.kind AND c.id=r.event_id AND c.organization_id=r.organization_id
                  WHERE r.collection='availability' AND @role IN ('parent','player') AND NOT c.is_archived
                    AND c.member_projection_json IS NOT NULL AND EXISTS(SELECT 1 FROM ready)
                    AND EXISTS(SELECT 1 FROM bound WHERE role_available) AND
                """+"\n"+CalendarAccessSql.PlayerEligible+"\n"+"""
                  ORDER BY r.updated_at DESC NULLS LAST,r.kind COLLATE "C",r.event_id COLLATE "C",r.player_id COLLATE "C" LIMIT 500
                )
                SELECT EXISTS(SELECT 1 FROM bound WHERE role_available),EXISTS(SELECT 1 FROM ready),
                  coalesce((SELECT jsonb_agg(jsonb_build_object('event_id',r.event_id,'event_type',r.kind,'player_id',r.player_id,'status',r.status,'updated_at',r.updated_at)
                    ORDER BY r.updated_at DESC NULLS LAST,r.kind COLLATE "C",r.event_id COLLATE "C",r.player_id COLLATE "C") FROM own_rows r),'[]'::jsonb)
                """;
            await using var command=Command(sql,session);
            await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            return new(reader.GetBoolean(0),reader.GetBoolean(1),ReadJson<MobileAvailabilityRow[]>(reader.GetString(2)));
        }
        finally{await database.Database.CloseConnectionAsync();}
    }
}
