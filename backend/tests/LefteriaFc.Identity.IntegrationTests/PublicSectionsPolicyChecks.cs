using System.Text.Json;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.Roster;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class PublicSectionsPolicyChecks
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static void Run()
    {
        var bytes = Fixture();
        var plan = PublicSectionsImportValidator.Validate(bytes);
        Check(plan.ManifestBytes.AsSpan().SequenceEqual(bytes), "Original source bytes must remain recoverable.");
        var players = plan.Documents.Where(row => row.Kind == "player")
            .Select(row => JsonSerializer.Deserialize<PublicSectionPlayer>(row.Json, Json)!).ToArray();
        Check(players.Length == 3, "Only the eligible source roster must be public.");
        var first = players.Single(row => row.Id == "first");
        Check(first.Name == "First Public Player" && first.Statistics?.Goals == 0 && first.Statistics.Assists is null,
            "Recorded zero and unknown statistics must remain distinct.");
        var child = players.Single(row => row.Id == "child");
        Check(child.Name == "Ανδρέας Πα." && child.OrganizationId == "lefteria_fc_academy" && child.Statistics is null,
            "Academy names must be abbreviated before the public DTO, and missing statistics must stay unavailable.");
        Check(child.Bio is null && child.Nationality is null && child.PreviousClubs.Count == 0 && child.Height is null,
            "Private Academy profile fields must not reach the public projection.");
        Check(players.Single(row => row.Id == "no-dob").Age is null,
            "A missing DOB cannot silently remove an otherwise published player.");
        Check(plan.Documents.Any(row => row.Kind == "staff" && row.Id == "coach"),
            "A missing DOB cannot remove an otherwise public coach.");
        var safe = string.Join('\n', plan.Documents.Select(row => row.Json));
        foreach (var value in new[] { "Παπαδόπουλος", "sentinel-private", "date_of_birth", "parent_phone", "registration_number", "medical_notes" })
            Check(!safe.Contains(value, StringComparison.Ordinal), "A private source field leaked to the public JSON.");
        Check(plan.Source.Any(row => row.Json.Contains("sentinel-private", StringComparison.Ordinal)), "Private source evidence must not be discarded.");
        Check(plan.Issues.Any(row => row.Code == "academy_group_reference") && plan.Issues.Any(row => row.Code == "integer_value"),
            "Malformed statistics and orphan memberships require explicit reconciliation issues.");
        CheckArchivePrivacy();
        try { PublicSectionsImportValidator.Validate(Fixture(duplicate: true)); }
        catch (RosterImportValidationException) { Console.WriteLine("PASS public sections: eligible roster, Academy profile/archive privacy, recorded statistics, raw evidence and invalid-source refusal."); return; }
        throw new InvalidOperationException("Duplicate source identities must refuse the snapshot.");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void CheckArchivePrivacy()
    {
        foreach (var (marker, code) in new[] { ("First Team", "archive_player_scope_conflict"), ("Academi", "archive_player_team_type") })
        {
            var refused = PublicSectionsImportValidator.Validate(Fixture(archiveTeamType: marker));
            Check(!refused.Documents.Any(row => row.Kind == "archive"), "An ambiguous archive must not publish a partial season or player name.");
            Check(refused.Issues.Any(row => row.Collection == "archived_seasons" && row.Code == code), "Unsafe archive scope must retain an explicit reconciliation issue.");
            using var evidence = JsonDocument.Parse(refused.Source.Single(row => row.Collection == "archived_seasons").Json);
            Check(evidence.RootElement.GetProperty("player_stats")[0].GetProperty("name").GetString() == "Ανδρέας Παπαδόπουλος",
                "Quarantined archive evidence must remain recoverable.");
        }
        foreach (var marker in new string?[] { "Academy", null })
        {
            var valid = PublicSectionsImportValidator.Validate(Fixture(archiveTeamType: marker, includeArchive: true));
            var archive = valid.Documents.Where(row => row.Kind == "archive")
                .Select(row => JsonSerializer.Deserialize<PublicSectionArchive>(row.Json, Json)!).Single();
            Check(archive.OrganizationId == "lefteria_fc_academy" && archive.PlayerStats.Single().Name == "Ανδρέας Πα." && archive.TopScorerName == "Ανδρέας Πα.",
                "A valid or identity-resolved Academy archive must abbreviate both the player and top scorer names.");
            Check(!valid.Issues.Any(row => row.Collection == "archived_seasons"), "A valid Academy archive must remain available.");
        }
    }

    private static byte[] Fixture(bool duplicate = false, string? archiveTeamType = null, bool includeArchive = false)
    {
        var first = new { id = "first", name = "First Public Player", number = 9, is_active = true, team_type = "First Team",
            statistics = new { appearances = 4, goals = 0 }, medical_notes = "sentinel-private", registration_number = "sentinel-private" };
        var players = new List<object>
        {
            first,
            new { id = "child", name = "Ανδρέας Παπαδόπουλος", number = 7, is_active = true, team_type = "Academy", academy_group_ids = new[] { "u12" },
                date_of_birth = "2016-01-02", parent_phone = "sentinel-private", bio = "sentinel-private", nationality = "sentinel-private", height = "sentinel-private" },
            new { id = "no-dob", name = "Published Without Birthday", number = 8, is_active = true, team_type = "First Team" },
            new { id = "inactive", name = "Excluded", number = 1, is_active = false },
            new { id = "archived", name = "Excluded", number = 1, is_active = true, is_archived = true },
            new { id = "transferred", name = "Excluded", number = 1, is_active = true, is_transferred = true },
            new { id = "orphan", name = "Unresolved Group", number = 1, is_active = true, team_type = "Academy", academy_group_ids = new[] { "missing" } },
            new { id = "invalid-stats", name = "Invalid Statistics", number = 1, is_active = true, statistics = new { goals = "unknown" } }
        };
        if (duplicate) players.Add(first);
        var root = new Dictionary<string, object?>
        {
            ["schema_version"] = 1, ["snapshot_version"] = 1, ["source_repository"] = ValidatedPublicContentImport.Repository,
            ["source_commit"] = new string('a', 40), ["base_public_sha256"] = new string('b', 64),
            ["exported_at"] = "2026-10-31T00:00:00Z", ["complete_collections"] = PublicSectionsImportValidator.Collections
        };
        foreach (var collection in PublicSectionsImportValidator.Collections) root[collection] = Array.Empty<object>();
        root["players"] = players;
        root["academy_groups"] = new[] { new { id = "u12", name = "U12", coach_id = "coach" } };
        root["staff"] = new[] { new { id = "coach", name = "Public Coach", role = "Head Coach", team_type = "Academy", is_active = true, academy_group_ids = new[] { "u12" }, phone = "sentinel-private" } };
        if (includeArchive || archiveTeamType is not null)
            root["archived_seasons"] = new[] { new { id = "season-archive", season_id = "season", season_name = "2025-2026",
                player_stats = new[] { new { player_id = "child", name = "Ανδρέας Παπαδόπουλος", number = 7, team_type = archiveTeamType, statistics = new { goals = 3 } } } } };
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }
}
