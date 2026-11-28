using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using LefteriaFc.Domain.Identity;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task PotmOpenPeriodAsync()
    {
        const string path="/api/mobile/potm/ballot";
        var phone=database.NewPhone();
        var fan=await database.SeedIdentityAsync(phone,MobileRole.Fan,"CI open ballot");
        var otherPhone=database.NewPhone();
        var other=await database.SeedIdentityAsync(otherPhone,MobileRole.Fan,"CI independent ballot");
        using var first=Client();
        using var second=Client();
        using var independent=Client();
        var login=await LoginAsync(first,phone,"fan");
        second.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("token").GetString());
        await LoginAsync(independent,otherPhone,"fan");

        await database.WithPotmOpenFixtureAsync(fan,other,async month=>
        {
            var empty=await GetAsync(first,path,200);
            Check.True(empty.GetProperty("voting_open").GetBoolean(),"Synthetic owner-reviewed period did not open.");
            Check.Equal(month,empty.GetProperty("month_key").GetString(),"Unexpected Cyprus month.");
            Check.Equal(0,empty.GetProperty("revision").GetInt32(),"New ballot had a revision.");

            foreach(var bad in new[]{"ci-potm-http-inactive","ci-potm-http-youth","ci-potm-http-missing"})
            {
                using var denied=await first.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id=bad});
                await ExpectAsync(denied,422);
            }

            // Hold the exact identity row so BOTH real HTTP requests are
            // observed waiting on PostgreSQL locks before either can write.
            var replies=await database.RacePotmWritesAsync(fan,
                ()=>first.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id="ci-potm-http-one"}),
                ()=>second.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id="ci-potm-http-two"}));
            using var a=replies[0];using var b=replies[1];
            Check.True(new[]{(int)a.StatusCode,(int)b.StatusCode}.OrderBy(x=>x).SequenceEqual(new[]{200,409}),
                "Simultaneous same-revision writes did not produce exactly one committed ballot and one conflict.");
            var winning=await ExpectAsync((int)a.StatusCode==200?a:b,200);
            var conflict=await ExpectAsync((int)a.StatusCode==409?a:b,409);
            Check.Equal("potm_revision_conflict",conflict.GetProperty("code").GetString(),"Wrong stale ballot error.");
            var winner=winning.GetProperty("my_vote_player_id").GetString()!;
            var alternate=winner=="ci-potm-http-one"?"ci-potm-http-two":"ci-potm-http-one";
            Check.Equal((int)a.StatusCode==200?"ci-potm-http-one":"ci-potm-http-two",winner,"Unexpected winning player.");
            Check.Equal(1,winning.GetProperty("revision").GetInt32(),"First successful ballot did not advance once.");

            async Task Confirm(HttpClient client,string? choice,int revision,int expected)
            {
                using var reply=await client.PutAsJsonAsync(path,new{month_key=month,expected_revision=revision,player_id=choice});
                var result=await ExpectAsync(reply,200);
                Check.Equal(expected,result.GetProperty("revision").GetInt32(),"Ballot revision did not match persisted mutation.");
                Check.Equal(choice,result.GetProperty("my_vote_player_id").GetString(),"Wrong confirmed ballot.");
                var read=await GetAsync(client,path,200);
                Check.Equal(expected,read.GetProperty("revision").GetInt32(),"Read-after-write lost the ballot revision.");
                Check.Equal(choice,read.GetProperty("my_vote_player_id").GetString(),"Read-after-write lost the ballot choice.");
            }

            var persisted=await GetAsync(second,path,200);
            Check.Equal(winner,persisted.GetProperty("my_vote_player_id").GetString(),"Concurrent winner did not persist.");
            var unrelated=await GetAsync(independent,path,200);
            Check.Equal(0,unrelated.GetProperty("revision").GetInt32(),"Another account's ballot leaked.");
            Check.True(unrelated.GetProperty("my_vote_player_id").ValueKind==JsonValueKind.Null,"Another account's choice leaked.");

            await Confirm(first,winner,1,1);
            await Confirm(first,alternate,1,2);
            await Confirm(first,null,2,3);
            await Confirm(first,null,3,3);
            await Confirm(first,winner,3,4);
            await Confirm(independent,alternate,0,1);
            await database.AssertPotmOpenHistoryAsync(fan,other,winner,alternate);
            await database.ClosePotmOpenFixtureAsync();

            var closed=await GetAsync(first,path,200);
            Check.True(!closed.GetProperty("voting_open").GetBoolean(),"Closed period still offered votes.");
            Check.Equal(winner,closed.GetProperty("my_vote_player_id").GetString(),"Closing erased the confirmed ballot.");
            using var refused=await second.PutAsJsonAsync(path,new{month_key=month,expected_revision=4,player_id=alternate});
            await ExpectAsync(refused,409);
            await database.AssertPotmOpenHistoryAsync(fan,other,winner,alternate);
        });
    }
}

