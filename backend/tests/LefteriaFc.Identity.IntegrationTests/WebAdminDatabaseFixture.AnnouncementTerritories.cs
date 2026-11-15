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

    public async Task GrantAnnouncementCityAsync(
        Account manager,AnnouncementTerritoryFixture topology)
    {
        RequireOwned(manager);
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            INSERT INTO club.football_city_admin_grants(
              admin_account_id,city_id,role)
            VALUES(@actor,@city,'city_manager')
            """,con);
        cmd.Parameters.AddWithValue("actor",manager.Id);
        cmd.Parameters.AddWithValue("city",topology.City);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AssertScopedAcademyGroupAsync(
        string groupId,Guid city,Guid location)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(groupId,"^grp_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Unsafe synthetic group ID.");
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*)::integer
            FROM club.academy_groups g
            JOIN club.academy_group_location_assignments m
              ON m.organization_id=g.organization_id AND m.group_id=g.id
            JOIN club.academy_group_profiles p
              ON p.organization_id=g.organization_id AND p.group_id=g.id
            WHERE g.organization_id='lefteria_fc_academy' AND g.id=@id
              AND m.city_id=@city AND m.location_id=@location
              AND p.revision=1 AND NOT p.is_public
            """,con);
        cmd.Parameters.AddWithValue("id",groupId);
        cmd.Parameters.AddWithValue("city",city);
        cmd.Parameters.AddWithValue("location",location);
        if((int)(await cmd.ExecuteScalarAsync())! !=1)
            throw new InvalidOperationException(
              "Cohort, site assignment and original profile were not atomic.");
        await using var publicView=new NpgsqlCommand(
            "SELECT club.read_public_academy_group_directory()::text",con);
        var json=(string?)await publicView.ExecuteScalarAsync() ?? "";
        if(json.Contains(groupId,StringComparison.Ordinal))
            throw new InvalidOperationException(
              "A newly created unapproved Academy group reached the public directory.");
    }

    public async Task<bool> AcademyGroupVisiblePubliclyAsync(string groupId)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(groupId,"^grp_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Refusing unverified Academy public fixture ID.");
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM jsonb_array_elements(
                club.read_public_academy_group_directory()) row
              WHERE row->>'id'=@id
            )
            """,con);
        cmd.Parameters.AddWithValue("id",groupId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task CleanupScopedAcademyGroupsAsync(Account actor,
        IReadOnlyCollection<string> ids)
    {
        RequireOwned(actor);
        if(ids.Count==0)return;
        if(ids.Any(id=>!System.Text.RegularExpressions.Regex.IsMatch(
            id,"^grp_[0-9a-f]{32}$")))
            throw new InvalidOperationException("Refusing unrelated cohort cleanup.");
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("""
            DELETE FROM club.admin_group_create_requests
              WHERE organization_id='lefteria_fc_academy'
                AND actor_id=@actor AND group_id=ANY(@ids);
            DELETE FROM club.academy_group_location_assignments
              WHERE organization_id='lefteria_fc_academy' AND group_id=ANY(@ids);
            DELETE FROM club.academy_group_profiles
              WHERE organization_id='lefteria_fc_academy' AND group_id=ANY(@ids);
            DELETE FROM club.academy_groups
              WHERE organization_id='lefteria_fc_academy' AND id=ANY(@ids);
            """,con,tx);
        cmd.Parameters.AddWithValue("actor",actor.Id);
        cmd.Parameters.AddWithValue("ids",ids.ToArray());
        int affected;
        try { affected=await cmd.ExecuteNonQueryAsync(); }
        catch(PostgresException e)
        {
            // Disposable CI fixture only: no player, administrator or
            // credential values. Identify the exact FK/check preventing
            // synthetic group teardown.
            Console.WriteLine("CHECK scoped create: cleanup SQLSTATE="+e.SqlState+
                " constraint="+e.ConstraintName+" message="+e.MessageText);
            throw;
        }
        // Exactly one retry row, one canonical site assignment, one
        // profile and one Academy group per fixture ID. Npgsql returns the
        // total affected count from all four DELETE statements.
        if(affected!=ids.Count*4)
            throw new InvalidOperationException(
              "Scoped cohort cleanup did not remove all four exact owned rows.");
        await tx.CommitAsync();
    }

    public async Task DiagnoseFixtureFirstTeamProfileScopeAsync(
        Account manager,string teamId,Guid teamScope)
    {
        RequireOwned(manager);
        await using var con=await OwnerAsync();
        await using var session=new NpgsqlCommand("""
            SELECT token_digest FROM identity.admin_sessions
            WHERE admin_account_id=@actor AND revoked_at IS NULL
              AND expires_at>clock_timestamp()
            ORDER BY expires_at DESC LIMIT 1
            """,con);
        session.Parameters.AddWithValue("actor",manager.Id);
        var digest=await session.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("Synthetic manager session missing.");
        await using(var allowed=new NpgsqlCommand("""
            SELECT club.admin_football_allowed(
              @actor,'manager','lefteria_fc',@scope,NULL,false)
            """,con))
        {
            allowed.Parameters.AddWithValue("actor",manager.Id);
            allowed.Parameters.AddWithValue("scope",teamScope);
            Console.WriteLine("CHECK profile scope: direct team manager grant="+
              (await allowed.ExecuteScalarAsync()));
        }
        await using(var guard=new NpgsqlCommand("""
            SELECT club.require_admin_group_profile_scope(
              @digest,'lefteria_fc',@team,false)
            """,con))
        {
            guard.Parameters.AddWithValue("digest",digest);
            guard.Parameters.AddWithValue("team",teamId);
            try{
                var result=await guard.ExecuteScalarAsync();
                Console.WriteLine("CHECK profile scope: owner-direct guard authorized="+
                    (result is Guid));
            }catch(PostgresException e){
                Console.WriteLine("CHECK profile scope: owner-direct guard SQLSTATE="+
                    e.SqlState+" message="+e.MessageText);
            }
        }
    }

    public async Task CleanupTerritoryProfileFixturesAsync(AudienceFixture audience)
    {
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            DELETE FROM club.academy_group_profiles
              WHERE organization_id='lefteria_fc_academy'
                AND group_id=ANY(@academy);
            DELETE FROM club.first_team_profiles
              WHERE organization_id='lefteria_fc' AND team_id=ANY(@teams);
            """,con);
        cmd.Parameters.AddWithValue("academy",new[]{audience.Group1,audience.Group2});
        cmd.Parameters.AddWithValue("teams",new[]{audience.Team1,audience.Team2});
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetAnnouncementSiteActiveAsync(Guid location,bool active)
    {
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            UPDATE club.football_locations SET is_active=@active WHERE id=@location
            """,con);
        cmd.Parameters.AddWithValue("active",active);
        cmd.Parameters.AddWithValue("location",location);
        if(await cmd.ExecuteNonQueryAsync()!=1)
            throw new InvalidOperationException("Expected one disposable Academy location.");
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
