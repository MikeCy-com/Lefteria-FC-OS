using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LefteriaFc.Application.PublicContent;

public static partial class PublicSectionsImportValidator
{
    private static void Project(string collection, string id, JsonElement row, List<PublicSectionsDocument> result,
        HashSet<string> groupIds, HashSet<string> teamIds, DateOnly asOf, JsonElement[] groupsSource)
    {
        switch (collection)
        {
            case "players":
            {
                if (!Flag(row, "is_active") || Flag(row, "is_archived") || Flag(row, "is_transferred")) throw Skip("excluded_inactive_or_archived");
                var type = Text(row, "team_type", 16) ?? "First Team";
                if (type is not ("First Team" or "Academy")) throw Skip("team_type");
                var memberships = Strings(row, "academy_group_ids", 128, 128).ToHashSet(StringComparer.Ordinal);
                if (Text(row, "academy_group_id", 128) is { } primary) memberships.Add(primary);
                if (memberships.Any(group => !groupIds.Contains(group))) throw Skip("academy_group_reference");
                var academy = type == "Academy" || memberships.Count != 0;
                var organization = Organization(row, academy ? Academy : Club);
                if (organization != (academy ? Academy : Club)) throw Skip("organization_conflict");
                var name = Required(row, "name", 200); var number = Integer(row, "number", 0, 999) ?? throw Skip("required_number");
                var team = Text(row, "team_id", 128);
                if (team is not null && !teamIds.Contains(team)) throw Skip("team_reference");
                var stats = row.TryGetProperty("statistics", out var nested) && nested.ValueKind != JsonValueKind.Null ? nested : default;
                if (stats.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw Skip("statistics_shape");
                PublicSectionStatistics? statistics = stats.ValueKind == JsonValueKind.Undefined ? null : new PublicSectionStatistics(Stat(stats, "appearances"), Stat(stats, "goals"), Stat(stats, "assists"), Stat(stats, "minutes_played", 10000000),
                    Stat(stats, "clean_sheets"), Stat(stats, "yellow_cards"), Stat(stats, "red_cards"));
                var previous = Array.Empty<PublicSectionPreviousClub>();
                if (!academy)
                {
                    previous = Objects(row, "previous_clubs", 100).Select(item => new PublicSectionPreviousClub(Required(item, "club_name", 200))).ToArray();
                }
                var groupName = Text(row, "academy_group_name", 200);
                if (academy && groupName is null && memberships.Count == 1)
                    groupName = groupsSource.Where(group => Text(group, "id", 128) == memberships.Single()).Select(group => Text(group, "name", 200)).SingleOrDefault();
                Add(result, "player", organization, id, new PublicSectionPlayer(id, organization,
                    academy ? AcademyDisplayName(name) : name, number, Image(row, "image_url"), academy ? "Academy" : "First Team",
                    Text(row, "position", 32), PublicAge(row, asOf),
                    academy ? null : Text(row, "nationality", 100), academy ? null : Text(row, "bio", 20000),
                    academy ? null : Text(row, "height", 32), academy ? null : Text(row, "weight", 32), academy ? null : Text(row, "preferred_foot", 32),
                    academy ? null : team, academy ? groupName : null, memberships.Order(StringComparer.Ordinal).ToArray(), statistics, previous), number);
                break;
            }
            case "staff":
            {
                if (!Flag(row, "is_active", true) || Flag(row, "is_archived")) throw Skip("excluded_inactive_or_archived");
                var type = Text(row, "team_type", 16) ?? "First Team";
                if (type is not ("First Team" or "Academy")) throw Skip("team_type");
                var groups = Strings(row, "academy_group_ids", 128, 128).ToHashSet(StringComparer.Ordinal);
                if (Text(row, "academy_group_id", 128) is { } primary) groups.Add(primary);
                foreach (var group in groupsSource.Where(group => Text(group, "coach_id", 128) == id)) groups.Add(Id(group));
                var teams = Strings(row, "team_ids", 128, 128);
                if (groups.Any(group => !groupIds.Contains(group)) || teams.Any(team => !teamIds.Contains(team))) throw Skip("staff_assignment_reference");
                var scopes = new HashSet<string>(StringComparer.Ordinal) { Organization(row, type == "Academy" ? Academy : Club) };
                if (groups.Count != 0) scopes.Add(Academy);
                if (teams.Length != 0) scopes.Add(Club);
                foreach (var org in scopes.Order(StringComparer.Ordinal))
                    Add(result, "staff", org, id, new PublicSectionStaff(id, org, Required(row, "name", 200), Required(row, "role", 100), Image(row, "image_url"), type,
                        Text(row, "nationality", 100), Text(row, "bio", 20000), Text(row, "joined_date", 64), Strings(row, "qualifications", 100, 500), Strings(row, "badges", 100, 200),
                        Text(row, "coaching_philosophy", 10000), Text(row, "favorite_quote", 2000), teams, groups.Order(StringComparer.Ordinal).ToArray(),
                        Objects(row, "previous_experience", 100).Select(item => new PublicSectionExperience(Text(item, "club", 200), Text(item, "role", 200), Text(item, "years", 100))).ToArray()));
                break;
            }
            case "standings":
            {
                var org = Organization(row, Club);
                // Legacy Standing/Create require a string but permit an empty
                // competition. Preserve that unknown label without inventing
                // one; missing/null or malformed values still quarantine.
                if (!row.TryGetProperty("competition", out var competition) || competition.ValueKind == JsonValueKind.Null)
                    throw Skip("required_value");
                var competitionName = Text(row, "competition", 200) ?? "";
                Add(result, "standing", org, id, new PublicSectionStanding(id, org, Required(row, "team_name", 200), Image(row, "team_logo"),
                    Integer(row, "played") ?? 0, Integer(row, "won") ?? 0, Integer(row, "drawn") ?? 0, Integer(row, "lost") ?? 0,
                    Integer(row, "goals_for") ?? 0, Integer(row, "goals_against") ?? 0, Integer(row, "goal_difference", -100000, 100000) ?? 0,
                    Integer(row, "points", -100000, 100000) ?? 0, competitionName, Text(row, "season", 100), Text(row, "form", 100)));
                break;
            }
            case "venues":
                foreach (var org in Scopes(row))
                    Add(result, "venue", org, id, new PublicSectionVenue(id, org, Required(row, "name", 200), Text(row, "address", 1000), Text(row, "city", 200),
                        Text(row, "country", 100), Integer(row, "capacity", 0, 1000000), Text(row, "surface", 100), Image(row, "image_url"), Url(row, "map_url"),
                        Decimal(row, "latitude", -90, 90), Decimal(row, "longitude", -180, 180), Flag(row, "is_home_ground")));
                break;
            case "seasons":
            {
                var type = Text(row, "team_type", 16); var org = Organization(row, type == "Academy" ? Academy : Club);
                Add(result, "season", org, id, new PublicSectionSeason(id, org, Required(row, "name", 100), Text(row, "start_date", 64), Text(row, "end_date", 64),
                    Flag(row, "is_current"), Flag(row, "is_archived"), Strings(row, "competitions", 100, 200)));
                break;
            }
            case "gallery":
            {
                var group = Text(row, "academy_group_id", 128); var category = Text(row, "category", 100) ?? "Other";
                var org = Organization(row, group is not null || category.Contains("academy", StringComparison.OrdinalIgnoreCase) || category.Contains("ακαδημ", StringComparison.OrdinalIgnoreCase) ? Academy : Club);
                if (group is not null && !groupIds.Contains(group)) throw Skip("academy_group_reference");
                Add(result, "gallery", org, id, new PublicSectionGallery(id, org, Required(row, "title", 1000), Image(row, "image_url") ?? throw Skip("public_image_unavailable"),
                    category, Text(row, "description", 20000), Text(row, "player_id", 128), Text(row, "team_id", 128), group, Flag(row, "is_featured"), Timestamp(row, "created_at")));
                break;
            }
            case "sponsors":
            {
                var type = Text(row, "sponsor_type", 32) ?? "first_team";
                if (type is not ("first_team" or "academy")) throw Skip("sponsor_type");
                var org = Organization(row, type == "academy" ? Academy : Club); var order = Integer(row, "display_order", -100000, 100000) ?? 0;
                Add(result, "sponsor", org, id, new PublicSectionSponsor(id, org, Required(row, "name", 200), Text(row, "description", 20000), Image(row, "logo_url"), Image(row, "banner_url"),
                    Url(row, "website"), Url(row, "facebook"), Url(row, "instagram"), Url(row, "twitter"), Url(row, "youtube"), Url(row, "linkedin"),
                    Text(row, "level", 32) ?? "supporter", type, order, Blocks(row)), order);
                break;
            }
            case "partners":
            {
                if (!Flag(row, "is_active")) throw Skip("excluded_inactive_partner");
                foreach (var org in Scopes(row))
                {
                    var order = Integer(row, "display_order", -100000, 100000) ?? 100;
                    Add(result, "partner", org, id, new PublicSectionPartner(id, org, Required(row, "slug", 200), Required(row, "name", 200), Text(row, "partner_type", 64) ?? "technical",
                        Text(row, "tagline", 2000), Image(row, "hero_image_url"), Image(row, "headshot_url"), Text(row, "executive_name", 200), Text(row, "executive_role", 200), Text(row, "bio", 100000),
                        Strings(row, "bio_bullets", 100, 2000), Objects(row, "staff_members", 100).Select(item => new PublicSectionPartnerStaff(Required(item, "name", 200), Text(item, "role", 200), Image(item, "photo_url"), Text(item, "bio", 20000))).ToArray(),
                        Objects(row, "benefits", 100).Select(item => new PublicSectionBenefit(Required(item, "title", 500), Text(item, "description", 10000), Text(item, "icon", 100))).ToArray(),
                        Url(row, "website_url"), Socials(row), Text(row, "cta_title", 2000), Text(row, "cta_text", 10000), Text(row, "cta_button_1_label", 500), Url(row, "cta_button_1_url"),
                        Text(row, "cta_button_2_label", 500), Url(row, "cta_button_2_url"), order), order);
                }
                break;
            }
            case "marketing_events":
            {
                var status = Required(row, "status", 32);
                if (status is not ("live" or "completed" or "cancelled")) throw Skip("excluded_event_not_published");
                var start = Timestamp(row, "start_at") ?? throw Skip("required_event_start");
                var end = Timestamp(row, "end_at");
                if (end < start) throw Skip("event_interval");
                foreach (var org in Scopes(row)) Add(result, "event", org, id, new PublicSectionEvent(id, org, Required(row, "title", 1000), Text(row, "description", 100000),
                    Text(row, "event_type", 32) ?? "other", start, end, Text(row, "location", 2000), Image(row, "cover_image_url"), Integer(row, "capacity", 0, 1000000),
                    Decimal(row, "price_eur", 0, 1000000), Url(row, "registration_url"), status, Flag(row, "is_featured")));
                break;
            }
            case "products":
                foreach (var org in Scopes(row)) Add(result, "product", org, id, new PublicSectionProduct(id, org, Required(row, "name", 500), Text(row, "description", 100000),
                    Decimal(row, "price", 0, 1000000) ?? throw Skip("required_price"), Image(row, "image_url"), Text(row, "category", 100) ?? "clothing", Strings(row, "sizes", 100, 100),
                    Flag(row, "in_stock", true), Text(row, "product_type", 100) ?? "merchandise", Strings(row, "delivery_options", 100, 500)));
                break;
            case "trials_settings":
                if (id != "singleton") throw Skip("unknown_trials_setting");
                Add(result, "trials", Club, id, new PublicSectionTrials(id, Club, Flag(row, "open"), Text(row, "headline", 1000), Text(row, "subtitle", 10000), Text(row, "button_text", 500), Text(row, "closed_message", 10000)));
                break;
            case "tickets":
                if (!Flag(row, "available")) throw Skip("excluded_ticket_unavailable");
                var ticketType = Required(row, "ticket_type", 32);
                if (ticketType is not ("match" or "seasonal")) throw Skip("ticket_type");
                var ticketOrg = Organization(row, Club);
                Add(result, "ticket", ticketOrg, id, new PublicSectionTicket(id, ticketOrg, Required(row, "name", 500), Text(row, "description", 100000),
                    Decimal(row, "price", 0, 1000000) ?? throw Skip("required_price"), ticketType, Text(row, "fixture_id", 128), true, Integer(row, "max_quantity", 1, 100000) ?? 100));
                break;
            case "reviews":
                if (Text(row, "status", 32) != "approved") throw Skip("excluded_review_not_approved");
                foreach (var org in Scopes(row)) Add(result, "review", org, id, new PublicSectionReview(id, org, Required(row, "author_name", 200), Text(row, "author_role", 200),
                    Integer(row, "rating", 1, 5) ?? throw Skip("required_rating"), Text(row, "title", 1000), Required(row, "body", 100000), Text(row, "response", 100000), Timestamp(row, "published_at")));
                break;
            case "club_profile":
                var clubOrg = Organization(row, id.Contains("academy", StringComparison.OrdinalIgnoreCase) ? Academy : Club);
                Add(result, "branding", clubOrg, id, new PublicSectionBranding(id, clubOrg, Required(row, "name", 200), Text(row, "greek_name", 200), Image(row, "logo_url"),
                    Text(row, "stadium", 500), Text(row, "home_venue_id", 128), Text(row, "city", 200), Text(row, "country", 100), Text(row, "history", 100000),
                    Integer(row, "stadium_capacity", 0, 1000000), Text(row, "stadium_surface", 200), Text(row, "stadium_dimensions", 200), Image(row, "stadium_image_url"),
                    ClubSocials(row, "first_team_"), ClubSocials(row, "academy_"), ClubSocials(row, "")));
                break;
            case "fixtures":
            {
                var group = Text(row, "academy_group_id", 128) ?? Text(row, "group_id", 128); var type = IsAcademyFixture(row) ? "Academy" : "First Team";
                if (type is not ("First Team" or "Academy")) throw Skip("team_type");
                var org = Organization(row, group is not null || type == "Academy" ? Academy : Club);
                var date = Required(row, "match_date", 64);
                if (!DateOnly.TryParseExact(date.Length >= 10 ? date[..10] : date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw Skip("fixture_date");
                var time = Text(row, "match_time", 16);
                if (time is not null && !Regex.IsMatch(time, "^(?:[01][0-9]|2[0-3]):[0-5][0-9](?::[0-5][0-9])?$", RegexOptions.CultureInvariant)) throw Skip("fixture_time");
                var status = Text(row, "status", 32) ?? "Scheduled";
                status = status.ToLowerInvariant() switch { "finished" or "ft" or "final" => "Completed", "canceled" => "Cancelled", _ => status };
                if (status is not ("Scheduled" or "Live" or "Half Time" or "Completed" or "Postponed" or "Cancelled")) throw Skip("fixture_status");
                Add(result, "fixture", org, id, new PublicSectionFixture(id, org, Required(row, "home_team", 200), Required(row, "away_team", 200), Image(row, "home_team_logo"), Image(row, "away_team_logo"),
                    Integer(row, "home_score", 0, 1000), Integer(row, "away_score", 0, 1000), day, time, Text(row, "venue", 1000), Text(row, "competition", 200), status,
                    Text(row, "team_id", 128), group, Text(row, "season", 100), type));
                break;
            }
        }
    }
    private static int? Stat(JsonElement row, string field, int maximum = 100000) => row.ValueKind == JsonValueKind.Undefined ? null : Integer(row, field, 0, maximum);
    private static int? PublicAge(JsonElement row, DateOnly asOf)
    {
        if (Integer(row, "age", 0, 120) is { } age) return age;
        var source = Text(row, "date_of_birth", 64);
        if (source is null || !DateOnly.TryParseExact(source, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthday) || birthday > asOf) return null;
        var years = asOf.Year - birthday.Year;
        if (birthday.AddYears(years) > asOf) years--;
        return years is >= 0 and <= 120 ? years : null;
    }

    // Exact source public website formatter (playerHelpers.js at380b402):
    // first name + first two UTF-16 surname code units + dot. Never return a
    // full Academy surname to the anonymous API, HTML or slug builder.
    public static string AcademyDisplayName(string name)
    {
        var parts = Regex.Split(name.Trim(), "\\s+", RegexOptions.CultureInvariant);
        if (parts.Length < 2) return name;
        var surname = string.Join(' ', parts.Skip(1));
        return parts[0] + " " + surname[..Math.Min(2, surname.Length)] + ".";
    }
    private static PublicSectionSocial[] Socials(JsonElement row) => Objects(row, "socials", 20)
        .Where(item => Text(item, "platform", 32) is "instagram" or "facebook" or "linkedin" or "youtube" or "tiktok" or "website")
        .Where(item => Url(item, "url") is not null).Select(item => new PublicSectionSocial(Required(item, "platform", 32), Url(item, "url")!)).ToArray();
    private static PublicSectionBlock[] Blocks(JsonElement row) => Objects(row, "content_blocks", 100)
        .Where(item => Text(item, "type", 32) is "text" or "banner" or "offer" or "highlight" or "gallery")
        .Select(item => new PublicSectionBlock(Text(item, "id", 128), Required(item, "type", 32), Text(item, "title", 2000), Text(item, "content", 100000),
            Image(item, "image_url"), Url(item, "link_url"), Text(item, "link_text", 500), Strings(item, "images", 100, 2048).Where(value =>
                LefteriaFc.Application.Roster.RosterImportValidator.PublicImageUrl(value) && (value.StartsWith('/') || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))).ToArray(),
            Objects(item, "items", 100).Select(value => new PublicSectionHighlight(Text(value, "value", 500), Text(value, "label", 500))).ToArray())).ToArray();
}
