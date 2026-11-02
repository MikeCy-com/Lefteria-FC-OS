using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LefteriaFc.Application.PublicContent;

public static partial class PublicSectionsImportValidator
{
    private static bool IsAcademyFixture(JsonElement row) => Text(row, "academy_group_id", 128) is not null || Text(row, "group_id", 128) is not null ||
        Regex.IsMatch((Text(row, "team_type", 100) ?? "") + " " + (Text(row, "competition", 200) ?? ""), "academy|ακαδημ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static PublicSectionSocial[] ClubSocials(JsonElement row, string prefix) => new[] { "facebook", "instagram", "twitter", "youtube", "tiktok" }
        .Where(platform => Url(row, prefix + platform) is not null).Select(platform => new PublicSectionSocial(platform, Url(row, prefix + platform)!)).ToArray();

    private static void ProjectArchives(Dictionary<string, JsonElement[]> rows, List<PublicSectionsDocument> result, List<PublicSectionsIssue> issues,
        HashSet<string> groups, HashSet<string> teams, DateOnly asOf)
    {
        var identities = rows["players"].Where(row => row.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .ToDictionary(row => row.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < rows["archived_seasons"].Length; ordinal++)
        {
            try
            {
                var row = rows["archived_seasons"][ordinal]; var id = Id(row);
                if (!seen.Add(id)) throw Invalid("duplicate_id", "archived_seasons", ordinal);
                var projected = new List<PublicSectionsDocument>();
                foreach (var org in new[] { Club, Academy })
                {
                    // A mixed legacy archive is scoped by each nested row, like
                    // archive_scope.py. None of its attendance/lineup documents
                    // are ever copied into the anonymous projection.
                    var academy = org == Academy;
                    var fixtures = new List<PublicSectionFixture>();
                    foreach (var fixture in Objects(row, "fixtures", 20000).Where(item => IsAcademyFixture(item) == academy))
                    {
                        var temporary = new List<PublicSectionsDocument>();
                        Project("fixtures", Id(fixture), fixture, temporary, groups, teams, asOf, rows["academy_groups"]);
                        if (temporary.Single().OrganizationId != org) throw Skip("archive_fixture_scope_conflict");
                        fixtures.Add(JsonSerializer.Deserialize<PublicSectionFixture>(temporary.Single().Json, Json)!);
                    }
                    var standings = new List<PublicSectionStanding>();
                    foreach (var standing in Objects(row, "standings", 20000).Where(item => IsAcademyFixture(item) == academy))
                    {
                        // Source archive standings can omit organization_id;
                        // their scope comes from the archive's nested markers.
                        var temporary = new List<PublicSectionsDocument>();
                        Project("standings", Id(standing), standing, temporary, groups, teams, asOf, rows["academy_groups"]);
                        var safe = JsonSerializer.Deserialize<PublicSectionStanding>(temporary.Single().Json, Json)!;
                        if (Text(standing, "organization_id", 32) is { } owner && owner != org) throw Skip("archive_standing_scope_conflict");
                        standings.Add(safe with { OrganizationId = org });
                    }
                    var players = new List<PublicSectionArchivePlayer>();
                    foreach (var player in Objects(row, "player_stats", 20000))
                    {
                        var playerId = Required(player, "player_id", 128);
                        var archivedScope = ArchivePlayerScope(player);
                        if (identities.TryGetValue(playerId, out var identity))
                        {
                            // The source player projection defaults an unmarked
                            // known identity to First Team. An archive marker
                            // cannot override a known Academy identity and expose
                            // its full surname through the Club archive.
                            var knownScope = ArchivePlayerScope(identity) ?? false;
                            if (archivedScope is not null && archivedScope != knownScope) throw Skip("archive_player_scope_conflict");
                            archivedScope = knownScope;
                        }
                        if (archivedScope is null) throw Skip("archive_player_scope_unknown");
                        if (archivedScope != academy) continue;
                        var name = Required(player, "name", 200);
                        var stats = player.TryGetProperty("statistics", out var value) && value.ValueKind != JsonValueKind.Null ? value : default;
                        if (stats.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined)) throw Skip("statistics_shape");
                        players.Add(new(playerId, academy ? AcademyDisplayName(name) : name, Integer(player, "number", 0, 999), Image(player, "image_url"),
                            new(Stat(stats, "appearances"), Stat(stats, "goals"), Stat(stats, "assists"), Stat(stats, "minutes_played", 10000000), Stat(stats, "clean_sheets"), Stat(stats, "yellow_cards"), Stat(stats, "red_cards"))));
                    }
                    if (fixtures.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != fixtures.Count ||
                        standings.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != standings.Count ||
                        players.Select(item => item.PlayerId).Distinct(StringComparer.Ordinal).Count() != players.Count) throw Skip("archive_duplicate_identity");
                    if (fixtures.Count + standings.Count + players.Count == 0) continue;
                    var top = players.Where(item => item.Statistics.Goals > 0).OrderByDescending(item => item.Statistics.Goals).ThenBy(item => item.PlayerId, StringComparer.Ordinal).FirstOrDefault();
                    Add(projected, "archive", org, id, new PublicSectionArchive(id, org, Required(row, "season_id", 128), Required(row, "season_name", 200), Timestamp(row, "archived_at"),
                        academy ? "Academy" : "First Team", fixtures, standings, players, top?.Name, top?.Statistics.Goals ?? 0));
                }
                result.AddRange(projected); // A bad nested row quarantines the whole archive, never a misleading partial season.
            }
            catch (ProjectionRefused error) { issues.Add(new("archived_seasons", ordinal, error.Code)); }
        }
    }

    private static bool? ArchivePlayerScope(JsonElement row)
    {
        var type = Text(row, "team_type", 100);
        if (type is not (null or "First Team" or "Academy")) throw Skip("archive_player_team_type");
        var hasGroups = Text(row, "academy_group_id", 128) is not null || Strings(row, "academy_group_ids", 128, 128).Length != 0;
        var organization = Text(row, "organization_id", 32);
        if (organization is not (null or Club or Academy)) throw Skip("archive_player_organization");
        var academy = type == "Academy" || hasGroups || organization == Academy;
        if (organization == Club && academy || organization == Academy && type == "First Team" && !hasGroups)
            throw Skip("archive_player_scope_conflict");
        return type is null && !hasGroups && organization is null ? null : academy;
    }

    private static void ProjectAcademyStats(Dictionary<string, JsonElement[]> rows, List<PublicSectionsDocument> result, List<PublicSectionsIssue> issues, DateTimeOffset asOf)
    {
        var groups = ValidIds(rows["academy_groups"]); var weekdays = new Dictionary<string, HashSet<DayOfWeek>>(StringComparer.Ordinal);
        var trainingInvalid = false;
        for (var ordinal = 0; ordinal < rows["training_sessions"].Length; ordinal++)
        {
            try
            {
                var row = rows["training_sessions"][ordinal]; var group = Text(row, "academy_group_id", 128);
                if (group is null) continue; // First-team/private events contribute no public Academy measure.
                if (!groups.Contains(group)) throw Skip("aggregate_group_reference");
                var date = Required(row, "date", 64);
                if (!DateOnly.TryParseExact(date.Length >= 10 ? date[..10] : date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw Skip("aggregate_training_date");
                if (!weekdays.TryGetValue(group, out var days)) weekdays[group] = days = [];
                days.Add(day.DayOfWeek);
            }
            catch (ProjectionRefused error) { trainingInvalid = true; issues.Add(new("training_sessions", ordinal, error.Code)); }
        }
        // The legacy public statistic counts all attendance over 90 days. Keep
        // that aggregate contract; no individual identity/event/member fields
        // enter the DTO. Missing created_at rows do not satisfy its query.
        var present = 0; var total = 0; var attendanceInvalid = false;
        for (var ordinal = 0; ordinal < rows["attendance"].Length; ordinal++)
        {
            try
            {
                var row = rows["attendance"][ordinal]; var created = Timestamp(row, "created_at");
                if (created is null || created < asOf.AddDays(-90) || created > asOf) continue;
                var status = Required(row, "status", 32);
                if (status is not ("present" or "absent" or "Present" or "Absent")) throw Skip("aggregate_attendance_status");
                total++; if (status.Equals("present", StringComparison.OrdinalIgnoreCase)) present++;
            }
            catch (ProjectionRefused error) { attendanceInvalid = true; issues.Add(new("attendance", ordinal, error.Code)); }
        }
        var players = result.Where(item => item.Kind == "player" && item.OrganizationId == Academy).Count();
        Add(result, "academy_stats", Academy, "academy", new PublicSectionAcademyStats("academy", Academy, groups.Count, players,
            trainingInvalid ? null : weekdays.Values.Select(days => (int?)days.Count).Max(),
            attendanceInvalid || total == 0 ? null : (int)Math.Round(100m * present / total, MidpointRounding.ToEven), asOf));
    }

    private static void ProjectPotm(Dictionary<string, JsonElement[]> rows, List<PublicSectionsDocument> result, List<PublicSectionsIssue> issues)
    {
        var players = result.Where(row => row.Kind == "player" && row.OrganizationId == Club)
            .Select(row => JsonSerializer.Deserialize<PublicSectionPlayer>(row.Json, Json)!).ToDictionary(row => row.Id, StringComparer.Ordinal);
        var counts = new Dictionary<(string Month, string Player), int>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < rows["potm_votes"].Length; ordinal++)
        {
            try
            {
                var row = rows["potm_votes"][ordinal]; var id = Id(row);
                if (!seen.Add(id)) throw Invalid("duplicate_id", "potm_votes", ordinal);
                var month = Required(row, "month_key", 7);
                if (!Regex.IsMatch(month, "^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)) throw Skip("potm_month");
                var player = Required(row, "player_id", 128);
                if (!players.ContainsKey(player)) throw Skip("potm_public_player_unavailable");
                counts[(month, player)] = counts.GetValueOrDefault((month, player)) + 1;
            }
            catch (ProjectionRefused error) { issues.Add(new("potm_votes", ordinal, error.Code)); }
        }
        foreach (var month in counts.GroupBy(item => item.Key.Month))
        {
            var candidates = month.OrderByDescending(item => item.Value).ThenBy(item => item.Key.Player, StringComparer.Ordinal)
                .Select(item => { var player = players[item.Key.Player]; return new PublicSectionPotmCandidate(player.Id, player.Name, player.Number, player.Position, player.ImageUrl, item.Value); }).ToArray();
            Add(result, "potm", Club, month.Key, new PublicSectionPotm(month.Key, Club, month.Key, candidates.Sum(item => item.Votes), candidates));
        }
    }

    private static void ProjectModuleStates(List<PublicSectionsDocument> result, List<PublicSectionsIssue> issues)
    {
        var mappings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["player"] = ["players"], ["staff"] = ["staff"], ["academy_group"] = ["academy_groups", "staff"], ["standing"] = ["standings"],
            ["venue"] = ["venues"], ["season"] = ["seasons"], ["gallery"] = ["gallery"], ["sponsor"] = ["sponsors"], ["partner"] = ["partners"],
            ["event"] = ["marketing_events"], ["product"] = ["products"], ["trials"] = ["trials_settings"], ["fixture"] = ["fixtures"],
            ["ticket"] = ["tickets"], ["archive"] = ["archived_seasons"], ["review"] = ["reviews"],
            ["academy_stats"] = ["players", "academy_groups", "training_sessions", "attendance"], ["potm"] = ["potm_votes", "players"], ["branding"] = ["club_profile"]
        };
        foreach (var org in new[] { Club, Academy })
        foreach (var (kind, collections) in mappings)
        {
            if (org == Club && kind is "academy_group" or "academy_stats" || org == Academy && kind is "trials" or "potm") continue;
            var published = result.Count(row => row.OrganizationId == org && row.Kind == kind);
            // Scope-ambiguous bad rows conservatively mark both scopes partial.
            // No raw row, identity, issue text or unpublished-row count is public.
            var quarantined = issues.Count(issue => collections.Contains(issue.Collection, StringComparer.Ordinal) && !issue.Code.StartsWith("excluded_", StringComparison.Ordinal));
            var status = quarantined > 0 ? published > 0 ? "partial" : "unavailable" : published > 0 ? "ready" : "empty";
            Add(result, "module", org, kind, new PublicSectionModule(kind, org, status, published, quarantined));
        }
    }
}
