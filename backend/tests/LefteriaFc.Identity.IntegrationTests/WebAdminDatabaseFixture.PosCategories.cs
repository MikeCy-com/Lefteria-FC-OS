using System.Text.RegularExpressions;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task AssertCategoryAsync(string owner, string id, string name, bool active, long version)
    {
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*)=1 AND bool_and(name=@name AND active=@active AND version=@version)
            FROM pos.categories WHERE organization_id=@owner AND id=@id
            """, connection);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("version", version);
        if (await command.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException("Audited category state differs from committed SQL.");
    }

    public async Task<long> CategoryAuditCountAsync(Account account)
    {
        RequireOwned(account);
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_identity_id=@actor AND actor_realm='admin'
              AND action IN ('admin.pos.category.create','admin.pos.category.update')
            """, connection);
        command.Parameters.AddWithValue("actor", account.Id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task CleanupCreatedCategoryAsync(string owner, string id)
    {
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            !Regex.IsMatch(id, "^cat_[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing unknown POS category cleanup.");
        await using var connection = await OwnerAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM pos.categories WHERE organization_id=@owner AND id=@id", connection);
        command.Parameters.AddWithValue("owner", owner); command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("POS created category was not removed exactly.");
    }

    public async Task WithPosAuditFailureAsync(Account actor, Func<Task> action)
    {
        RequireOwned(actor);
        await using var connection = await OwnerAsync();
        await using (var create = new NpgsqlCommand($"""
            CREATE FUNCTION public.pos_fixture_audit_failure() RETURNS trigger LANGUAGE plpgsql AS $pos_fixture$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid
                 AND NEW.action IN ('admin.pos.category.create','admin.pos.category.update')
              THEN RAISE EXCEPTION 'pos_fixture_atomic_audit_failure'; END IF;
              RETURN NEW;
            END $pos_fixture$;
            CREATE TRIGGER pos_fixture_audit_failure BEFORE INSERT ON audit.security_events
              FOR EACH ROW EXECUTE FUNCTION public.pos_fixture_audit_failure();
            """, connection))
            await create.ExecuteNonQueryAsync();
        try { await action(); }
        finally
        {
            await using var drop = new NpgsqlCommand("""
                DROP TRIGGER pos_fixture_audit_failure ON audit.security_events;
                DROP FUNCTION public.pos_fixture_audit_failure();
                """, connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
