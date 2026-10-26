using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.Roster;

public static partial class RosterImportValidator
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly string[] Collections = ["teams", "academy_groups", "players"];
    private static readonly string[] ManifestFields = ["schema_version", "source_repository", "source_commit", "complete_collections", "teams", "academy_groups", "players"];

    public static ValidatedRosterImport Validate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("input_size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("manifest_object_required");
            CheckDuplicateKeys(root, null, null);
            if (root.EnumerateObject().Any(property => !ManifestFields.Contains(property.Name, StringComparer.Ordinal))) throw Invalid("unknown_manifest_field");
            if (!root.TryGetProperty("schema_version", out var version) || !version.TryGetInt32(out var number) || number != 1) throw Invalid("schema_version");
            if (String(root, "source_repository", 100) != ValidatedRosterImport.Repository) throw Invalid("source_repository");
            var commit = String(root, "source_commit", 40);
            if (!CommitPattern().IsMatch(commit)) throw Invalid("source_commit");
            if (!root.TryGetProperty("complete_collections", out var complete) || complete.ValueKind != JsonValueKind.Array ||
                !complete.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).SequenceEqual(Collections))
                throw Invalid("complete_collections");
            var teamsJson = Rows(root, "teams", 1000); var groupsJson = Rows(root, "academy_groups", 2000); var playersJson = Rows(root, "players", 20000);
            var teams = new List<RosterTeamInput>(); var groups = new List<RosterGroupInput>(); var players = new List<RosterPlayerInput>();
            var source = new List<RosterSourceInput>();
            ReadRows(teamsJson, "teams", (row, index) =>
            {
                var id = Id(row, "id", "teams", index); var organization = Organization(row, OrganizationIds.Club, "teams", index);
                if (organization != OrganizationIds.Club) throw Invalid("team_organization", "teams", index);
                teams.Add(new(id, organization, String(row, "name", 200, "teams", index)));
                source.Add(new("teams", id, row.GetRawText()));
            });
            ReadRows(groupsJson, "academy_groups", (row, index) =>
            {
                var id = Id(row, "id", "academy_groups", index); var organization = Organization(row, OrganizationIds.Academy, "academy_groups", index);
                if (organization != OrganizationIds.Academy) throw Invalid("group_organization", "academy_groups", index);
                groups.Add(new(id, organization, String(row, "name", 200, "academy_groups", index)));
                source.Add(new("academy_groups", id, row.GetRawText()));
            });
            ReadRows(playersJson, "players", (row, index) =>
            {
                const string collection = "players";
                var id = Id(row, "id", collection, index);
                var type = row.TryGetProperty("team_type", out _) ? String(row, "team_type", 16, collection, index) : "First Team";
                if (type is not ("First Team" or "Academy")) throw Invalid("team_type", collection, index);
                var primary = OptionalId(row, "academy_group_id", collection, index);
                var memberships = new HashSet<string>(StringComparer.Ordinal);
                if (row.TryGetProperty("academy_group_ids", out var plural))
                {
                    if (plural.ValueKind != JsonValueKind.Array || plural.GetArrayLength() > 128) throw Invalid("group_list", collection, index);
                    foreach (var group in plural.EnumerateArray()) memberships.Add(Identifier(group, collection, index));
                }
                if (primary is not null) memberships.Add(primary);
                var inferred = type == "Academy" || memberships.Count > 0 ? OrganizationIds.Academy : OrganizationIds.Club;
                var organization = Organization(row, inferred, collection, index);
                if (!row.TryGetProperty("number", out var jersey) || jersey.ValueKind != JsonValueKind.Number || !jersey.TryGetInt32(out var shirt)) throw Invalid("player_number", collection, index);
                var position = String(row, "position", 32, collection, index);
                if (position is not ("Goalkeeper" or "Defender" or "Midfielder" or "Forward")) throw Invalid("player_position", collection, index);
                var image = OptionalString(row, "image_url", 2048, collection, index);
                if (image is not null && !PublicImageUrl(image)) throw Invalid("image_url", collection, index);
                players.Add(new(id, organization, String(row, "name", 200, collection, index), shirt, position, type,
                    OptionalId(row, "team_id", collection, index), primary, Array.AsReadOnly(memberships.Order(StringComparer.Ordinal).ToArray()),
                    image, Flag(row, "is_active", true, index), Flag(row, "is_archived", false, index), Flag(row, "is_transferred", false, index),
                    row.TryGetProperty("is_active", out _) && row.TryGetProperty("team_type", out _)));
                source.Add(new(collection, id, row.GetRawText()));
            });
            var teamMap = teams.ToDictionary(row => row.Id, StringComparer.Ordinal);
            var groupMap = groups.ToDictionary(row => row.Id, StringComparer.Ordinal);
            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                if (player.TeamId is not null && (!teamMap.TryGetValue(player.TeamId, out var team) || team.OrganizationId != player.OrganizationId))
                    throw Invalid("team_reference", "players", index);
                if (player.Groups.Any(id => !groupMap.TryGetValue(id, out var group) || group.OrganizationId != player.OrganizationId))
                    throw Invalid("group_reference", "players", index);
            }
            return new(Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant(), commit.ToLowerInvariant(), teams, groups, players, source);
        }
        catch (JsonException) { throw Invalid("invalid_json"); }
        catch (InvalidOperationException) { throw Invalid("invalid_type"); }
    }

    private static JsonElement Rows(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > maximum) throw Invalid("collection_shape", name);
        return rows;
    }
    private static void ReadRows(JsonElement rows, string collection, Action<JsonElement, int> read)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal); var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) throw Invalid("row_object_required", collection, index);
            if (!ids.Add(Id(row, "id", collection, index))) throw Invalid("duplicate_id", collection, index);
            read(row, index++);
        }
    }
    private static void CheckDuplicateKeys(JsonElement value, string? collection, int? index)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw Invalid("duplicate_json_key", collection, index);
                CheckDuplicateKeys(property.Value, Collections.Contains(property.Name, StringComparer.Ordinal) ? property.Name : collection, index);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var row = 0;
            foreach (var item in value.EnumerateArray()) CheckDuplicateKeys(item, collection, index ?? row++);
        }
    }
    private static string Id(JsonElement row, string field, string collection, int index) =>
        row.TryGetProperty(field, out var value) ? Identifier(value, collection, index) : throw Invalid("id_required", collection, index);
    private static string Identifier(JsonElement value, string collection, int index)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid("id_type", collection, index);
        var id = value.GetString()!;
        if (id.Length is < 1 or > 128 || id != id.Trim() || id.Any(char.IsControl)) throw Invalid("id_value", collection, index);
        return id;
    }
    private static string? OptionalId(JsonElement row, string field, string collection, int index)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "") return null;
        return Identifier(value, collection, index);
    }
    private static string String(JsonElement row, string field, int maximum, string? collection = null, int? index = null)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) throw Invalid("string_required", collection, index);
        var text = value.GetString()!;
        if (text.Length == 0 || text.Length > maximum || string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) throw Invalid("string_value", collection, index);
        return text;
    }
    private static string? OptionalString(JsonElement row, string field, int maximum, string collection, int index) =>
        !row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == ""
            ? null : String(row, field, maximum, collection, index);
    private static string Organization(JsonElement row, string fallback, string collection, int index)
    {
        if (!row.TryGetProperty("organization_id", out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind != JsonValueKind.String || !OrganizationIds.IsKnown(value.GetString())) throw Invalid("organization_id", collection, index);
        return value.GetString()!;
    }
    private static bool Flag(JsonElement row, string field, bool fallback, int index)
    {
        if (!row.TryGetProperty(field, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("boolean_flag", "players", index);
        return value.GetBoolean();
    }
    private static RosterImportValidationException Invalid(string code, string? collection = null, int? index = null) => new(new(code, collection, index));
    [GeneratedRegex("\\A[0-9a-fA-F]{40}\\z", RegexOptions.CultureInvariant)] private static partial Regex CommitPattern();
}
