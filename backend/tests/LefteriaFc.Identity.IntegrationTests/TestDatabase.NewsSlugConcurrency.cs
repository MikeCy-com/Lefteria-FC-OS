using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    // Independent committed transactions prove that a reservation is exclusive
    // across editions, including two owners of the same imported story.
    public async Task AssertConcurrentNewsSlugClaimAsync()
    {
        FixtureConfiguration.ValidateConnection(fixture.AdminConnectionString, fixture.AdminUsername);
        await fixture.VerifyServerAsync();
        var slug = "fixture-race-" + Guid.NewGuid().ToString("N");
        var owners = new[] { "lefteria_fc", "lefteria_fc_academy" };
        await using var control = new NpgsqlConnection(fixture.AdminConnectionString);
        await control.OpenAsync();
        await using (var preflight = new NpgsqlCommand("""
            SELECT count(*) FROM public_content.news_slug_aliases
              WHERE kind='news' AND article_id='shared'
            """, control))
        {
            if ((long)(await preflight.ExecuteScalarAsync())! != 0)
                throw new InvalidOperationException("Unexpected existing reservations in disposable news fixture.");
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string?> AttemptAsync(string owner)
        {
            await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
            await connection.OpenAsync();
            await start.Task;
            await using var command = new NpgsqlCommand("""
                SELECT public_content.claim_news_slug(@owner,'shared',@slug,NULL)
                """, connection) { CommandTimeout = 20 };
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("slug", slug);
            try
            {
                return (string?)await command.ExecuteScalarAsync() == slug
                    ? owner : throw new InvalidOperationException("Unexpected reservation result.");
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation
                && error.MessageText == "news_slug_reserved")
            { return null; }
        }

        try
        {
            var pending = owners.Select(AttemptAsync).ToArray();
            start.SetResult(true);
            var results = await Task.WhenAll(pending);
            if (results.Count(owner => owner is not null) != 1)
                throw new InvalidOperationException("Concurrent claims did not produce exactly one winner.");
            var winner = results.Single(owner => owner is not null);
            await using var check = new NpgsqlCommand("""
                SELECT a.organization_id,c.organization_id
                FROM public_content.news_slug_aliases a
                JOIN public_content.news_slug_current c USING(slug,kind,organization_id,article_id)
                WHERE a.slug=@slug AND a.kind='news' AND a.article_id='shared'
                """, control);
            check.Parameters.AddWithValue("slug", slug);
            await using var reader = await check.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetString(0) != winner ||
                reader.GetString(1) != winner || await reader.ReadAsync())
                throw new InvalidOperationException("Concurrent claim left inconsistent canonical ownership.");
        }
        finally
        {
            // Remove only this random, test-owned slug, never an existing alias.
            await using var transaction = await control.BeginTransactionAsync();
            foreach (var statement in new[]
            {
                "DELETE FROM public_content.news_slug_current WHERE slug=@slug AND kind='news' AND article_id='shared'",
                "DELETE FROM public_content.news_slug_aliases WHERE slug=@slug AND kind='news' AND article_id='shared'"
            })
            {
                await using var cleanup = new NpgsqlCommand(statement, control, transaction);
                cleanup.Parameters.AddWithValue("slug", slug);
                await cleanup.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
    }
}
