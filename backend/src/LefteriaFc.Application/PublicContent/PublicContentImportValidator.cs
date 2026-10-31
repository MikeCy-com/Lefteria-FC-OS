using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.PublicContent;

namespace LefteriaFc.Application.PublicContent;

public static partial class PublicContentImportValidator
{
    public static ValidatedPublicContentImport Validate(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("input_size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("manifest_object_required");
            DuplicateKeys(root);
            var fields = new[] { "schema_version", "source_repository", "source_commit", "complete_collections" }.Concat(Collections).ToHashSet(StringComparer.Ordinal);
            if (root.EnumerateObject().Any(property => !fields.Contains(property.Name))) throw Invalid("unknown_manifest_field");
            if (!root.TryGetProperty("schema_version", out var version) || !version.TryGetInt32(out var number) || number != 1) throw Invalid("schema_version");
            if (Required(root, "source_repository", 100) != ValidatedPublicContentImport.Repository) throw Invalid("source_repository");
            var commit = Required(root, "source_commit", 40);
            if (!Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant)) throw Invalid("source_commit");
            if (!root.TryGetProperty("complete_collections", out var complete) || complete.ValueKind != JsonValueKind.Array ||
                !complete.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).SequenceEqual(Collections))
                throw Invalid("complete_collections");
            var rows = Collections.ToDictionary(name => name, name => Rows(root, name), StringComparer.Ordinal);
            var documents = new List<PublicContentDocument>();
            var source = new List<PublicContentSource>();
            foreach (var collection in Collections)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < rows[collection].Length; index++)
                {
                    var row = rows[collection][index];
                    if (row.ValueKind != JsonValueKind.Object) throw Invalid("row_object_required", collection, index);
                    var id = Required(row, "id", 128);
                    if (id != id.Trim() || id.Any(char.IsControl)) throw Invalid("id_value", collection, index);
                    if (!ids.Add(id)) throw Invalid("duplicate_id", collection, index);
                    source.Add(new(collection, index, row.GetRawText()));
                    switch (collection)
                    {
                        case "club_profile":
                            var clubOrg = Organization(row, id.EndsWith(":lefteria_fc_academy", StringComparison.Ordinal) ? "lefteria_fc_academy" : "lefteria_fc");
                            var links = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (var key in new[] { "facebook", "instagram", "twitter", "youtube", "tiktok" })
                            {
                                var value = Optional(row, (clubOrg == "lefteria_fc" ? "first_team_" : "academy_") + key, 2048) ?? Optional(row, key, 2048);
                                if (SafeExternalUrl(value) is { } url) links.Add(key, url);
                            }
                            Add(PublicContentKinds.Club, clubOrg, id, new PublicClub(id, clubOrg, Required(row, "name", 200),
                                Optional(row, "greek_name", 200), Integer(row, "founded", 1800, 2200), Optional(row, "description", 20000),
                                Optional(row, "motto", 1000) ?? Optional(row, "slogan", 1000), Optional(row, "history", 100000),
                                Media(row, "logo_url"), Media(row, "stadium_image_url"), Optional(row, "city", 200), Optional(row, "country", 100),
                                Optional(row, "address", 1000), Email(row), Phone(row),
                                SafeExternalUrl(Optional(row, "website", 2048)), links));
                            break;
                        case "teams":
                            var teamOrg = Organization(row, "lefteria_fc");
                            if (teamOrg != "lefteria_fc") throw Invalid("team_organization", collection, index);
                            Add(PublicContentKinds.Team, teamOrg, id, new PublicTeam(id, teamOrg, Required(row, "name", 200),
                                Optional(row, "level", 200), Optional(row, "description", 20000), Media(row, "banner_url")));
                            break;
                        case "academy_groups":
                            var groupOrg = Organization(row, "lefteria_fc_academy");
                            if (groupOrg != "lefteria_fc_academy") throw Invalid("group_organization", collection, index);
                            var order = Integer(row, "display_order", -100000, 100000) ?? 0;
                            Add(PublicContentKinds.AcademyGroup, groupOrg, id, new PublicAcademyGroup(id, groupOrg, Required(row, "name", 200),
                                Optional(row, "age_range", 200), Optional(row, "city", 200), Optional(row, "location_name", 200),
                                Optional(row, "training_schedule", 10000), Optional(row, "description", 20000), Media(row, "banner_url"),
                                Optional(row, "season", 100), order), order);
                            break;
                        case "news":
                            var category = Optional(row, "category", 200) ?? "Νέα";
                            var audience = Optional(row, "audience", 32) ?? "first_team";
                            foreach (var organization in NewsOrganizations(row, audience, category))
                                Add(PublicContentKinds.News, organization, id, new PublicArticle(id, organization, Required(row, "title", 1000),
                                    Required(row, "content", 500000, allowEmpty: true), Required(row, "excerpt", 20000, allowEmpty: true),
                                    Media(row, "image_url"), category, audience, Flag(row, "is_featured", false), Timestamp(row, "created_at")));
                            break;
                        case "fixtures":
                            var groupId = Optional(row, "academy_group_id", 128);
                            var type = Optional(row, "team_type", 32);
                            var fixtureOrg = Organization(row, groupId is not null || string.Equals(type, "Academy", StringComparison.OrdinalIgnoreCase)
                                ? "lefteria_fc_academy" : "lefteria_fc");
                            var date = Required(row, "match_date", 64);
                            if (!DateOnly.TryParseExact(date.Length >= 10 ? date[..10] : date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                                throw Invalid("fixture_date", collection, index);
                            var time = Optional(row, "match_time", 16);
                            if (time is not null && !Regex.IsMatch(time, "^(?:[01][0-9]|2[0-3]):[0-5][0-9](?::[0-5][0-9])?$", RegexOptions.CultureInvariant))
                                throw Invalid("fixture_time", collection, index);
                            var status = Optional(row, "status", 32) ?? "Scheduled";
                            status = status.ToLowerInvariant() switch { "finished" or "ft" or "final" => "Completed", "canceled" => "Cancelled", _ => status };
                            if (status is not ("Scheduled" or "Live" or "Half Time" or "Completed" or "Postponed" or "Cancelled")) throw Invalid("fixture_status", collection, index);
                            Add(PublicContentKinds.Fixture, fixtureOrg, id, new PublicFixture(id, fixtureOrg, Required(row, "home_team", 200), Required(row, "away_team", 200),
                                Media(row, "home_team_logo"), Media(row, "away_team_logo"), Integer(row, "home_score", 0, 1000), Integer(row, "away_score", 0, 1000),
                                day, time, Optional(row, "venue", 1000), Optional(row, "competition", 200), status, Optional(row, "team_id", 128), groupId));
                            break;
                    }
                }
            }
            if (documents.GroupBy(row => (row.Kind, row.OrganizationId, row.Id)).Any(group => group.Count() != 1) ||
                documents.Where(row => row.Kind == PublicContentKinds.Club).GroupBy(row => row.OrganizationId).Any(group => group.Count() > 1))
                throw Invalid("duplicate_public_target");
            return new(Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant(), commit, bytes.ToArray(), documents.AsReadOnly(), source.AsReadOnly());

            void Add<T>(string kind, string organization, string id, T value, int order = 0) =>
                documents.Add(new(kind, organization, id, JsonSerializer.Serialize(value, JsonOptions), order));
        }
        catch (JsonException) { throw Invalid("invalid_json"); }
        catch (InvalidOperationException) { throw Invalid("invalid_type"); }
    }

    private static string[] NewsOrganizations(JsonElement row, string audience, string category)
    {
        if (Optional(row, "organization_id", 32) is { } explicitOrg) return [Organization(row, explicitOrg)];
        var academyCategory = category.Contains("ακαδημ", StringComparison.OrdinalIgnoreCase) || category.Contains("academy", StringComparison.OrdinalIgnoreCase);
        if (academyCategory || audience == "academy") return ["lefteria_fc_academy"];
        if (audience == "both") return ["lefteria_fc", "lefteria_fc_academy"];
        if (audience is "first_team" or "") return ["lefteria_fc"];
        throw Invalid("news_audience", "news");
    }

    private static string Organization(JsonElement row, string fallback)
    {
        var organization = Optional(row, "organization_id", 32) ?? fallback;
        if (!PublicContentService.IsOrganization(organization)) throw Invalid("organization");
        return organization;
    }
    private static JsonElement[] Rows(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 20000) throw Invalid("collection_shape", name);
        return rows.EnumerateArray().ToArray();
    }
    private static void DuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw Invalid("duplicate_json_key");
                DuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) DuplicateKeys(item);
    }
    private static string Required(JsonElement row, string field, int maximum, bool allowEmpty = false)
    {
        var result = Optional(row, field, maximum, preserveEmpty: allowEmpty);
        return result ?? throw Invalid("required_value");
    }
    private static string? Optional(JsonElement row, string field, int maximum, bool preserveEmpty = false)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid("string_type");
        var result = value.GetString()!;
        if (result.Length > maximum || result.Any(character => character == '\0')) throw Invalid("string_length");
        return result.Length == 0 && !preserveEmpty ? null : result;
    }
    private static int? Integer(JsonElement row, string field, int minimum, int maximum)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < minimum || number > maximum) throw Invalid("integer_value");
        return number;
    }
    private static bool Flag(JsonElement row, string field, bool fallback)
    {
        if (!row.TryGetProperty(field, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("boolean_value");
        return value.GetBoolean();
    }
    private static string? Media(JsonElement row, string field)
    {
        var value = Optional(row, field, 2048);
        return value is not null && RosterImportValidator.PublicImageUrl(value) &&
            (value.StartsWith('/') || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ? value : null;
    }
    private static string? SafeExternalUrl(string? value) => value is not null && !value.Any(char.IsControl) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) ? value : null;
    private static DateTimeOffset? Timestamp(JsonElement row, string field)
    {
        var value = Optional(row, field, 64);
        return value is not null && value.Length >= 10 && Regex.IsMatch(value, "^[0-9]{4}-[0-9]{2}-[0-9]{2}", RegexOptions.CultureInvariant) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
    }
    private static string? Email(JsonElement row)
    {
        var value = Optional(row, "email", 254);
        return value is not null && Regex.IsMatch(value, "^[^\\s@<>:]+@[^\\s@<>:]+\\.[^\\s@<>:]+$", RegexOptions.CultureInvariant) ? value : null;
    }
    private static string? Phone(JsonElement row)
    {
        var value = Optional(row, "phone", 80);
        return value is not null && Regex.IsMatch(value, "^[+0-9 ()-]{5,40}$", RegexOptions.CultureInvariant) ? value : null;
    }
    private static RosterImportValidationException Invalid(string code, string? collection = null, int? index = null) => new(new(code, collection, index));
}
