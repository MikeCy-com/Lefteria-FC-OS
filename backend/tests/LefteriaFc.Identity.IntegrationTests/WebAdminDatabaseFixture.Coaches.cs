using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    internal sealed record CoachFixture(string Prefix,bool OwnsReadiness)
    {
        public string Coach1=>Prefix+"-coach1";
        public string Coach2=>Prefix+"-coach2";
        public string Digest=>string.Concat(Enumerable.Repeat(Prefix[4..],2))[..64];
    }

    public async Task<CoachFixture> SeedCoachesAsync(AudienceFixture audience)
    {
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        await using var check=new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial')",
            con,tx);
        var ready=await check.ExecuteScalarAsync() is true;
        var fixture=new CoachFixture("stf-"+Guid.NewGuid().ToString("N"),!ready);

        async Task Execute(string sql,params (string Key,object Value)[] args)
        {
            await using var cmd=new NpgsqlCommand(sql,con,tx);
            foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Execute("""
            INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,
                has_explicit_active,has_explicit_archived)
            VALUES(@id,'Fictional shared coach','Youth Coach','Academy',true,false,true,true)
            """,("id",fixture.Coach1));
        await Execute("""
            INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,
                has_explicit_active,has_explicit_archived)
            VALUES(@id,'Fictional inactive coach','Head Coach','First Team',false,false,true,true)
            """,("id",fixture.Coach2));
        await Execute("""
            INSERT INTO club.staff_teams(staff_id,team_id,organization_id,from_plural,from_singular)
            VALUES(@coach,@team,'lefteria_fc',true,false)
            """,("coach",fixture.Coach1),("team",audience.Team1));
        await Execute("""
            INSERT INTO club.staff_academy_groups(
                staff_id,academy_group_id,organization_id,from_plural,from_singular,from_group_coach)
            VALUES(@coach,@group,'lefteria_fc_academy',true,false,false)
            """,("coach",fixture.Coach1),("group",audience.Group1));
        await Execute("""
            INSERT INTO club.academy_group_coaches(academy_group_id,organization_id,staff_id)
            VALUES(@group,'lefteria_fc_academy',@coach)
            """,("group",audience.Group2),("coach",fixture.Coach1));
        if(!ready)
        {
            await Execute("""
                INSERT INTO migration.staff_relationship_import_state(
                  id,digest,roster_digest,source_commit,environment,staff_count,
                  team_assignments_count,group_assignments_count,group_coaches_count,
                  contacts_count,applied_at)
                SELECT 'initial',@digest,r.digest,@commit,'development',2,1,1,1,0,clock_timestamp()
                FROM club.roster_import_state r WHERE r.id='initial'
                """,("digest",fixture.Digest),("commit",fixture.Prefix[4..]+"00000000"));
        }
        await tx.CommitAsync();
        return fixture;
    }

    public async Task CleanupCoachesAsync(CoachFixture fixture)
    {
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        async Task Delete(string sql,int count)
        {
            await using var cmd=new NpgsqlCommand(sql,con,tx);
            cmd.Parameters.AddWithValue("coach",fixture.Coach1);
            cmd.Parameters.AddWithValue("inactive",fixture.Coach2);
            var n=await cmd.ExecuteNonQueryAsync();
            if(n!=count)throw new InvalidOperationException("Unexpected fixture coach cleanup.");
        }
        await Delete("DELETE FROM club.academy_group_coaches WHERE staff_id=@coach",1);
        await Delete("DELETE FROM club.staff_academy_groups WHERE staff_id=@coach",1);
        // A failed HTTP assertion may leave a successful extra team assignment.
        // Cleanup still targets only this fictional coach and explicitly
        // permits the one additional team created by these tests.
        await using(var cmd=new NpgsqlCommand(
            "DELETE FROM club.staff_teams WHERE staff_id=@coach",con,tx))
        {
            cmd.Parameters.AddWithValue("coach",fixture.Coach1);
            var count=await cmd.ExecuteNonQueryAsync();
            if(count is <1 or >2)
                throw new InvalidOperationException("Unexpected synthetic coach team cleanup.");
        }
        await Delete("DELETE FROM club.staff WHERE id IN (@coach,@inactive)",2);
        if(fixture.OwnsReadiness)
        {
            await using var cmd=new NpgsqlCommand("""
                DELETE FROM migration.staff_relationship_import_state
                WHERE id='initial' AND digest=@digest
                """,con,tx);
            cmd.Parameters.AddWithValue("digest",fixture.Digest);
            if(await cmd.ExecuteNonQueryAsync()!=1)
                throw new InvalidOperationException("Coach readiness cleanup failed.");
        }
        await tx.CommitAsync();
    }
}
