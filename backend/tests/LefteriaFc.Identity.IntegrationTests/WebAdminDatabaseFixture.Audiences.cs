using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    internal sealed record AudienceFixture(string Prefix, bool OwnsReadiness)
    {
        public string Team1 => Prefix+"-team1";
        public string Team2 => Prefix+"-team2";
        public string Group1 => Prefix+"-group1";
        public string Group2 => Prefix+"-group2";
        public string Player(int n) => Prefix+"-player"+n;
        public string Digest => string.Concat(Enumerable.Repeat(Prefix[4..],2))[..64];
    }

    private AudienceFixture? currentAudience;

    public async Task<AudienceFixture> SeedAudienceAsync()
    {
        if(currentAudience is not null) throw new InvalidOperationException("A fixture audience already exists.");
        var prefix="aud-"+Guid.NewGuid().ToString("N");
        await using var connection=await OwnerAsync();
        await using var tx=await connection.BeginTransactionAsync();

        async Task Execute(string sql,params (string Key,object Value)[] args)
        {
            await using var command=new NpgsqlCommand(sql,connection,tx);
            foreach(var (key,value) in args)command.Parameters.AddWithValue(key,value);
            await command.ExecuteNonQueryAsync();
        }

        await using var check=new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')",connection,tx);
        var ready=await check.ExecuteScalarAsync() is true;
        var fixture=new AudienceFixture(prefix,!ready);

        for(var n=1;n<=2;n++)
        {
            await Execute("""
                INSERT INTO club.teams(id,organization_id,name)
                VALUES (@id,'lefteria_fc',@name)
                """,("id",n==1?fixture.Team1:fixture.Team2),
                ("name",n==1?"Fixture first team":"Fixture second team"));
            await Execute("""
                INSERT INTO club.academy_groups(id,organization_id,name)
                VALUES (@id,'lefteria_fc_academy',@name)
                """,("id",n==1?fixture.Group1:fixture.Group2),
                ("name",n==1?"Fixture Academy Alpha":"Fixture Academy Beta"));
        }
        for(var n=1;n<=7;n++)
        {
            var owner=n<=3?"lefteria_fc":"lefteria_fc_academy";
            var active=n is not (2 or 5);
            var team=n is 1 or 2 ? fixture.Team1 : null;
            var group=n is 4 or 5 ? fixture.Group1 : null;
            await using var command=new NpgsqlCommand("""
                INSERT INTO club.players(
                  id,organization_id,name,number,position,team_type,
                  team_id,primary_academy_group_id,
                  is_active,is_archived,is_transferred,has_explicit_picker_fields)
                VALUES(@id,@owner,@name,@number,'Midfielder',@type,
                  @team,@group,@active,false,false,true)
                """,connection,tx);
            command.Parameters.AddWithValue("id",fixture.Player(n));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("name","Private synthetic roster child "+n);
            command.Parameters.AddWithValue("number",n);
            command.Parameters.AddWithValue("type",n<=3?"First Team":"Academy");
            command.Parameters.Add(new NpgsqlParameter("team",NpgsqlTypes.NpgsqlDbType.Text)
                {Value=(object?)team ?? DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("group",NpgsqlTypes.NpgsqlDbType.Text)
                {Value=(object?)group ?? DBNull.Value});
            command.Parameters.AddWithValue("active",active);
            await command.ExecuteNonQueryAsync();
        }
        foreach(var (player,group) in new[]{
            (fixture.Player(4),fixture.Group1),
            (fixture.Player(4),fixture.Group2),
            (fixture.Player(6),fixture.Group2)
        })
        {
            await Execute("""
                INSERT INTO club.player_academy_groups(organization_id,player_id,academy_group_id)
                VALUES('lefteria_fc_academy',@player,@group)
                """,("player",player),("group",group));
        }

        if(!ready)
        {
            await Execute("""
                INSERT INTO club.roster_import_state(
                  id,digest,source_repository,source_commit,environment,
                  teams_count,academy_groups_count,players_count,memberships_count,applied_at)
                VALUES('initial',@digest,'fixture-canonical-roster',@commit,
                  'development',2,2,7,3,clock_timestamp())
                """,("digest",fixture.Digest),("commit",prefix[4..]+"00000000"));
        }
        await tx.CommitAsync();
        currentAudience=fixture;
        return fixture;
    }

    public async Task RemoveAudienceMembershipAsync(AudienceFixture fixture)
    {
        if(fixture!=currentAudience)throw new InvalidOperationException("Unknown audience fixture.");
        await using var con=await OwnerAsync();
        await using var command=new NpgsqlCommand("""
            DELETE FROM club.player_academy_groups
            WHERE organization_id='lefteria_fc_academy' AND player_id=@player
              AND academy_group_id=@group
            """,con);
        command.Parameters.AddWithValue("player",fixture.Player(6));
        command.Parameters.AddWithValue("group",fixture.Group2);
        if(await command.ExecuteNonQueryAsync()!=1)
            throw new InvalidOperationException("Expected one specific membership edit.");
    }

    public async Task CleanupAudienceAsync(AudienceFixture fixture)
    {
        if(fixture!=currentAudience)throw new InvalidOperationException("Refusing unrelated audience cleanup.");
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        var ids=Enumerable.Range(1,7).Select(fixture.Player).ToArray();
        async Task Delete(string sql,int count)
        {
            await using var cmd=new NpgsqlCommand(sql,con,tx);
            cmd.Parameters.AddWithValue("ids",ids);
            cmd.Parameters.AddWithValue("teams",new[]{fixture.Team1,fixture.Team2});
            cmd.Parameters.AddWithValue("groups",new[]{fixture.Group1,fixture.Group2});
            var affected=await cmd.ExecuteNonQueryAsync();
            // The read-only audience check removes one membership, while
            // group-creation checks leave all three. Both are exact keys
            // owned by this disposable fixture, never imported child rows.
            if ((count == -1 && affected != 2 && affected != 3) ||
                (count != -1 && affected != count))
                throw new InvalidOperationException("Unexpected synthetic audience cleanup row count.");
        }
        await Delete("DELETE FROM club.player_academy_groups WHERE player_id=ANY(@ids)",-1);
        await Delete("DELETE FROM club.players WHERE id=ANY(@ids)",7);
        await Delete("DELETE FROM club.teams WHERE id=ANY(@teams)",2);
        await Delete("DELETE FROM club.academy_groups WHERE id=ANY(@groups)",2);
        if(fixture.OwnsReadiness)
        {
            await using var state=new NpgsqlCommand(
              "DELETE FROM club.roster_import_state WHERE id='initial' AND digest=@digest",con,tx);
            state.Parameters.AddWithValue("digest",fixture.Digest);
            if(await state.ExecuteNonQueryAsync()!=1)
                throw new InvalidOperationException("Synthetic roster readiness was not cleaned.");
        }
        await tx.CommitAsync();
        currentAudience=null;
    }
}
