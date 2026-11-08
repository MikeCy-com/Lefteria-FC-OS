using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task<long> AssignmentAuditCountAsync(Account actor)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_identity_id=@id AND actor_realm='admin'
              AND action='admin.roster.membership.replace'
            """,con);
        cmd.Parameters.AddWithValue("id",actor.Id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task WithAssignmentAuditFailureAsync(Account actor,Func<Task> callback)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using(var create=new NpgsqlCommand($"""
            CREATE FUNCTION public.assignment_fixture_reject_audit() RETURNS trigger
            LANGUAGE plpgsql AS $aud$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid
                AND NEW.action='admin.roster.membership.replace'
              THEN RAISE EXCEPTION 'fixture_assignment_audit_unavailable'; END IF;
              RETURN NEW;
            END $aud$;
            CREATE TRIGGER assignment_fixture_reject_audit
              BEFORE INSERT ON audit.security_events FOR EACH ROW
              EXECUTE FUNCTION public.assignment_fixture_reject_audit();
            """,con))
            await create.ExecuteNonQueryAsync();
        try { await callback(); }
        finally
        {
            await using var drop=new NpgsqlCommand("""
                DROP TRIGGER assignment_fixture_reject_audit ON audit.security_events;
                DROP FUNCTION public.assignment_fixture_reject_audit();
                """,con);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
