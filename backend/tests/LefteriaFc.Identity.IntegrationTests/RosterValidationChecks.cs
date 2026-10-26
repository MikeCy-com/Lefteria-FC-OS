using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Tools.RosterImport;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static async Task RosterValidationAsync()
    {
        var valid = RosterManifest("validation");
        ((JsonArray)valid["players"]!).Add(RosterPlayer("opaque-not-a-uuid", "Private fixture name"));
        var plan = RosterImportValidator.Validate(RosterBytes(valid));
        Check.Equal("opaque-not-a-uuid", plan.Players[0].Id, "Importer coerced an opaque ID to a UUID.");
        Check.True(plan.Source[2].Json.Contains("private_parent_email", StringComparison.Ordinal), "Owner archive dropped unknown private source fields.");
        foreach (var field in new[] { "is_active", "team_type" })
        {
            var defaults = valid.DeepClone().AsObject();
            var player = defaults["players"]![0]!.AsObject();
            player.Remove(field);
            player["has_explicit_picker_fields"] = true;
            player["HasExplicitPickerFields"] = true;
            var imported = RosterImportValidator.Validate(RosterBytes(defaults)).Players.Single();
            Check.True(imported.IsActive && imported.TeamType == "First Team", "Canonical defaults were lost for historical missing fields.");
            Check.True(!imported.HasExplicitPickerFields, "An input property forged explicit source picker-field provenance.");
        }
        var bad = new List<JsonObject>();
        var unknown = valid.DeepClone().AsObject(); unknown["source_commit"] = "bad"; bad.Add(unknown);
        var incomplete = valid.DeepClone().AsObject(); incomplete["complete_collections"] = new JsonArray("teams", "players"); bad.Add(incomplete);
        foreach (var (field, value) in new (string, JsonNode?)[]
        {
            ("id", " spaced "), ("id", new string('x',129)), ("number", "9"), ("is_active", null),
            ("is_archived", "false"), ("team_type", null), ("position", "Unknown"), ("organization_id", ""),
            ("academy_group_ids", null), ("team_id", "dangling"), ("academy_group_id", "dangling")
        })
        {
            var copy = valid.DeepClone().AsObject(); copy["players"]![0]![field] = value; bad.Add(copy);
        }
        foreach (var url in new[] { "javascript:alert(1)", "data:image/png;base64,AA", "//host/image.jpg", "/api/uploads/private_attachments/secret.jpg", "/api/uploads/player_docs/secret.pdf",
            "https://example.invalid/API/uploads/private_attachments/secret.jpg", "https://example.invalid/%61pi/%61ttachments/private",
            "https://example.invalid/api/social/media/private", "https://example.invalid/api/uploads/players/%2e%2e/player_docs/private" })
        {
            var copy = valid.DeepClone().AsObject(); copy["players"]![0]!["image_url"] = url; bad.Add(copy);
        }
        var duplicate = valid.DeepClone().AsObject(); duplicate["players"]!.AsArray().Add(duplicate["players"]![0]!.DeepClone()); bad.Add(duplicate);
        var cross = valid.DeepClone().AsObject(); cross["players"]![0]!["academy_group_id"] = "validation-group"; cross["players"]![0]!["organization_id"] = "lefteria_fc"; bad.Add(cross);
        foreach (var manifest in bad) ExpectInvalidRoster(RosterBytes(manifest));
        var nestedDuplicate = Encoding.UTF8.GetString(RosterBytes(valid)).Replace("\"private_parent_email\":\"fixture-private@example.invalid\"", "\"private\":{\"same\":1,\"same\":2}", StringComparison.Ordinal);
        ExpectInvalidRoster(Encoding.UTF8.GetBytes(nestedDuplicate));
        ExpectInvalidRoster(new byte[RosterImportValidator.MaximumBytes + 1]);

        var oldConnection = Environment.GetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION");
        var output = Console.Out; var errors = Console.Error;
        var path = Path.Combine(Path.GetTempPath(), "lefteria-roster-validation-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllBytesAsync(path, RosterBytes(valid));
            Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION", "deliberately-not-a-connection-string");
            using var captured = new StringWriter();
            Console.SetOut(captured); Console.SetError(captured);
            Check.Equal(0, await RosterImportProgram.Main(["--input", path]), "Default dry-run required a DB connection.");
            Check.True(!captured.ToString().Contains("fixture-private@example.invalid", StringComparison.Ordinal) &&
                !captured.ToString().Contains("Private fixture name", StringComparison.Ordinal), "CLI summary exposed source values.");
        }
        finally
        {
            Console.SetOut(output); Console.SetError(errors);
            Environment.SetEnvironmentVariable("LEFTERIA_ROSTER_IMPORT_CONNECTION", oldConnection);
            File.Delete(path);
        }
        try { await PostgresRosterImport.ApplyAsync(plan, "invalid", plan.Digest, "lefteria_rewrite_identity_tests", "production"); }
        catch (RosterImportApplyException exception) when (exception.Code == "environment_not_allowed") { return; }
        throw new InvalidOperationException("Importer did not reject production before connection setup.");
    }

    private static void ExpectInvalidRoster(byte[] bytes)
    {
        try { RosterImportValidator.Validate(bytes); }
        catch (RosterImportValidationException) { return; }
        throw new InvalidOperationException("Importer accepted invalid/ambiguous source data.");
    }
    private static byte[] RosterBytes(JsonObject manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest);
    private static JsonObject RosterManifest(string prefix) => new()
    {
        ["schema_version"] = 1, ["source_repository"] = "MikeCy-com/Lefteria-FC",
        ["source_commit"] = "7833daceceac1611c368e7bc4df2a82c64b45e1a",
        ["complete_collections"] = new JsonArray("teams", "academy_groups", "players"),
        ["teams"] = new JsonArray(new JsonObject { ["id"] = prefix + "-team", ["name"] = "Fixture FC" }),
        ["academy_groups"] = new JsonArray(new JsonObject { ["id"] = prefix + "-group", ["name"] = "Fixture academy" }),
        ["players"] = new JsonArray()
    };
    private static JsonObject RosterPlayer(string id, string name, int number = 9) => new()
    {
        ["id"] = id, ["name"] = name, ["number"] = number, ["position"] = "Forward",
        ["is_active"] = true, ["team_type"] = "First Team",
        ["image_url"] = "/api/uploads/players/fixture.jpg",
        ["private_parent_email"] = "fixture-private@example.invalid",
        ["quick_notes"] = new JsonObject { ["private"] = "owner-only-fixture-marker" }
    };
}
