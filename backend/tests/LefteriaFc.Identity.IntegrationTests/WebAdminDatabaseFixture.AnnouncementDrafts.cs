using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task<long> AnnouncementDraftAuditCountAsync(Account actor)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_realm='admin' AND actor_identity_id=@actor
              AND action='admin.announcement.draft.create'
            """,con);
        cmd.Parameters.AddWithValue("actor",actor.Id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<int> AnnouncementDraftCountAsync(string owner,string group)
    {
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*)::integer FROM club.admin_announcement_drafts
            WHERE organization_id=@owner AND group_id=@group
            """,con);
        cmd.Parameters.AddWithValue("owner",owner);
        cmd.Parameters.AddWithValue("group",group);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task WithAnnouncementDraftAuditFailureAsync(Account actor,Func<Task> action)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using(var create=new NpgsqlCommand($"""
            CREATE FUNCTION public.fixture_announcement_audit_failure() RETURNS trigger
            LANGUAGE plpgsql AS $audit$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid
                 AND NEW.action='admin.announcement.draft.create' THEN
                RAISE EXCEPTION 'fixture_audit_denied';
              END IF;
              RETURN NEW;
            END $audit$;
            CREATE TRIGGER fixture_announcement_audit_failure
              BEFORE INSERT ON audit.security_events FOR EACH ROW
              EXECUTE FUNCTION public.fixture_announcement_audit_failure();
            """,con))await create.ExecuteNonQueryAsync();
        try {await action();}
        finally
        {
            await using var drop=new NpgsqlCommand("""
                DROP TRIGGER fixture_announcement_audit_failure ON audit.security_events;
                DROP FUNCTION public.fixture_announcement_audit_failure();
                """,con);
            await drop.ExecuteNonQueryAsync();
        }
    }

    public async Task CleanupAnnouncementDraftsAsync(Account actor)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            DELETE FROM club.admin_announcement_drafts
            WHERE created_by=@actor AND id LIKE 'ann_%'
            """,con);
        cmd.Parameters.AddWithValue("actor",actor.Id);
        var deleted=await cmd.ExecuteNonQueryAsync();
        if(deleted is <0 or >5)
            throw new InvalidOperationException("Unexpected fictional draft cleanup.");
    }
}
