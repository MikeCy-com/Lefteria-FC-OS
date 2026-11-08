using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    // Every row is synthetic and transaction-rolled back on the exact disposable
    // PostgreSQL fixture. This is not a checkout, payment or catalog activation.
    public async Task AssertPosCatalogFoundationAsync()
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString, fixture.AdminUsername);
        FixtureConfiguration.ValidateConnection(fixture.AppConnectionString, fixture.AppUsername);
        await fixture.VerifyServerAsync();

        await using (var app = new NpgsqlConnection(fixture.AppConnectionString))
        {
            await app.OpenAsync();
            await using var restricted = new NpgsqlCommand("""
                SELECT NOT has_schema_privilege(current_user,'pos','USAGE'),
                       NOT has_schema_privilege(current_user,'pos','CREATE'),
                       (SELECT count(*)=3 AND bool_and(
                            rel.relrowsecurity
                            AND NOT has_table_privilege(current_user,rel.oid,'SELECT')
                            AND NOT has_table_privilege(current_user,rel.oid,'INSERT')
                            AND NOT has_table_privilege(current_user,rel.oid,'UPDATE')
                            AND NOT has_table_privilege(current_user,rel.oid,'DELETE'))
                          FROM pg_catalog.pg_class rel
                          JOIN pg_catalog.pg_namespace ns ON ns.oid=rel.relnamespace
                          WHERE ns.nspname='pos'
                            AND rel.relname IN ('categories','products','product_variants'))
                """, app);
            await using var check = await restricted.ExecuteReaderAsync();
            Check.True(await check.ReadAsync(), "Private POS runtime security snapshot missing.");
            for (var index=0; index<3; index++)
                Check.True(check.GetBoolean(index),
                    "The runtime unexpectedly acquired POS catalog access.");
        }

        var fcCategory = "fc-category-" + Guid.NewGuid().ToString("N");
        var academyCategory = "academy-category-" + Guid.NewGuid().ToString("N");
        var fcProduct = "fc-product-" + Guid.NewGuid().ToString("N");
        var academyProduct = "academy-product-" + Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            async Task ExecAsync(string sql, params (string Name, object Value)[] values)
            {
                await using var cmd = new NpgsqlCommand(sql,connection,transaction);
                foreach (var (name,value) in values) cmd.Parameters.AddWithValue(name,value);
                await cmd.ExecuteNonQueryAsync();
            }

            async Task RejectAsync(string sql, string state, params (string Name, object Value)[] values)
            {
                await ExecAsync("SAVEPOINT pos_failure");
                var rejected=false;
                try { await ExecAsync(sql,values); }
                catch (PostgresException error) when (error.SqlState==state) { rejected=true; }
                finally { await ExecAsync("ROLLBACK TO SAVEPOINT pos_failure; RELEASE SAVEPOINT pos_failure"); }
                Check.True(rejected,"POS catalog unexpectedly accepted invalid data.");
            }

            await ExecAsync("""
                INSERT INTO pos.categories(organization_id,id,name)
                VALUES ('lefteria_fc',@id,'Fixture FC category')
                """,("id",fcCategory));
            await ExecAsync("""
                INSERT INTO pos.categories(organization_id,id,name)
                VALUES ('lefteria_fc_academy',@id,'Fixture Academy category')
                """,("id",academyCategory));
            await ExecAsync("""
                INSERT INTO pos.products(organization_id,id,category_id,name)
                VALUES ('lefteria_fc',@id,@category,'Fixture FC product')
                """,("id",fcProduct),("category",fcCategory));
            await ExecAsync("""
                INSERT INTO pos.products(organization_id,id,category_id,name)
                VALUES ('lefteria_fc_academy',@id,@category,'Fixture Academy product')
                """,("id",academyProduct),("category",academyCategory));

            await ExecAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc',@product,'small','500ml',40,150)
                """,("product",fcProduct));
            await ExecAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc_academy',@product,'small','500ml',40,150)
                """,("product",academyProduct));

            // A matching legacy code is allowed in separate owners only.
            await using (var aggregate = new NpgsqlCommand("""
                SELECT
                  (SELECT count(*) FROM pos.categories WHERE id=ANY(@cats)),
                  (SELECT count(*) FROM pos.products WHERE id=ANY(@products)),
                  (SELECT count(*) FROM pos.product_variants WHERE product_id=ANY(@products)),
                  (SELECT count(*) FROM pos.products
                   WHERE organization_id='lefteria_fc' AND category_id=@academy_category)
                """,connection,transaction))
            {
                aggregate.Parameters.AddWithValue("cats",new[] {fcCategory,academyCategory});
                aggregate.Parameters.AddWithValue("products",new[] {fcProduct,academyProduct});
                aggregate.Parameters.AddWithValue("academy_category",academyCategory);
                await using var read = await aggregate.ExecuteReaderAsync();
                Check.True(await read.ReadAsync(),"POS catalog fixture counts unavailable.");
                Check.Equal(2L,read.GetInt64(0),"POS categories not independently scoped.");
                Check.Equal(2L,read.GetInt64(1),"POS products not independently scoped.");
                Check.Equal(2L,read.GetInt64(2),"POS variants not independently scoped.");
                Check.Equal(0L,read.GetInt64(3),"FC product referenced Academy category.");
            }

            await RejectAsync("""
                INSERT INTO pos.products(organization_id,id,category_id,name)
                VALUES ('lefteria_fc',@id,@category,'Cross owner product')
                """,PostgresErrorCodes.ForeignKeyViolation,
                ("id","cross-"+Guid.NewGuid().ToString("N")),("category",academyCategory));
            await RejectAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc_academy',@product,'cross','Cross owner',40,150)
                """,PostgresErrorCodes.ForeignKeyViolation,("product",fcProduct));
            await RejectAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc',@product,'zero','Free checkout',10,0)
                """,PostgresErrorCodes.CheckViolation,("product",fcProduct));
            await RejectAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc',@product,'excess','Overflow',10,10000001)
                """,PostgresErrorCodes.CheckViolation,("product",fcProduct));
            await RejectAsync("""
                INSERT INTO pos.product_variants(organization_id,product_id,id,name,cost_cents,price_cents)
                VALUES ('lefteria_fc',@product,'small','Duplicate variant',10,150)
                """,PostgresErrorCodes.UniqueViolation,("product",fcProduct));
            await RejectAsync("""
                INSERT INTO pos.categories(organization_id,id,name)
                VALUES ('other-club',@id,'Unknown club')
                """,PostgresErrorCodes.CheckViolation,
                ("id","invalid-"+Guid.NewGuid().ToString("N")));

            await transaction.RollbackAsync();
        }

        // Exact synthetic keys must leave no rows after rollback, even after
        // failed constraints that would otherwise abort the SQL transaction.
        await using (var verify = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pos.categories WHERE id=ANY(@cats)),
                   (SELECT count(*) FROM pos.products WHERE id=ANY(@products)),
                   (SELECT count(*) FROM pos.product_variants WHERE product_id=ANY(@products))
            """, connection))
        {
            verify.Parameters.AddWithValue("cats",new[] {fcCategory,academyCategory});
            verify.Parameters.AddWithValue("products",new[] {fcProduct,academyProduct});
            await using var reader=await verify.ExecuteReaderAsync();
            Check.True(await reader.ReadAsync(),"POS catalog rollback check unavailable.");
            for (var column=0; column<3; column++)
                Check.Equal(0L,reader.GetInt64(column),
                    "Synthetic private POS catalog row survived fixture rollback.");
        }
    }
}
