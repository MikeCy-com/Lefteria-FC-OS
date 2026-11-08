using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    private string? catalogFixtureId;

    public async Task<string> SeedPosCatalogAsync()
    {
        if (catalogFixtureId is not null) throw new InvalidOperationException("POS fixture already seeded.");
        var id = "posfixture_" + Guid.NewGuid().ToString("N");
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var empty = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pos.categories)=0
               AND (SELECT count(*) FROM pos.products)=0
               AND (SELECT count(*) FROM pos.product_variants)=0
            """, connection, transaction))
            if (await empty.ExecuteScalarAsync() is not true)
                throw new InvalidOperationException("POS fixture requires empty private catalog.");
        foreach (var owner in new[] { "lefteria_fc", "lefteria_fc_academy" })
        {
            await using (var category = new NpgsqlCommand("""
                INSERT INTO pos.categories(organization_id,id,name)
                VALUES (@owner,@id,@name)
                """, connection, transaction))
            {
                category.Parameters.AddWithValue("owner", owner);
                category.Parameters.AddWithValue("id", id);
                category.Parameters.AddWithValue("name", owner == "lefteria_fc" ? "FC catalog category" : "Academy catalog category");
                await category.ExecuteNonQueryAsync();
            }
            await using (var product = new NpgsqlCommand("""
                INSERT INTO pos.products(organization_id,id,category_id,name)
                VALUES (@owner,@id,@id,@name)
                """, connection, transaction))
            {
                product.Parameters.AddWithValue("owner", owner);
                product.Parameters.AddWithValue("id", id);
                product.Parameters.AddWithValue("name", owner == "lefteria_fc" ? "FC fixture item" : "Academy fixture item");
                await product.ExecuteNonQueryAsync();
            }
            await using (var variant = new NpgsqlCommand("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES (@owner,@id,'small','500ml',40,150)
                """, connection, transaction))
            {
                variant.Parameters.AddWithValue("owner", owner);
                variant.Parameters.AddWithValue("id", id);
                await variant.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
        catalogFixtureId = id;
        return id;
    }

    public async Task CleanupPosCatalogAsync(string id)
    {
        if (id != catalogFixtureId) throw new InvalidOperationException("Unexpected POS cleanup key.");
        await using var connection = await OwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var sql in new[] {
            "DELETE FROM pos.product_variants WHERE product_id=@id",
            "DELETE FROM pos.products WHERE id=@id",
            "DELETE FROM pos.categories WHERE id=@id"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteNonQueryAsync() != 2)
                throw new InvalidOperationException("POS fixture cleanup did not affect exactly the two synthetic owners.");
        }
        await transaction.CommitAsync();
        catalogFixtureId = null;
    }
}
