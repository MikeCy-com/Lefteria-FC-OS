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

    public async Task<int> AppAnnouncementCountAsync(string owner)
    {
        await using var con=await OwnerAsync();
        await using var command=new NpgsqlCommand("""
            SELECT count(*)::integer FROM club.app_announcements
            WHERE organization_id=@owner AND removed_at IS NULL
            """,con);
        command.Parameters.AddWithValue("owner",owner);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    public async Task<long> AppAnnouncementAuditCountAsync(Account actor,string action)
    {
        RequireOwned(actor);
        if(action is not ("admin.app.announcement.publish" or "admin.app.announcement.remove"))
            throw new InvalidOperationException("Unexpected announcement audit operation.");
        await using var con=await OwnerAsync();
        await using var command=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_realm='admin' AND actor_identity_id=@actor AND action=@action
            """,con);
        command.Parameters.AddWithValue("actor",actor.Id);
        command.Parameters.AddWithValue("action",action);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public Task WithAppAnnouncementAuditFailureAsync(Account actor,string operation,Func<Task> action)
    {
        if(operation is not ("admin.app.announcement.publish" or "admin.app.announcement.remove"))
            throw new InvalidOperationException("Unexpected published announcement audit operation.");
        return WithAnnouncementDraftAuditFailureAsync(actor,action,operation);
    }

    public async Task WithAnnouncementDraftAuditFailureAsync(Account actor,Func<Task> action,string auditAction="admin.announcement.draft.create")
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using(var create=new NpgsqlCommand($"""
            CREATE FUNCTION public.fixture_announcement_audit_failure() RETURNS trigger
            LANGUAGE plpgsql AS $audit$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid
                 AND NEW.action='{auditAction}' THEN
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
            DELETE FROM club.app_announcement_media
            WHERE announcement_id IN
              (SELECT id FROM club.admin_announcement_drafts
               WHERE created_by=@actor AND id LIKE 'ann_%');
            DELETE FROM club.app_announcements
            WHERE published_by=@actor AND id LIKE 'ann_%';
            DELETE FROM club.admin_announcement_drafts
            WHERE created_by=@actor AND id LIKE 'ann_%'
            """,con);
        cmd.Parameters.AddWithValue("actor",actor.Id);
        var deleted=await cmd.ExecuteNonQueryAsync();
        // Two bounded media rows (one FC PDF, one Academy banner) now
        // accompany the existing at-most-five owner-owned notice rows.
        if(deleted is <0 or >7)
            throw new InvalidOperationException("Unexpected fictional draft cleanup.");
    }
}
