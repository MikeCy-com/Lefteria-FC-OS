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

    // A dormant cashier assignment must also block role reinterpretation.
    public async Task<string> SeedManagedRolePosAssignmentAsync(Account account)
    {
        RequireOwned(account);
        var id="role"+Guid.NewGuid().ToString("N");
        await using var connection=await OwnerAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var cmd=new NpgsqlCommand("""
            INSERT INTO pos.cities(organization_id,id,name)
              VALUES('lefteria_fc',@id,'Disposable role POS city');
            INSERT INTO pos.locations(organization_id,id,city_id,code,name)
              VALUES('lefteria_fc',@id,@id,@code,'Disposable role POS location');
            INSERT INTO pos_cashier.registers(organization_id,location_id,id,label)
              VALUES('lefteria_fc',@id,@id,'Disposable role POS register');
            INSERT INTO pos_cashier.cashier_assignments(
              organization_id,register_id,admin_account_id,is_active)
              VALUES('lefteria_fc',@id,@account,true);
            """,connection,transaction))
        {
            cmd.Parameters.AddWithValue("id",id);
            cmd.Parameters.AddWithValue("code","role-"+Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("account",account.Id);
            await cmd.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return id;
    }

    public async Task DeactivateManagedRolePosAssignmentAsync(Account account,string id)
    {
        RequireOwned(account);
        await using var connection=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            UPDATE pos_cashier.cashier_assignments SET is_active=false
              WHERE organization_id='lefteria_fc'
                AND register_id=@id AND admin_account_id=@account
                AND is_active
            """,connection);
        cmd.Parameters.AddWithValue("id",id);
        cmd.Parameters.AddWithValue("account",account.Id);
        if(await cmd.ExecuteNonQueryAsync()!=1)
            throw new InvalidOperationException("POS role fixture did not revoke exactly one active cashier assignment.");
    }

    public async Task ClearManagedRolePosAssignmentAsync(Account account,string id)
    {
        RequireOwned(account);
        await using var connection=await OwnerAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var cmd=new NpgsqlCommand("""
            DELETE FROM pos_cashier.cashier_assignments
              WHERE organization_id='lefteria_fc' AND register_id=@id
                AND admin_account_id=@account;
            DELETE FROM pos_cashier.registers
              WHERE organization_id='lefteria_fc' AND id=@id;
            DELETE FROM pos.locations
              WHERE organization_id='lefteria_fc' AND id=@id;
            DELETE FROM pos.cities
              WHERE organization_id='lefteria_fc' AND id=@id;
            """,connection,transaction))
        {
            cmd.Parameters.AddWithValue("id",id);
            cmd.Parameters.AddWithValue("account",account.Id);
            await cmd.ExecuteNonQueryAsync();
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
