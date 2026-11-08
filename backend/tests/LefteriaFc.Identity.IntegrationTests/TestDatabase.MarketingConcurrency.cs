using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    // This test uses only the exact disposable loopback PostgreSQL fixture.
    // Independent transactions MUST commit to test the rate bucket row lock:
    // rolling back each competing transaction would erase its throttle state.
    // Every synthetic row is owned by randomly generated IDs and removed in
    // finally; the disposable CI database is discarded after this run.
    public async Task AssertMarketingConcurrentRateLimitAsync()
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString, fixture.AdminUsername);
        await fixture.VerifyServerAsync();

        var ids = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        var contact = "Concurrent Fixture Guardian " + Guid.NewGuid().ToString("N");
        var fingerprint = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string?> AttemptAsync(Guid id)
        {
            await start.Task;
            await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT marketing.store_academy_lead(
                  @id,@name,'+35799000000',NULL::text,9,
                  'Academy U10',NULL::text,'fixture-partner',
                  true,false,@fingerprint)
                """, connection) { CommandTimeout = 30 };
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("name", contact);
            command.Parameters.AddWithValue("fingerprint", fingerprint);
            return (string?)await command.ExecuteScalarAsync();
        }

        try
        {
            // The tasks reach the same fingerprint INSERT simultaneously.
            // Each independent SQL statement commits before returning.
            var pending = ids.Select(AttemptAsync).ToArray();
            start.SetResult(true);
            var results = await Task.WhenAll(pending);
            Check.Equal(5, results.Count(value => value == "stored"),
                "Concurrent Academy leads escaped the five-attempt limit.");
            Check.Equal(4, results.Count(value => value == "rate_limited"),
                "Concurrent rate-limit denials did not match excess requests.");

            await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var counts = new NpgsqlCommand("""
                SELECT
                  (SELECT count(*) FROM marketing.public_leads
                   WHERE id=ANY(@ids) AND contact_name=@name
                     AND organization_id='lefteria_fc_academy'),
                  (SELECT count(*) FROM marketing.lead_events
                   WHERE lead_id=ANY(@ids) AND event_type='created'
                     AND organization_id='lefteria_fc_academy'),
                  (SELECT attempts FROM marketing.lead_rate_buckets
                   WHERE fingerprint=@fingerprint),
                  (SELECT count(*) FROM marketing.public_leads
                   WHERE id=ANY(@ids) AND organization_id='lefteria_fc')
                """, connection);
            counts.Parameters.AddWithValue("ids", ids);
            counts.Parameters.AddWithValue("name", contact);
            counts.Parameters.AddWithValue("fingerprint", fingerprint);
            await using var reader = await counts.ExecuteReaderAsync();
            Check.True(await reader.ReadAsync(), "Concurrent Academy audit projection missing.");
            Check.Equal(5L, reader.GetInt64(0), "Concurrent stored lead count differs.");
            Check.Equal(5L, reader.GetInt64(1), "Concurrent lead events are not atomic.");
            Check.Equal((short)5, reader.GetInt16(2), "Concurrent rate bucket exceeded its cap.");
            Check.Equal(0L, reader.GetInt64(3), "Concurrent Academy enquiry crossed into FC.");
            Check.True(!await reader.ReadAsync(), "Unexpected duplicate audit projection.");
        }
        finally
        {
            // Only exact test-owned UUIDs/digest may be removed. Failure to
            // clean the disposable fixture is itself a failed integration run.
            await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var statement in new[]
            {
                "DELETE FROM marketing.lead_events WHERE lead_id=ANY(@ids)",
                "DELETE FROM marketing.public_leads WHERE id=ANY(@ids)",
                "DELETE FROM marketing.lead_rate_buckets WHERE fingerprint=@fingerprint"
            })
            {
                await using var cleanup = new NpgsqlCommand(statement, connection, transaction);
                cleanup.Parameters.AddWithValue("ids", ids);
                cleanup.Parameters.AddWithValue("fingerprint", fingerprint);
                await cleanup.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
    }
}
