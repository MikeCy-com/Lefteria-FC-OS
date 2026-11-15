using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    internal sealed record AnnouncementTerritoryFixture(
        Guid FirstTeamScope,Guid SecondTeamScope,Guid AcademyScope,
        Guid City,Guid FirstLocation,Guid SecondLocation);

    // Disposable, canonical, two-location topology. A city may share its
    // opponents and fields, but permission to communicate with children
    // remains restricted to each explicitly assigned location.
    public async Task<AnnouncementTerritoryFixture> SeedAnnouncementTerritoriesAsync(
        AudienceFixture audience,Account manager)
    {
        RequireOwned(manager);
        var topology=new AnnouncementTerritoryFixture(
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var slug="notice-"+Guid.NewGuid().ToString("N");
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        async Task Add(string sql,params (string Key,object Value)[] args)
        {
            await using var cmd=new NpgsqlCommand(sql,con,tx);
            foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Add("""
            INSERT INTO club.football_cities(id,name,slug)
              VALUES(@city,'Fixture notices city',@slug)
            """,("city",topology.City),("slug",slug));
        foreach(var (id,siteSlug) in new[]{
            (topology.FirstLocation,"first"),(topology.SecondLocation,"second")})
        {
            await Add("""
                INSERT INTO club.football_locations(id,city_id,name,slug)
                  VALUES(@id,@city,'Fixture notice site',@slug)
                """,("id",id),("city",topology.City),("slug",siteSlug));
        }
        await Add("""
            INSERT INTO club.football_operational_scopes(
              id,organization_id,team_id)
            VALUES(@id,'lefteria_fc',@team)
            """,("id",topology.FirstTeamScope),("team",audience.Team1));
        await Add("""
            INSERT INTO club.football_operational_scopes(
              id,organization_id,team_id)
            VALUES(@id,'lefteria_fc',@team)
            """,("id",topology.SecondTeamScope),("team",audience.Team2));
        await Add("""
            INSERT INTO club.football_operational_scopes(
              id,organization_id,academy_city_id)
            VALUES(@id,'lefteria_fc_academy',@city)
            """,("id",topology.AcademyScope),("city",topology.City));
        foreach(var (group,location) in new[]{
            (audience.Group1,topology.FirstLocation),
            (audience.Group2,topology.SecondLocation)})
        {
            await Add("""
                INSERT INTO club.academy_group_location_assignments(
                  organization_id,group_id,city_id,location_id,scope_id)
                VALUES('lefteria_fc_academy',@group,@city,@location,@scope)
                """,("group",group),("city",topology.City),
                ("location",location),("scope",topology.AcademyScope));
        }
        await Add("""
            INSERT INTO club.football_team_admin_grants(
              admin_account_id,scope_id,organization_id,role)
            VALUES(@actor,@scope,'lefteria_fc','team_manager')
            """,("actor",manager.Id),("scope",topology.FirstTeamScope));
        await Add("""
            INSERT INTO club.football_location_admin_grants(
              admin_account_id,location_id,role)
            VALUES(@actor,@location,'location_manager')
            """,("actor",manager.Id),("location",topology.SecondLocation));
        await tx.CommitAsync();
        return topology;
    }

    public async Task RevokeAnnouncementTerritoriesAsync(Account manager,
        AnnouncementTerritoryFixture topology)
    {
        RequireOwned(manager);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            DELETE FROM club.football_team_admin_grants
              WHERE admin_account_id=@actor AND scope_id=@team AND role='team_manager';
            DELETE FROM club.football_location_admin_grants
              WHERE admin_account_id=@actor AND location_id=@location
                AND role='location_manager';
            """,con);
        cmd.Parameters.AddWithValue("actor",manager.Id);
        cmd.Parameters.AddWithValue("team",topology.FirstTeamScope);
        cmd.Parameters.AddWithValue("location",topology.SecondLocation);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CleanupAnnouncementTerritoriesAsync(Account manager,
        AnnouncementTerritoryFixture topology)
    {
        RequireOwned(manager);
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("""
            DELETE FROM club.football_team_admin_grants WHERE admin_account_id=@actor
              AND scope_id=ANY(@fc);
            DELETE FROM club.football_location_admin_grants WHERE admin_account_id=@actor
              AND location_id=ANY(@sites);
            DELETE FROM club.football_city_admin_grants WHERE admin_account_id=@actor
              AND city_id=@city;
            DELETE FROM club.academy_group_location_assignments
              WHERE scope_id=@academy AND location_id=ANY(@sites);
            DELETE FROM club.football_operational_scopes WHERE id=ANY(@allscopes);
            DELETE FROM club.football_locations WHERE city_id=@city AND id=ANY(@sites);
            DELETE FROM club.football_cities WHERE id=@city;
            """,con,tx);
        cmd.Parameters.AddWithValue("actor",manager.Id);
        cmd.Parameters.AddWithValue("fc",new[]{topology.FirstTeamScope,topology.SecondTeamScope});
        cmd.Parameters.AddWithValue("academy",topology.AcademyScope);
        cmd.Parameters.AddWithValue("allscopes",
            new[]{topology.FirstTeamScope,topology.SecondTeamScope,topology.AcademyScope});
        cmd.Parameters.AddWithValue("sites",new[]{topology.FirstLocation,topology.SecondLocation});
        cmd.Parameters.AddWithValue("city",topology.City);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
