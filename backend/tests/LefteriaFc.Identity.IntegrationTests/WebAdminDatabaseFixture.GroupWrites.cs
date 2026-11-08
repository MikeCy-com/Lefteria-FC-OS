using System.Text.RegularExpressions;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task<long> GroupCreationAuditCountAsync(Account actor)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_identity_id=@id AND actor_realm='admin'
              AND action IN ('admin.roster.team.create','admin.roster.academy_group.create')
            """,con);
        cmd.Parameters.AddWithValue("id",actor.Id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<long> SyntheticGroupCountAsync(string owner,string name)
    {
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new InvalidOperationException("Invalid fixture owner.");
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand(owner=="lefteria_fc"
            ? "SELECT count(*) FROM club.teams WHERE organization_id=@owner AND name=@name"
            : "SELECT count(*) FROM club.academy_groups WHERE organization_id=@owner AND name=@name",con);
        cmd.Parameters.AddWithValue("owner",owner);
        cmd.Parameters.AddWithValue("name",name);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task CleanupCreatedRosterGroupAsync(string owner,string id)
    {
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            !Regex.IsMatch(id,"^grp_[0-9a-f]{32}$",RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing unknown group cleanup.");
        await using var con=await OwnerAsync();
        var sql=owner=="lefteria_fc"
            ? "DELETE FROM club.teams WHERE organization_id=@owner AND id=@id"
            : "DELETE FROM club.academy_groups WHERE organization_id=@owner AND id=@id";
        await using var cmd=new NpgsqlCommand(sql,con);
        cmd.Parameters.AddWithValue("owner",owner);
        cmd.Parameters.AddWithValue("id",id);
        if(await cmd.ExecuteNonQueryAsync()!=1)
            throw new InvalidOperationException("One synthetic created group was not removed.");
    }

    public async Task WithGroupAuditFailureAsync(Account actor,Func<Task> callback)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using(var create=new NpgsqlCommand($"""
            CREATE FUNCTION public.group_fixture_reject_audit() RETURNS trigger
            LANGUAGE plpgsql AS $aud$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid AND
                NEW.action IN ('admin.roster.team.create','admin.roster.academy_group.create')
              THEN RAISE EXCEPTION 'fixture_group_audit_unavailable'; END IF;
              RETURN NEW;
            END $aud$;
            CREATE TRIGGER group_fixture_reject_audit
              BEFORE INSERT ON audit.security_events FOR EACH ROW
              EXECUTE FUNCTION public.group_fixture_reject_audit();
            """,con))
            await create.ExecuteNonQueryAsync();
        try { await callback(); }
        finally
        {
            await using var drop=new NpgsqlCommand("""
                DROP TRIGGER group_fixture_reject_audit ON audit.security_events;
                DROP FUNCTION public.group_fixture_reject_audit();
                """,con);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
