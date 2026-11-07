using LefteriaFc.Application.Trials;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Trials;

/// <summary>
/// Only the audited club.read_first_team_trial_display() function is used.
/// The app role has no direct SELECT/UPDATE on club.first_team_trial_settings.
/// No private identity, date of birth or application fields may be loaded.
/// </summary>
public sealed class PostgresFirstTeamTrialDisplayReader(LefteriaDbContext database)
    : IFirstTeamTrialDisplayReader
{
    public async Task<FirstTeamTrialsSettings?> ReadAsync(CancellationToken ct)
    {
        var connectionString = database.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT is_open,headline,subtitle,button_text,closed_message FROM club.read_first_team_trial_display()",
                connection) { CommandTimeout = 5 };
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;

            // Never trust even an approved SQL display function to activate
            // an athlete registration form without the separate consent gate.
            var candidate = new FirstTeamTrialsSettings(
                Open: false,
                Headline: reader.GetString(1),
                Subtitle: reader.GetString(2),
                ButtonText: reader.GetString(3),
                ClosedMessage: reader.GetString(4));
            if (await reader.ReadAsync(ct) ||
                !FirstTeamTrialsSettingsPolicy.TryValidate(candidate, out _))
                return null;
            return candidate;
        }
        catch (NpgsqlException) { return null; }
        catch (TimeoutException) { return null; }
    }
}
