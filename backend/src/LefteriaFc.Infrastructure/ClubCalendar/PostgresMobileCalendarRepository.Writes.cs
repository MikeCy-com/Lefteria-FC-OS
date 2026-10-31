using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed partial class PostgresMobileCalendarRepository
{
    public async Task<CalendarWriteSnapshot> SubmitAvailabilityAsync(MobileSession session,MobileAvailabilityCommand input,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            // Each authorization join and both legacy-compatible RSVP rows are
            // evaluated by one statement. Physical attendance is never changed.
            // PostgreSQL authorization uses this statement's committed snapshot;
            // no UPDATE privilege on canonical roster/staff is needed for locks.
            var sql=OperationalScopeSql.Ctes+"\n"+CalendarAccessSql.SelectionCtes+"\n"+"""
                , targets AS (SELECT * FROM club.calendar_items WHERE id=@event AND (@kind IS NULL OR kind=@kind)),
                permitted AS (
                  SELECT c.kind,c.id,c.organization_id,p.id AS player_id
                  FROM targets c JOIN chosen_player p ON p.organization_id=c.organization_id
                  WHERE (SELECT count(*) FROM targets)=1 AND @role IN ('parent','player')
                    AND EXISTS(SELECT 1 FROM bound WHERE role_available) AND EXISTS(SELECT 1 FROM ready)
                    AND NOT c.is_archived AND NOT c.is_cancelled AND c.member_projection_json IS NOT NULL AND
                """+"\n"+CalendarAccessSql.PlayerEligible+"\n"+"""
                ), availability_write AS (
                  INSERT INTO club.calendar_responses(collection,kind,event_id,player_id,organization_id,status,actor_identity_id,updated_at)
                  SELECT 'availability',p.kind,p.id,p.player_id,p.organization_id,@status,@identity,statement_timestamp() FROM permitted p
                  ON CONFLICT(collection,kind,event_id,player_id) DO UPDATE SET
                    status=EXCLUDED.status,actor_identity_id=EXCLUDED.actor_identity_id,updated_at=EXCLUDED.updated_at
                  RETURNING kind,event_id,player_id
                ), response_write AS (
                  INSERT INTO club.calendar_responses(collection,kind,event_id,player_id,organization_id,status,actor_identity_id,updated_at)
                  SELECT 'event_attendance',p.kind,p.id,p.player_id,p.organization_id,@status,@identity,statement_timestamp() FROM permitted p
                  WHERE EXISTS(SELECT 1 FROM availability_write)
                  ON CONFLICT(collection,kind,event_id,player_id) DO UPDATE SET
                    status=EXCLUDED.status,actor_identity_id=EXCLUDED.actor_identity_id,updated_at=EXCLUDED.updated_at
                  RETURNING kind,event_id,player_id
                ), audit_write AS (
                  INSERT INTO audit.security_events(id,occurred_at,action,outcome,actor_identity_id,device_session_id,organization_id,reason_code)
                  SELECT @audit,statement_timestamp(),'mobile.calendar.availability','success',@identity,@sid,p.organization_id,'own_player_rsvp'
                  FROM permitted p WHERE EXISTS(SELECT 1 FROM response_write)
                  RETURNING 1 AS written
                )
                SELECT
                """+"\n"+CalendarAccessSql.Metadata+"\n"+"""
                  ,(SELECT count(*) FROM targets),EXISTS(SELECT 1 FROM audit_write),
                  coalesce((SELECT bool_or(is_cancelled OR is_archived) FROM targets),false),
                  (SELECT player_id FROM response_write),(SELECT kind FROM response_write)
                """;
            await using var command=Command(sql,session,input.PlayerId,input.EventId,input.EventType);
            command.Parameters.AddWithValue("status",input.Status);command.Parameters.AddWithValue("audit",Guid.NewGuid());
            await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            var count=reader.GetInt64(4);var written=reader.GetBoolean(5);var cancelled=reader.GetBoolean(6);
            var decision=written?CalendarWriteDecision.Applied:count==0?CalendarWriteDecision.NotFound:
                count>1?CalendarWriteDecision.Ambiguous:cancelled?CalendarWriteDecision.Cancelled:CalendarWriteDecision.NotAssigned;
            return new(reader.GetBoolean(0),reader.GetBoolean(1),reader.GetBoolean(2),decision,OptionalString(reader,7),OptionalString(reader,8));
        }
        finally{await database.Database.CloseConnectionAsync();}
    }
}
