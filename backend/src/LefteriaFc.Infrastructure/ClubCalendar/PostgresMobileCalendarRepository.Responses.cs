using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed partial class PostgresMobileCalendarRepository
{
    public async Task<CalendarResponsesSnapshot> GetResponsesAsync(MobileSession session,string eventId,string? kind,int offset,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            var sql=OperationalScopeSql.Ctes+"\n"+CalendarAccessSql.SelectionCtes+"\n"+"""
                , targets AS (SELECT * FROM club.calendar_items WHERE id=@event AND (@kind IS NULL OR kind=@kind)),
                authorized AS (
                  SELECT c.* FROM targets c WHERE (SELECT count(*) FROM targets)=1 AND NOT c.is_archived
                    AND @role IN ('coach','management') AND EXISTS(SELECT 1 FROM bound WHERE role_available)
                    AND EXISTS(SELECT 1 FROM ready) AND
                """+"\n"+CalendarAccessSql.VisibleItem+"\n"+"""
                ), roster AS (
                  SELECT p.id,p.name,p.number,p.position,p.image_url,r.status,r.updated_at
                  FROM authorized c JOIN club.players p ON p.organization_id=c.organization_id
                  LEFT JOIN club.calendar_responses r ON r.collection='event_attendance' AND r.kind=c.kind AND r.event_id=c.id AND r.player_id=p.id
                  WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred AND
                """+"\n"+CalendarAccessSql.PlayerEligible+"\n"+"""
                    AND (@role='management' OR (
                      EXISTS(SELECT 1 FROM scope_players own WHERE own.id=p.id)
                      OR ((
                """+"\n"+CalendarAccessSql.HostAssigned+"\n"+"""
                      ) AND EXISTS(SELECT 1 FROM club.calendar_item_guests guest WHERE guest.kind=c.kind AND guest.event_id=c.id AND guest.player_id=p.id))
                    ))
                  ORDER BY p.number,p.id COLLATE "C" OFFSET @offset LIMIT 201
                )
                SELECT
                """+"\n"+CalendarAccessSql.Metadata+"\n"+"""
                  ,(SELECT count(*) FROM targets),EXISTS(SELECT 1 FROM authorized),
                  (SELECT kind FROM authorized),(SELECT organization_id FROM authorized),
                  coalesce((SELECT jsonb_agg(jsonb_build_object('id',p.id,'name',p.name,'number',p.number,'position',p.position,
                    'image_url',p.image_url,'status',p.status,'updated_at',p.updated_at) ORDER BY p.number,p.id COLLATE "C") FROM roster p),'[]'::jsonb)
                """;
            await using var command=Command(sql,session,eventId:eventId,kind:kind,offset:offset);
            await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            var count=reader.GetInt64(4);var authorized=reader.GetBoolean(5);
            var decision=count==0?CalendarWriteDecision.NotFound:count>1?CalendarWriteDecision.Ambiguous:
                !authorized?CalendarWriteDecision.NotAssigned:CalendarWriteDecision.Applied;
            MobileCalendarResponseResult? result=null;
            if(authorized)
            {
                var rows=ReadJson<MobileCalendarResponsePlayer[]>(reader.GetString(8));
                result=new(eventId,reader.GetString(6),reader.GetString(7),rows.Take(PageSize).ToArray(),rows.Length>PageSize?offset+PageSize:null);
            }
            return new(reader.GetBoolean(0),reader.GetBoolean(1),reader.GetBoolean(2),decision,result);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }
}
