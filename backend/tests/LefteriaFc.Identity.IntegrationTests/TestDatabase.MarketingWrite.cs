using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    // Fully synthetic test-only public lead, rolled back even on success.
    // No production credentials, no public endpoint, no SMS and no fake data.
    public async Task AssertMarketingAtomicWriteAsync()
    {
        var contact="Fixture Guardian "+Guid.NewGuid().ToString("N");
        var digest=new string('d',64);
        await using var connection=new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using(var tx=await connection.BeginTransactionAsync())
        {
            for(var attempt=0;attempt<6;attempt++)
            {
                await using var cmd=new NpgsqlCommand("""
                    SELECT marketing.store_academy_lead(
                      gen_random_uuid(),@name,'+35799000000',NULL::text,9,
                      'Academy U10',NULL::text,'fixture-partner',
                      true,false,@fingerprint)
                    """,connection,tx);
                cmd.Parameters.AddWithValue("name",contact);
                cmd.Parameters.AddWithValue("fingerprint",digest);
                var result=(string?)await cmd.ExecuteScalarAsync();
                Check.Equal(attempt<5?"stored":"rate_limited",result,
                    "The Academy lead DB throttle did not enforce a five-attempt hour.");
            }

            await using(var cmd=new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM marketing.public_leads
                          WHERE contact_name=@name AND organization_id='lefteria_fc_academy'),
                       (SELECT count(*) FROM marketing.lead_events e
                          JOIN marketing.public_leads l ON l.id=e.lead_id
                          WHERE l.contact_name=@name AND e.event_type='created'
                            AND e.organization_id='lefteria_fc_academy'),
                       (SELECT attempts FROM marketing.lead_rate_buckets
                          WHERE fingerprint=@fingerprint)
                """,connection,tx))
            {
                cmd.Parameters.AddWithValue("name",contact);
                cmd.Parameters.AddWithValue("fingerprint",digest);
                await using var reader=await cmd.ExecuteReaderAsync();
                Check.True(await reader.ReadAsync(),"Marketing write verification row missing.");
                Check.Equal(5L,reader.GetInt64(0),"Academy lead insert count changed.");
                Check.Equal(5L,reader.GetInt64(1),"Academy lead audit count diverged.");
                Check.Equal((short)5,reader.GetInt16(2),"Rate bucket exceeded hard cap.");
            }
            await tx.RollbackAsync();
        }

        // No test fixture PII or rate bucket may persist once rollback commits.
        await using(var verify=new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM marketing.public_leads WHERE contact_name=@name),
                   (SELECT count(*) FROM marketing.lead_rate_buckets WHERE fingerprint=@fingerprint)
            """,connection))
        {
            verify.Parameters.AddWithValue("name",contact);
            verify.Parameters.AddWithValue("fingerprint",digest);
            await using var reader=await verify.ExecuteReaderAsync();
            Check.True(await reader.ReadAsync(),"Marketing rollback verification missing.");
            Check.Equal(0L,reader.GetInt64(0),"Synthetic marketing lead leaked after rollback.");
            Check.Equal(0L,reader.GetInt64(1),"Synthetic marketing rate bucket leaked after rollback.");
        }
    }
}
