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

        // Only the fixed, read-only, closed presentation function is callable
        // by the runtime role. No table SELECT/UPDATE or athlete-data access.
        await using var app = new NpgsqlConnection(fixture.AppConnectionString);
        await app.OpenAsync();
        await using (var privileges = new NpgsqlCommand("""
            SELECT has_function_privilege(current_user,
                    'club.read_first_team_trial_display()', 'EXECUTE'),
                   NOT has_table_privilege(current_user,
                    'club.first_team_trial_settings', 'SELECT'),
                   NOT has_table_privilege(current_user,
                    'club.first_team_trial_settings', 'UPDATE')
            """, app))
        {
            await using var reader = await privileges.ExecuteReaderAsync();
            Check.True(await reader.ReadAsync(),"Trials runtime privilege row missing.");
            for (var column = 0; column < 3; column++)
                Check.True(reader.GetBoolean(column),
                    "Runtime trial display permission exceeded its narrow boundary.");
        }

        await using var publicView = new NpgsqlCommand(
            "SELECT is_open,headline FROM club.read_first_team_trial_display()", app);
        await using var publicRows = await publicView.ExecuteReaderAsync();
        Check.True(await publicRows.ReadAsync(),"Runtime source display projection unavailable.");
        Check.True(!publicRows.GetBoolean(0),"Unverified trials may not open via runtime read.");
        Check.Equal("Ετοιμος να Ξεκινησεις το Ταξιδι σου;",
            publicRows.GetString(1),"Runtime display source heading changed.");
        Check.True(!await publicRows.ReadAsync(),"Unexpected extra organization in trial settings.");

    }
}
