using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    // Disposable city used to exercise an actual delegated authority.
    public async Task<Guid> SeedManagedRoleTerritoryAsync(Account account)
    {
        RequireOwned(account);
        var city=Guid.NewGuid();
        await using var connection=await OwnerAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var create=new NpgsqlCommand("""
            INSERT INTO club.football_cities(id,name,slug)
              VALUES(@city,'Disposable role-change city',@slug)
            """,connection,transaction))
        {
            create.Parameters.AddWithValue("city",city);
            create.Parameters.AddWithValue("slug","role-"+Guid.NewGuid().ToString("N"));
            await create.ExecuteNonQueryAsync();
        }
        await using(var grant=new NpgsqlCommand("""
            INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role)
              VALUES(@account,@city,'city_manager')
            """,connection,transaction))
        {
            grant.Parameters.AddWithValue("account",account.Id);
            grant.Parameters.AddWithValue("city",city);
            await grant.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return city;
    }

    public async Task ClearManagedRoleTerritoryAsync(Account account,Guid city)
    {
        RequireOwned(account);
        await using var connection=await OwnerAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var remove=new NpgsqlCommand("""
            DELETE FROM club.football_city_admin_grants
              WHERE admin_account_id=@account AND city_id=@city;
            DELETE FROM club.football_cities WHERE id=@city;
            """,connection,transaction))
        {
            remove.Parameters.AddWithValue("account",account.Id);
            remove.Parameters.AddWithValue("city",city);
            await remove.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<long> CountManagedRoleAuditAsync(Account account)
    {
        RequireOwned(account);
        await using var connection=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE action='admin.account.role_change'
              AND subject_digest=md5(@subject::text) AND outcome='succeeded'
            """,connection);
        cmd.Parameters.AddWithValue("subject",account.Id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