internal sealed partial class TestDatabase
{
    private const int PotmHttpVersion=99031;
    private const string PotmHttpApproval="CI HTTP concurrency fixture only";

    // Committed synthetic rows are necessary for separate HTTP connections.
    // The existing fixture loader verifies exact database name, host, port
    // and owner. No existing publication or election is ever modified.
    public async Task WithPotmOpenFixtureAsync(Guid fan,Guid other,Func<string,Task> test)
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString,fixture.AdminUsername);
        await using var connection=new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        var admin=Guid.NewGuid();
        string month;
        await using(var tx=await connection.BeginTransactionAsync())
        {
            await using var setup=new NpgsqlCommand("""
                DO $guard$ BEGIN
                  IF current_database()<>'lefteria_rewrite_identity_tests'
                    OR EXISTS(SELECT 1 FROM public_sections.snapshots)
                    OR EXISTS(SELECT 1 FROM public_sections.current)
                    OR EXISTS(SELECT 1 FROM public_content.import_state)
                    OR EXISTS(SELECT 1 FROM public_content.documents)
                    OR EXISTS(SELECT 1 FROM club.potm_periods)
                    OR EXISTS(SELECT 1 FROM club.players WHERE id LIKE 'ci-potm-http-%')
                  THEN RAISE EXCEPTION 'Refusing nonempty POTM HTTP fixture'; END IF;
                END $guard$;
                INSERT INTO identity.admin_accounts
                  (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
                VALUES(@admin,'ci-potm-http-'||left(md5(@admin::text),8),
                  'CI POTM Approver','super_admin',true,'ci-fixture-hash',false,false);
                INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
                  VALUES(@admin,'lefteria_fc');
                INSERT INTO club.players(id,organization_id,name,number,position,team_type,
                  is_active,is_archived,is_transferred,has_explicit_picker_fields)
                VALUES
                  ('ci-potm-http-one','lefteria_fc','CI HTTP First One',7,'Forward','First Team',true,false,false,true),
                  ('ci-potm-http-two','lefteria_fc','CI HTTP First Two',8,'Midfielder','First Team',true,false,false,true),
                  ('ci-potm-http-inactive','lefteria_fc','CI HTTP Inactive',9,'Defender','First Team',false,false,false,true),
                  ('ci-potm-http-youth','lefteria_fc_academy','CI HTTP Youth',10,'Forward','Academy',true,false,false,true);
                INSERT INTO public_content.import_state(id,digest,source_repository,source_commit,
                  environment,documents_count,sources_count,applied_at)
                VALUES('initial',repeat('b',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
                  'development',0,0,clock_timestamp());
                INSERT INTO public_sections.snapshots(version,digest,base_public_digest,source_repository,
                  source_commit,environment,exported_at,applied_at,documents_count,sources_count,issues_count)
                VALUES(@version,repeat('c',64),repeat('b',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
                  'development',clock_timestamp(),clock_timestamp(),5,0,0);
                INSERT INTO public_sections.current(id,snapshot_version) VALUES('current',@version);
                INSERT INTO public_sections.documents(snapshot_version,kind,organization_id,id,data_json,display_order)
                  SELECT @version,'player',p.organization_id,p.id,
                    jsonb_build_object('id',p.id,'organization_id',p.organization_id,'team_type',p.team_type,'name',p.name),p.number
                  FROM club.players p WHERE p.id IN
                    ('ci-potm-http-one','ci-potm-http-two','ci-potm-http-inactive','ci-potm-http-youth');
                INSERT INTO public_sections.documents(snapshot_version,kind,organization_id,id,data_json,display_order)
                  VALUES(@version,'module','lefteria_fc','potm',
                    '{"id":"potm","organization_id":"lefteria_fc","status":"empty","quarantined_count":0}'::jsonb,0);
                INSERT INTO club.potm_periods(month_start,snapshot_version,approved_by,approval_reference,legacy_stopped_at)
                  SELECT m,@version,@admin,@approval,(m::timestamp AT TIME ZONE 'Asia/Nicosia')-interval '1 second'
                  FROM (SELECT date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date AS m) period;
                """,connection,tx);
            setup.Parameters.AddWithValue("admin",admin);
            setup.Parameters.AddWithValue("version",PotmHttpVersion);
            setup.Parameters.AddWithValue("approval",PotmHttpApproval);
            await setup.ExecuteNonQueryAsync();
            await using var read=new NpgsqlCommand("SELECT to_char(month_start,'YYYY-MM') FROM club.potm_periods WHERE snapshot_version=@version",connection,tx);
            read.Parameters.AddWithValue("version",PotmHttpVersion);
            month=(string?)await read.ExecuteScalarAsync()??throw new InvalidOperationException("Synthetic period missing.");
            await tx.CommitAsync();
        }
        try{await test(month);}
        finally
        {
            await using var tx=await connection.BeginTransactionAsync();
            await using var cleanup=new NpgsqlCommand("""
                DELETE FROM club.potm_ballot_events WHERE month_start=(SELECT month_start FROM club.potm_periods
                  WHERE snapshot_version=@version AND approved_by=@admin AND approval_reference=@approval)
                  AND identity_id IN (@fan,@other);
                DELETE FROM club.potm_ballots WHERE month_start=(SELECT month_start FROM club.potm_periods
                  WHERE snapshot_version=@version AND approved_by=@admin AND approval_reference=@approval)
                  AND identity_id IN (@fan,@other);
                DELETE FROM club.potm_periods WHERE snapshot_version=@version AND approved_by=@admin AND approval_reference=@approval;
                DELETE FROM public_sections.current WHERE id='current' AND snapshot_version=@version;
                DELETE FROM public_sections.documents WHERE snapshot_version=@version;
                DELETE FROM public_sections.snapshots WHERE version=@version AND digest=repeat('c',64);
                DELETE FROM public_content.import_state WHERE id='initial' AND digest=repeat('b',64);
                DELETE FROM club.players WHERE id IN ('ci-potm-http-one','ci-potm-http-two','ci-potm-http-inactive','ci-potm-http-youth');
                DELETE FROM identity.admin_organization_grants WHERE admin_account_id=@admin;
                DELETE FROM identity.admin_accounts WHERE id=@admin;
                """,connection,tx);
            cleanup.Parameters.AddWithValue("version",PotmHttpVersion);
            cleanup.Parameters.AddWithValue("admin",admin);
            cleanup.Parameters.AddWithValue("approval",PotmHttpApproval);
            cleanup.Parameters.AddWithValue("fan",fan);
            cleanup.Parameters.AddWithValue("other",other);
            await cleanup.ExecuteNonQueryAsync();
            await using var proof=new NpgsqlCommand("""
                SELECT NOT EXISTS(SELECT 1 FROM club.potm_periods)
                  AND NOT EXISTS(SELECT 1 FROM club.potm_ballots)
                  AND NOT EXISTS(SELECT 1 FROM club.potm_ballot_events)
                  AND NOT EXISTS(SELECT 1 FROM public_sections.snapshots)
                  AND NOT EXISTS(SELECT 1 FROM public_content.import_state)
                  AND NOT EXISTS(SELECT 1 FROM club.players WHERE id LIKE 'ci-potm-http-%')
                  AND NOT EXISTS(SELECT 1 FROM identity.admin_accounts WHERE id=@admin)
                """,connection,tx);
            proof.Parameters.AddWithValue("admin",admin);
            Check.True((bool)(await proof.ExecuteScalarAsync())!,"Synthetic POTM cleanup did not restore the empty publication fixture.");
            await tx.CommitAsync();
        }
    }

    public async Task<HttpResponseMessage[]> RacePotmWritesAsync(Guid fan,
        Func<Task<HttpResponseMessage>> first,Func<Task<HttpResponseMessage>> second)
    {
        await using var blocker=new NpgsqlConnection(fixture.AdminConnectionString);
        await blocker.OpenAsync();
        await using var tx=await blocker.BeginTransactionAsync();
        await using(var hold=new NpgsqlCommand("SELECT id FROM identity.mobile_identities WHERE id=@fan FOR UPDATE",blocker,tx))
        {
            hold.Parameters.AddWithValue("fan",fan);
            Check.Equal(fan,(Guid)(await hold.ExecuteScalarAsync())!,"Missing fixture Fan row.");
        }
        var pending=new[]{first(),second()};
        var bothWaiting=false;
        try
        {
            await using var observer=new NpgsqlConnection(fixture.AdminConnectionString);
            await observer.OpenAsync();
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed<TimeSpan.FromSeconds(8))
            {
                await using var check=new NpgsqlCommand("""
                    SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()
                      AND usename=@runtime AND state='active' AND wait_event_type='Lock'
                      AND lower(ltrim(query)) LIKE 'select club.write_mobile_potm_ballot(%'
                    """,observer);
                check.Parameters.AddWithValue("runtime",fixture.AppUsername);
                if((long)(await check.ExecuteScalarAsync())!>=2){bothWaiting=true;break;}
                if(pending.Any(task=>task.IsCompleted))break;
                await Task.Delay(25);
            }
        }
        finally
        {
            // Release the barrier and drain both HTTP requests even when the
            // observation fails; no write may race the fixture cleanup.
            await tx.RollbackAsync();
            await Task.WhenAll(pending);
        }
        var replies=await Task.WhenAll(pending);
        if(!bothWaiting)
        {
            foreach(var reply in replies)reply.Dispose();
            throw new InvalidOperationException("Did not observe two concurrent ballot writes waiting on PostgreSQL locks.");
        }
        return replies;
    }

    public async Task AssertPotmOpenHistoryAsync(Guid fan,Guid other,string winner,string alternate)
    {
        await using var connection=new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM club.potm_ballots)=2
              AND (SELECT count(*) FROM club.potm_ballot_events)=5
              AND EXISTS(SELECT 1 FROM club.potm_ballots WHERE identity_id=@fan AND revision=4 AND player_id=@winner)
              AND EXISTS(SELECT 1 FROM club.potm_ballots WHERE identity_id=@other AND revision=1 AND player_id=@alternate)
              AND EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE identity_id=@fan AND revision=1 AND previous_player_id IS NULL AND player_id=@winner)
              AND EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE identity_id=@fan AND revision=2 AND previous_player_id=@winner AND player_id=@alternate)
              AND EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE identity_id=@fan AND revision=3 AND previous_player_id=@alternate AND player_id IS NULL)
              AND EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE identity_id=@fan AND revision=4 AND previous_player_id IS NULL AND player_id=@winner)
              AND EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE identity_id=@other AND revision=1 AND previous_player_id IS NULL AND player_id=@alternate)
            """,connection);
        cmd.Parameters.AddWithValue("fan",fan);cmd.Parameters.AddWithValue("other",other);
        cmd.Parameters.AddWithValue("winner",winner);cmd.Parameters.AddWithValue("alternate",alternate);
        Check.True((bool)(await cmd.ExecuteScalarAsync())!,"Persisted one-ballot and exact revision history invariants failed.");
    }

    public async Task ClosePotmOpenFixtureAsync()
    {
        await using var connection=new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd=new NpgsqlCommand("UPDATE club.potm_periods SET closed_at=clock_timestamp() WHERE snapshot_version=@version AND approval_reference=@approval AND closed_at IS NULL",connection);
        cmd.Parameters.AddWithValue("version",PotmHttpVersion);cmd.Parameters.AddWithValue("approval",PotmHttpApproval);
        Check.Equal(1,await cmd.ExecuteNonQueryAsync(),"Unexpected fixture period closure.");
    }
}
