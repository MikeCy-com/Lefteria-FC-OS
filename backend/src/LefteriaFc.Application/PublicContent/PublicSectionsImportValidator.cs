using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;

namespace LefteriaFc.Application.PublicContent;

public static partial class PublicSectionsImportValidator
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public static readonly IReadOnlyList<string> Collections = Array.AsReadOnly(new[]
    { "players", "staff", "standings", "venues", "seasons", "gallery", "sponsors", "partners", "marketing_events", "products", "trials_settings", "fixtures", "teams", "academy_groups",
        "tickets", "archived_seasons", "reviews", "training_sessions", "attendance", "potm_votes", "club_profile" });
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const string Club = "lefteria_fc";
    private const string Academy = "lefteria_fc_academy";

    public static ValidatedPublicSectionsImport Validate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("input_size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("manifest_object_required");
            UniqueKeys(root);
            var headers = new[] { "schema_version", "snapshot_version", "source_repository", "source_commit", "base_public_sha256", "exported_at", "complete_collections" };
            if (!root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(headers.Concat(Collections))) throw Invalid("manifest_fields");
            if (!root.GetProperty("schema_version").TryGetInt32(out var schema) || schema != 1 ||
                !root.GetProperty("snapshot_version").TryGetInt32(out var version) || version is < 1 or > 100000) throw Invalid("schema_version");
            if (Required(root, "source_repository", 100) != ValidatedPublicContentImport.Repository) throw Invalid("source_repository");
            var commit = Required(root, "source_commit", 40); var baseDigest = Required(root, "base_public_sha256", 64);
            if (!Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant) || !Regex.IsMatch(baseDigest, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant)) throw Invalid("source_identity");
            var exported = Required(root, "exported_at", 64);
            if (!exported.EndsWith('Z') || !exported.Contains('T') || !DateTimeOffset.TryParse(exported, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exportedAt) || exportedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw Invalid("exported_at");
            var complete = root.GetProperty("complete_collections");
            if (complete.ValueKind != JsonValueKind.Array || !complete.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).SequenceEqual(Collections)) throw Invalid("complete_collections");
            var rows = new Dictionary<string, JsonElement[]>(StringComparer.Ordinal);
            var source = new List<PublicSectionsSource>(); var issues = new List<PublicSectionsIssue>();
            foreach (var collection in Collections)
            {
                var array = root.GetProperty(collection);
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 20000) throw Invalid("collection_shape", collection);
                rows[collection] = array.EnumerateArray().ToArray();
                for (var ordinal = 0; ordinal < rows[collection].Length; ordinal++)
                {
                    var row = rows[collection][ordinal];
                    if (row.ValueKind != JsonValueKind.Object) throw Invalid("row_object_required", collection, ordinal);
                    source.Add(new(collection, ordinal, row.GetRawText()));
                }
            }
            var result = new List<PublicSectionsDocument>();
            var groupIds = ValidIds(rows["academy_groups"]); var teamIds = ValidIds(rows["teams"]);
            foreach (var collection in Collections.Where(name => name is not ("teams" or "academy_groups" or "training_sessions" or "attendance" or "potm_votes" or "archived_seasons")))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var ordinal = 0; ordinal < rows[collection].Length; ordinal++)
                {
                    var row = rows[collection][ordinal];
                    try
                    {
                        var id = collection == "trials_settings" ? Required(row, "_id", 128) : Id(row);
                        if (!seen.Add(id)) throw Invalid("duplicate_id", collection, ordinal);
                        Project(collection, id, row, result, groupIds, teamIds, DateOnly.FromDateTime(exportedAt.UtcDateTime), rows["academy_groups"]);
                    }
                    catch (ProjectionRefused error) { issues.Add(new(collection, ordinal, error.Code)); }
                }
            }
            ProjectArchives(rows, result, issues, groupIds, teamIds, DateOnly.FromDateTime(exportedAt.UtcDateTime));
            ProjectAcademyStats(rows, result, issues, exportedAt);
            ProjectPotm(rows, result, issues);
            for (var ordinal = 0; ordinal < rows["academy_groups"].Length; ordinal++)
            {
                var row = rows["academy_groups"][ordinal];
                try
                {
                    var id = Id(row); var coachId = Text(row, "coach_id", 128);
                    var coaches = result.Where(item => item.Kind == "staff" && item.OrganizationId == Academy && item.Id == coachId)
                        .Select(item => JsonSerializer.Deserialize<PublicSectionStaff>(item.Json, Json)!).ToArray();
                    if (coachId is not null && coaches.Length != 1) throw Skip("coach_reference_unavailable");
                    var coach = coaches.SingleOrDefault();
                    Add(result, "academy_group", Academy, id, new PublicSectionGroup(id, Academy, coach?.Id,
                        coach?.Name ?? Text(row, "coach_name", 200), coach?.Role, coach?.ImageUrl));
                }
                catch (ProjectionRefused error) { issues.Add(new("academy_groups", ordinal, error.Code)); }
            }
            ProjectModuleStates(result, issues);
            if (result.GroupBy(item => (item.Kind, item.OrganizationId, item.Id)).Any(group => group.Count() != 1)) throw Invalid("duplicate_projection");
            return new(version, Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant(), commit, baseDigest,
                exportedAt, bytes.ToArray(), result.AsReadOnly(), source.AsReadOnly(), issues.AsReadOnly());
        }
        catch (JsonException) { throw Invalid("invalid_json"); }
        catch (ProjectionRefused) { throw Invalid("invalid_manifest_value"); }
        catch (InvalidOperationException) { throw Invalid("invalid_type"); }
    }

    private static HashSet<string> ValidIds(IEnumerable<JsonElement> rows)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows) try { if (!result.Add(Id(row))) throw Invalid("duplicate_reference_id"); } catch (ProjectionRefused) { }
        return result;
    }
    private static void Add<T>(List<PublicSectionsDocument> result, string kind, string organization, string id, T value, int order = 0) =>
        result.Add(new(kind, organization, id, JsonSerializer.Serialize(value, Json), order));
    private static void UniqueKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!keys.Add(property.Name)) throw Invalid("duplicate_json_key"); UniqueKeys(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) UniqueKeys(item);
    }
    private static string Id(JsonElement row)
    {
        var value = Required(row, "id", 128);
        if (value != value.Trim() || value.Any(char.IsControl)) throw Skip("invalid_id");
        return value;
    }
    private static string Required(JsonElement row, string field, int maximum) => Text(row, field, maximum) ?? throw Skip("required_value");
    private static string? Text(JsonElement row, string field, int maximum)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Skip("string_type");
        var result = value.GetString()!;
        if (result.Length > maximum || result.Contains('\0')) throw Skip("string_length");
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
    private static bool Flag(JsonElement row, string field, bool fallback = false)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Skip("boolean_type");
    }
    private static int? Integer(JsonElement row, string field, int minimum = 0, int maximum = 100000)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result < minimum || result > maximum) throw Skip("integer_value");
        return result;
    }
    private static decimal? Decimal(JsonElement row, string field, decimal minimum, decimal maximum)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var result) || result < minimum || result > maximum) throw Skip("decimal_value");
        return result;
    }
    private static string[] Strings(JsonElement row, string field, int count = 128, int length = 200)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > count) throw Skip("list_shape");
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String && item.GetString() is { } text && text.Length <= length && !text.Contains('\0')
            ? text : throw Skip("list_value")).ToArray();
    }
    private static JsonElement[] Objects(JsonElement row, string field, int count = 128)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > count || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object)) throw Skip("object_list");
        return value.EnumerateArray().ToArray();
    }
    private static string? Image(JsonElement row, string field)
    {
        var value = Text(row, field, 2048);
        return value is not null && RosterImportValidator.PublicImageUrl(value) && (value.StartsWith('/') || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ? value : null;
    }
    private static string? Url(JsonElement row, string field)
    {
        var value = Text(row, field, 2048);
        return value is not null && !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? value : null;
    }
    private static DateTimeOffset? Timestamp(JsonElement row, string field)
    {
        if (row.TryGetProperty(field, out var raw) && raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("$date", out var date))
        {
            // Canonical EJSON dates remain unchanged in private source evidence.
            if (date.ValueKind == JsonValueKind.Object && date.TryGetProperty("$numberLong", out var milliseconds) &&
                milliseconds.ValueKind == JsonValueKind.String && long.TryParse(milliseconds.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var count))
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds(count); } catch (ArgumentOutOfRangeException) { throw Skip("timestamp_value"); }
            }
            if (date.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)) return instant;
            throw Skip("timestamp_value");
        }
        var value = Text(row, field, 64);
        if (value is null) return null;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)) throw Skip("timestamp_value");
        return result;
    }
    private static string Organization(JsonElement row, string fallback)
    {
        var value = Text(row, "organization_id", 32) ?? fallback;
        if (!PublicContentService.IsOrganization(value)) throw Skip("organization_value");
        return value;
    }
    private static string[] Scopes(JsonElement row, string fallback = "both")
    {
        if (Text(row, "organization_id", 32) is { } owner) return [Organization(row, owner)];
        return (Text(row, "team_scope", 32) ?? fallback) switch { "first_team" => [Club], "academy" => [Academy], "both" => [Club, Academy], _ => throw Skip("scope_value") };
    }
    private static ProjectionRefused Skip(string code) => new(code);
    private static RosterImportValidationException Invalid(string code, string? collection = null, int? ordinal = null) => new(new(code, collection, ordinal));
    private sealed class ProjectionRefused(string code) : Exception { public string Code { get; } = code; }
}
