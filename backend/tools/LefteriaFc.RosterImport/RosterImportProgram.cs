using System.Text.Json;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;

namespace LefteriaFc.Tools.RosterImport;

public static class RosterImportProgram
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var apply = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--apply") { if (apply) return Error("duplicate_option"); apply = true; continue; }
                var name = args[index];
                if (name is not ("--input" or "--expected-sha256" or "--expected-database" or "--environment") ||
                    index + 1 >= args.Length || !values.TryAdd(name, args[++index])) return Error("invalid_option");
            }
            if (!values.TryGetValue("--input", out var path)) return Error("input_required");
            var bytes = await ReadBoundedAsync(path);
            // Validation must finish before reading credentials or constructing
            // any database adapter, including when --apply was requested.
            var plan = RosterImportValidator.Validate(bytes);
            if (!apply)
            {
                Summary("valid_dry_run", plan);
                return 0;
            }
            if (!values.TryGetValue("--expected-sha256", out var digest) || !values.TryGetValue("--expected-database", out var database) ||
                !values.TryGetValue("--environment", out var environment)) return Error("apply_confirmation_required");
            var connection = System.Environment.GetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION");
            if (string.IsNullOrWhiteSpace(connection)) return Error("owner_connection_required");
            var result = await PostgresRosterImport.ApplyAsync(plan, connection, digest, database, environment);
            Summary(result.Noop ? "same_snapshot_noop" : "applied", plan);
            return 0;
        }
        catch (RosterImportValidationException exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "invalid", issue = exception.Issue }));
            return 2;
        }
        catch (RosterImportApplyException exception) { return Error(exception.Code); }
        catch (Exception) { return Error("operation_failed"); }
    }
    private static int Error(string code)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "refused", code }));
        return 2;
    }
    private static void Summary(string status, ValidatedRosterImport plan) => Console.WriteLine(JsonSerializer.Serialize(new
    {
        status, sha256 = plan.Digest, teams = plan.Teams.Count, academy_groups = plan.Groups.Count,
        players = plan.Players.Count, memberships = plan.MembershipCount
    }));
    private static async Task<byte[]> ReadBoundedAsync(string path)
    {
        await using var input = File.OpenRead(path);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024]; int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (output.Length + count > RosterImportValidator.MaximumBytes)
                throw new RosterImportValidationException(new("input_size"));
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        return output.ToArray();
    }
}
