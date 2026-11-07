using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class TestDatabase
{
    public async Task AssertFirstTeamTrialDisplayProjectionClosedAsync()
    {
        // The source-grounded read projection defaults closed and never
        // exposes opening without a separately authorized submission flow.
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "SELECT is_open,headline,subtitle,button_text,closed_message FROM club.read_first_team_trial_display()", admin))
        {
            await using var rows = await cmd.ExecuteReaderAsync();
            Check.True(await rows.ReadAsync(),"First-team closed display projection missing.");
            Check.True(!rows.GetBoolean(0),"Trials unexpectedly opened without verified approval.");
            Check.Equal("Ετοιμος να Ξεκινησεις το Ταξιδι σου;",rows.GetString(1),
                "Default trial heading differs from the original.");
            Check.True(rows.GetString(2).Contains("LEFTERIA FC",StringComparison.Ordinal),
                "Original first-team subtitle missing.");
            Check.Equal("Δηλωσε Συμμετοχη για Δοκιμαστικα",rows.GetString(3),
                "Original first-team button differs.");
            Check.True(rows.GetString(4).Contains("προσωρινά κλειστές",StringComparison.Ordinal),
                "Original closed registration message missing.");
            Check.True(!await rows.ReadAsync(),"Expected exactly one scoped settings row.");
        }

        // Explicitly confirm runtime role cannot execute this SECURITY
        // DEFINER function before its reviewed public-reader grant.
        await using var app = new NpgsqlConnection(fixture.AppConnectionString);
        await app.OpenAsync();
        await using var denied = new NpgsqlCommand("""
            SELECT NOT has_function_privilege(current_user, fun.oid, 'EXECUTE')
            FROM pg_catalog.pg_proc fun
            JOIN pg_catalog.pg_namespace ns ON ns.oid=fun.pronamespace
            WHERE ns.nspname='club' AND fun.proname='read_first_team_trial_display'
            """, app);
        Check.True(Convert.ToBoolean(await denied.ExecuteScalarAsync()),
            "Unapproved runtime role can execute trial settings projection.");
    }
}
