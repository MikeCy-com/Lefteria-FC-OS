using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed partial class PostgresMobileCalendarRepository
{
    public async Task<CalendarWriteSnapshot> SubmitDelegatedAvailabilityAsync(
        MobileSession session,MobileAvailabilityCommand input,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            // Authorization and both RSVP writes share ONE PostgreSQL
            // statement snapshot: revocation between detached identity
            // preflight and this query denies the entire mutation.
            var sql="""
                WITH ready AS (
                  SELECT time_zone FROM club.calendar_import_state
                  WHERE id='initial' AND is_complete
                ), delegated AS (
                  SELECT live.player_id,live.organization_id
                  FROM identity.read_current_family_delegation(
                    @identity,@phone,@role,@expires) live
                  WHERE identity.current_family_calendar_scope(
                    @identity,@phone,@role,@expires,
                    live.player_id,live.organization_id)
                  LIMIT 2
                ), chosen_player AS (
                  SELECT p.id,p.organization_id,p.team_id
                  FROM delegated d JOIN club.players p ON p.id=d.player_id
                    AND p.organization_id=d.organization_id
                  WHERE (SELECT count(*) FROM delegated)=1
                ), targets AS (
                  SELECT * FROM club.calendar_items
                  WHERE id=@event AND (@kind IS NULL OR kind=@kind)
                ), permitted AS (
                  SELECT c.kind,c.id,c.organization_id,p.id AS player_id
                  FROM targets c JOIN chosen_player p
                    ON p.organization_id=c.organization_id
                  WHERE @role='fan' AND (SELECT count(*) FROM targets)=1
                    AND EXISTS(SELECT 1 FROM ready)
                    AND NOT c.is_archived AND NOT c.is_cancelled
                    AND c.member_projection_json IS NOT NULL AND
                """+"\n"+CalendarAccessSql.PlayerEligible+"\n"+"""
                ), availability_write AS (
                  INSERT INTO club.calendar_responses(
                    collection,kind,event_id,player_id,organization_id,status,
                    actor_identity_id,updated_at)
                  SELECT 'availability',p.kind,p.id,p.player_id,
                    p.organization_id,@status,@identity,statement_timestamp()
                  FROM permitted p
                  ON CONFLICT(collection,kind,event_id,player_id) DO UPDATE SET
                    status=EXCLUDED.status,actor_identity_id=EXCLUDED.actor_identity_id,
                    updated_at=EXCLUDED.updated_at
                  RETURNING kind,event_id,player_id
                ), response_write AS (
                  INSERT INTO club.calendar_responses(
                    collection,kind,event_id,player_id,organization_id,status,
                    actor_identity_id,updated_at)
                  SELECT 'event_attendance',p.kind,p.id,p.player_id,
                    p.organization_id,@status,@identity,statement_timestamp()
                  FROM permitted p WHERE EXISTS(SELECT 1 FROM availability_write)
                  ON CONFLICT(collection,kind,event_id,player_id) DO UPDATE SET
                    status=EXCLUDED.status,actor_identity_id=EXCLUDED.actor_identity_id,
                    updated_at=EXCLUDED.updated_at
                  RETURNING kind,event_id,player_id
                ), audit_write AS (
                  INSERT INTO audit.security_events(
                    id,occurred_at,action,outcome,actor_identity_id,
                    device_session_id,organization_id,reason_code)
                  SELECT @audit,statement_timestamp(),
                    'mobile.calendar.availability','success',@identity,@sid,
                    p.organization_id,'delegated_family_rsvp'
                  FROM permitted p WHERE EXISTS(SELECT 1 FROM response_write)
                  RETURNING 1 AS written
                )
                SELECT
                  EXISTS(SELECT 1 FROM identity.mobile_identities identity_row
                    WHERE identity_row.id=@identity AND
                      identity_row.normalized_phone=@phone
                      AND identity_row.role='fan' AND identity_row.is_active
                      AND @role='fan' AND @expires>statement_timestamp()),
                  EXISTS(SELECT 1 FROM ready),
                  EXISTS(SELECT 1 FROM chosen_player),
                  (SELECT count(*) FROM targets),
                  EXISTS(SELECT 1 FROM audit_write),
                  coalesce((SELECT bool_or(is_cancelled OR is_archived)
                    FROM targets),false),
                  (SELECT player_id FROM response_write),
                  (SELECT kind FROM response_write)
                """;
            await using var command=Command(sql,session,eventId:input.EventId,kind:input.EventType);
            command.Parameters.AddWithValue("status",input.Status);
            command.Parameters.AddWithValue("audit",Guid.NewGuid());
            await using var reader=await command.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            var count=reader.GetInt64(3);
            var written=reader.GetBoolean(4);
            var cancelled=reader.GetBoolean(5);
            var decision=written?CalendarWriteDecision.Applied:
                count==0?CalendarWriteDecision.NotFound:
                count>1?CalendarWriteDecision.Ambiguous:
                cancelled?CalendarWriteDecision.Cancelled:CalendarWriteDecision.NotAssigned;
            return new(reader.GetBoolean(0),reader.GetBoolean(1),reader.GetBoolean(2),
                decision,OptionalString(reader,6),OptionalString(reader,7));
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

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
