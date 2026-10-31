using System.Text.Json;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Infrastructure.StaffRelationships;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Infrastructure.AccountImport;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Infrastructure.ClubCalendar;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Infrastructure.PublicContent;

namespace LefteriaFc.Tools.RosterImport;

public static class RosterImportProgram
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var apply = false;
            var reviewTarget = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--apply") { if (apply) return Error("duplicate_option"); apply = true; continue; }
                if (args[index] == "--review-target") { if (reviewTarget) return Error("duplicate_option"); reviewTarget = true; continue; }
                var name = args[index];
                if (name is not ("--kind" or "--input" or "--expected-sha256" or "--expected-database" or "--environment") ||
                    index + 1 >= args.Length || !values.TryAdd(name, args[++index])) return Error("invalid_option");
            }
            if (!values.TryGetValue("--input", out var path)) return Error("input_required");
            var kind = values.GetValueOrDefault("--kind", "roster");
            if (kind is not ("roster" or "staff-relationships" or "accounts" or "calendar" or "public-content")) return Error("invalid_kind");
            if (reviewTarget && (kind is not ("accounts" or "calendar") || apply)) return Error("invalid_review_mode");
            var bytes = await ReadBoundedAsync(path);
            // Validation must finish before reading credentials or constructing
            // any database adapter, including when --apply was requested.
            var plan = kind == "roster" ? RosterImportValidator.Validate(bytes) : null;
            var staffPlan = kind == "staff-relationships" ? StaffRelationshipImportValidator.Validate(bytes) : null;
            var accountManifest = kind == "accounts" ? AccountImportValidator.Validate(bytes) : null;
            var calendarManifest = kind == "calendar" ? CalendarImportValidator.Validate(bytes) : null;
            var publicManifest = kind == "public-content" ? PublicContentImportValidator.Validate(bytes) : null;
            if (!apply && !reviewTarget)
            {
                if (plan is not null) Summary("valid_dry_run", plan);
                else if (staffPlan is not null) StaffSummary("valid_dry_run", staffPlan);
                else if(accountManifest is not null) AccountSummary("manifest_valid", accountManifest);
                else if (publicManifest is not null) PublicSummary("manifest_valid", publicManifest);
                else CalendarSummary("manifest_valid",calendarManifest!);
                return 0;
            }
            if (!values.TryGetValue("--expected-sha256", out var digest) || !values.TryGetValue("--expected-database", out var database) ||
                !values.TryGetValue("--environment", out var environment)) return Error("apply_confirmation_required");
            var connection = System.Environment.GetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION");
            if (string.IsNullOrWhiteSpace(connection)) return Error("owner_connection_required");
            if (plan is not null)
            {
                var result = await PostgresRosterImport.ApplyAsync(plan, connection, digest, database, environment);
                Summary(result.Noop ? "same_snapshot_noop" : "applied", plan);
            }
            else if (staffPlan is not null)
            {
                var result = await PostgresStaffRelationshipImport.ApplyAsync(staffPlan, connection, digest, database, environment);
                StaffSummary(result.Noop ? "same_snapshot_noop" : "applied", staffPlan, result);
            }
            else if(accountManifest is not null)
            {
                var result = await PostgresAccountImport.ApplyAsync(accountManifest!, connection, digest, database, environment, reviewTarget);
                var status = reviewTarget ? "reviewed" : result.Noop ? "same_snapshot_noop" : "applied";
                if (result.IssueCounts.Count > 0) status += "_with_quarantine";
                AccountSummary(status, accountManifest!, result);
            }
            else if (publicManifest is not null)
            {
                var result = await PostgresPublicContentImport.ApplyAsync(publicManifest, connection, digest, database, environment);
                PublicSummary(result.Noop ? "same_snapshot_noop" : "applied", publicManifest);
            }
            else
            {
                var result=await PostgresCalendarImport.ApplyAsync(calendarManifest!,connection,digest,database,environment,reviewTarget);
                var status=reviewTarget?"reviewed":result.Noop?"same_snapshot_noop":"applied";
                if(result.IssueCounts.Count>0)status+="_with_quarantine";
                CalendarSummary(status,calendarManifest!,result);
            }
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
    private static void PublicSummary(string status, ValidatedPublicContentImport manifest) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status, kind = "public-content", sha256 = manifest.Digest, source_commit = manifest.SourceCommit,
            documents = manifest.Documents.Count, source_rows = manifest.Source.Count,
            kinds = manifest.Documents.GroupBy(row => row.Kind).ToDictionary(group => group.Key, group => group.Count())
        }));
    private static void CalendarSummary(string status,ValidatedCalendarImport manifest,CalendarImportOutcome? result=null)=>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status,kind="calendar",sha256=manifest.Digest,roster_sha256=manifest.RosterDigest,time_zone=manifest.TimeZone,
            source_rows=new[]{"fixtures","events","training_sessions","availability","event_attendance","attendance"}
                .ToDictionary(collection=>collection,collection=>manifest.Rows.Count(row=>row.Collection==collection),StringComparer.Ordinal),
            counts=result is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(result.CountsJson),
            issue_counts=result?.IssueCounts,bound_snapshots_checked=result is not null,applied=result?.Applied??false,
            review_only=result?.Reviewed??false,ready_for_calendar=result is not null&&(result.Applied||result.Noop)&&result.IssueCounts.Count==0
        }));
    private static void Summary(string status, ValidatedRosterImport plan) => Console.WriteLine(JsonSerializer.Serialize(new
    {
        status, sha256 = plan.Digest, teams = plan.Teams.Count, academy_groups = plan.Groups.Count,
        players = plan.Players.Count, memberships = plan.MembershipCount
    }));
    private static void StaffSummary(string status, ValidatedStaffRelationshipImport plan, StaffRelationshipApplyResult? result = null) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status, kind = "staff-relationships", sha256 = plan.Digest, roster_sha256 = plan.RosterDigest,
            staff = plan.Staff.Count, team_assignments = result?.TeamAssignments, group_assignments = result?.GroupAssignments,
            group_coaches = result?.GroupCoaches, contacts = result?.Contacts,
            bound_roster_checked = result is not null
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
    private static void AccountSummary(string status, ValidatedAccountImport manifest, AccountImportOutcome? result = null) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status, kind = "accounts", sha256 = manifest.Digest,
            roster_sha256 = manifest.RosterDigest, staff_sha256 = manifest.StaffDigest,
            source_rows = new
            {
                mobile_users = manifest.Rows.Count(row => row.Collection == "mobile_users"),
                fans = manifest.Rows.Count(row => row.Collection == "fans"),
                admin_users = manifest.Rows.Count(row => row.Collection == "admin_users")
            },
            counts = result is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(result.CountsJson),
            issue_counts = result?.IssueCounts,
            bound_snapshots_checked = result is not null,
            applied = result?.Applied ?? false,
            review_only = result?.Reviewed ?? false
        }));
}
