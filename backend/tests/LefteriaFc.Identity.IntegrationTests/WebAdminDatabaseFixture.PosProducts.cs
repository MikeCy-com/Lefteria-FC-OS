using System.Text.RegularExpressions;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task AssertProductAsync(string owner, string id, string category,
        string name, bool active, long version, int variantCount, int priceCents)
    {
        await using var con = await OwnerAsync();
        await using var command = new NpgsqlCommand("""
            SELECT p.category_id,p.name,p.active,p.version,
                   count(v.id),COALESCE(min(v.price_cents),0)
            FROM pos.products p
            LEFT JOIN pos.product_variants v
              ON v.organization_id=p.organization_id AND v.product_id=p.id
            WHERE p.organization_id=@owner AND p.id=@id
            GROUP BY p.category_id,p.name,p.active,p.version
            """,con);
        command.Parameters.AddWithValue("owner",owner);
        command.Parameters.AddWithValue("id",id);
        await using var rows=await command.ExecuteReaderAsync();
        if (!await rows.ReadAsync() || rows.GetString(0)!=category ||
            rows.GetString(1)!=name || rows.GetBoolean(2)!=active ||
            rows.GetInt64(3)!=version || rows.GetInt64(4)!=variantCount ||
            rows.GetInt32(5)!=priceCents || await rows.ReadAsync())
            throw new InvalidOperationException("POS product+variant atomic snapshot differs from SQL.");
    }

    public async Task<long> ProductAuditCountAsync(Account actor)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE actor_identity_id=@actor AND actor_realm='admin'
              AND action IN ('admin.pos.product.create','admin.pos.product.update')
            """,con);
        cmd.Parameters.AddWithValue("actor",actor.Id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task CleanupCreatedProductAsync(string owner,string id)
    {
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            !Regex.IsMatch(id, "^prod_[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing unexpected POS product cleanup.");
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        await using (var variant=new NpgsqlCommand(
            "DELETE FROM pos.product_variants WHERE organization_id=@owner AND product_id=@id",
            con,tx))
        {
            variant.Parameters.AddWithValue("owner",owner);
            variant.Parameters.AddWithValue("id",id);
            if (await variant.ExecuteNonQueryAsync() is < 1 or > 16)
                throw new InvalidOperationException("Unexpected product variant cleanup shape.");
        }
        await using (var product=new NpgsqlCommand(
            "DELETE FROM pos.products WHERE organization_id=@owner AND id=@id",con,tx))
        {
            product.Parameters.AddWithValue("owner",owner);
            product.Parameters.AddWithValue("id",id);
            if (await product.ExecuteNonQueryAsync()!=1)
                throw new InvalidOperationException("Expected one product to be removed.");
        }
        await tx.CommitAsync();
    }

    public async Task WithPosProductAuditFailureAsync(Account actor,Func<Task> action)
    {
        RequireOwned(actor);
        await using var con=await OwnerAsync();
        await using (var cmd=new NpgsqlCommand($"""
            CREATE FUNCTION public.pos_fixture_product_audit_failure() RETURNS trigger
            LANGUAGE plpgsql AS $audit$
            BEGIN
              IF NEW.actor_identity_id='{actor.Id:D}'::uuid
                AND NEW.action IN ('admin.pos.product.create','admin.pos.product.update')
              THEN RAISE EXCEPTION 'synthetic_pos_product_audit_denied'; END IF;
              RETURN NEW;
            END $audit$;
            CREATE TRIGGER pos_fixture_product_audit_failure
              BEFORE INSERT ON audit.security_events FOR EACH ROW
              EXECUTE FUNCTION public.pos_fixture_product_audit_failure();
            """,con))
            await cmd.ExecuteNonQueryAsync();
        try { await action(); }
        finally
        {
            await using var drop=new NpgsqlCommand("""
                DROP TRIGGER pos_fixture_product_audit_failure ON audit.security_events;
                DROP FUNCTION public.pos_fixture_product_audit_failure();
                """,con);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
