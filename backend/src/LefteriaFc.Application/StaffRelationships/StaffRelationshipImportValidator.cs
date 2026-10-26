using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.StaffRelationships;

public static partial class StaffRelationshipImportValidator
{
    private static readonly string[] Fields = ["schema_version", "source_repository", "source_commit", "roster_sha256", "complete_collections", "staff"];
    private static readonly string[] Roles = ["Head Coach", "Assistant Coach", "Goalkeeper Coach", "Fitness Coach", "Physiotherapist", "Team Manager", "Youth Coach", "Scout"];
    public static ValidatedStaffRelationshipImport Validate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > RosterImportValidator.MaximumBytes) throw Invalid("input_size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("manifest_object_required");
            DuplicateKeys(root);
            if (root.EnumerateObject().Any(property => !Fields.Contains(property.Name, StringComparer.Ordinal))) throw Invalid("unknown_manifest_field");
            if (!root.TryGetProperty("schema_version", out var version) || !version.TryGetInt32(out var number) || number != 1) throw Invalid("schema_version");
            if (Text(root, "source_repository", 100) != ValidatedRosterImport.Repository) throw Invalid("source_repository");
            var commit = Text(root, "source_commit", 40); var rosterDigest = Text(root, "roster_sha256", 64);
            if (!Commit().IsMatch(commit)) throw Invalid("source_commit");
            if (!Digest().IsMatch(rosterDigest)) throw Invalid("roster_sha256");
            if (!root.TryGetProperty("complete_collections", out var collections) || collections.ValueKind != JsonValueKind.Array ||
                collections.GetArrayLength() != 1 || collections[0].ValueKind != JsonValueKind.String || collections[0].GetString() != "staff") throw Invalid("complete_collections");
            if (!root.TryGetProperty("staff", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 5000) throw Invalid("collection_shape");
            var staff = new List<StaffRelationshipInput>(); var source = new List<RosterSourceInput>(); var contacts = new List<ContactProvenanceInput>();
            var ids = new HashSet<string>(StringComparer.Ordinal); var index = 0;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) throw Invalid("row_object_required", index);
                var id = Identifier(row.GetProperty("id"), index);
                if (!ids.Add(id)) throw Invalid("duplicate_id", index);
                var role = Text(row, "role", 64, index); if (!Roles.Contains(role, StringComparer.Ordinal)) throw Invalid("staff_job_role", index);
                var type = row.TryGetProperty("team_type", out _) ? Text(row, "team_type", 16, index) : "First Team";
                if (type is not ("First Team" or "Academy")) throw Invalid("team_type", index);
                if (row.TryGetProperty("organization_id", out var org) && org.ValueKind != JsonValueKind.Null &&
                    (org.ValueKind != JsonValueKind.String || !OrganizationIds.IsKnown(org.GetString()))) throw Invalid("organization_id", index);
                staff.Add(new(id, Text(row, "name", 200, index), role, type,
                    Flag(row, "is_active", true, index), Flag(row, "is_archived", false, index),
                    row.TryGetProperty("is_active", out _), row.TryGetProperty("is_archived", out _),
                    Assignments(row, "team_ids", "team_id", index), Assignments(row, "academy_group_ids", "academy_group_id", index)));
                source.Add(new("staff", id, row.GetRawText()));
                contacts.Add(ReadContact(row, "staff", id, "phone", "staff_own", 128, index));
                contacts.Add(ReadContact(row, "staff", id, "email", "staff_own", 320, index));
                index++;
            }
            return new(Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant(), rosterDigest.ToLowerInvariant(), commit.ToLowerInvariant(), staff, source, contacts);
        }
        catch (JsonException) { throw Invalid("invalid_json"); }
        catch (KeyNotFoundException) { throw Invalid("id_required"); }
        catch (InvalidOperationException) { throw Invalid("invalid_type"); }
    }
    public static ContactProvenanceInput ReadContact(JsonElement row, string collection, string id, string field, string kind, int maximum, int? index = null)
    {
        if (!row.TryGetProperty(field, out var value)) return new(collection, id, field, kind, "missing", null);
        if (value.ValueKind == JsonValueKind.Null) return new(collection, id, field, kind, "null", null);
        if (value.ValueKind != JsonValueKind.String) throw Invalid("contact_type", index, collection);
        var raw = value.GetString()!;
        if (raw.Length > maximum || raw.Contains('\0')) throw Invalid("contact_value", index, collection);
        return new(collection, id, field, kind, raw.Length == 0 ? "empty" : "value", raw);
    }
    private static IReadOnlyList<StaffAssignmentInput> Assignments(JsonElement row, string plural, string singular, int index)
    {
        var ids = new Dictionary<string, StaffAssignmentInput>(StringComparer.Ordinal);
        if (row.TryGetProperty(plural, out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 128) throw Invalid("assignment_list", index);
            foreach (var value in list.EnumerateArray()) { var id = Identifier(value, index); ids[id] = new(id, true, false); }
        }
        if (OptionalId(row, singular, index) is { } primary)
            ids[primary] = new(primary, ids.TryGetValue(primary, out var existing) && existing.FromPlural, true);
        return Array.AsReadOnly(ids.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray());
    }
    public static string? OptionalId(JsonElement row, string field, int? index = null)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "") return null;
        return Identifier(value, index);
    }
    private static string Identifier(JsonElement value, int? index)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid("id_type", index);
        var id = value.GetString()!;
        if (id.Length is < 1 or > 128 || id != id.Trim() || id.Any(char.IsControl)) throw Invalid("id_value", index);
        return id;
    }
    private static string Text(JsonElement row, string field, int maximum, int? index = null)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) throw Invalid("string_required", index);
        var text = value.GetString()!;
        if (text.Length == 0 || text.Length > maximum || string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) throw Invalid("string_value", index);
        return text;
    }
    private static bool Flag(JsonElement row, string field, bool fallback, int index)
    {
        if (!row.TryGetProperty(field, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("boolean_flag", index);
        return value.GetBoolean();
    }
    private static void DuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!keys.Add(property.Name)) throw Invalid("duplicate_json_key"); DuplicateKeys(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) DuplicateKeys(item);
    }
    private static RosterImportValidationException Invalid(string code, int? index = null, string collection = "staff") => new(new(code, collection, index));
    [GeneratedRegex("\\A[0-9a-fA-F]{40}\\z", RegexOptions.CultureInvariant)] private static partial Regex Commit();
    [GeneratedRegex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant)] private static partial Regex Digest();
}
