using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;

namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    // Build once per bounded manifest. No per-subject full collection scans or
    // repeated source JSON parsing are needed to resolve trusted relationships.
    private sealed class PlanningIndex
    {
        public PlanningIndex(AccountImportTrustedContext context, IReadOnlyList<PlannedContactBinding> bindings)
        {
            Players = context.Roster.Players.ToDictionary(row => row.Id, StringComparer.Ordinal);
            Staff = context.Staff.Staff.ToDictionary(row => row.Id, StringComparer.Ordinal);
            TeamIds = context.Roster.Teams.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            GroupIds = context.Roster.Groups.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            Bindings = bindings.ToLookup(row => (row.Kind, row.Phone));
            StaffSourcePhones = context.Staff.Source.Select(source => (source.SourceId, Phone:SourceComparisonPhone(ReadJson(source.Json))))
                .Where(row => row.Phone is not null).ToLookup(row => row.Phone!, row => row.SourceId, StringComparer.Ordinal);
            PlayerSourcePhones = context.Roster.Source.Where(source => source.Collection == "players")
                .Select(source => (source.SourceId, Phone:SourceComparisonPhone(ReadJson(source.Json))))
                .Where(row => row.Phone is not null).ToLookup(row => row.Phone!, row => row.SourceId, StringComparer.Ordinal);
            var groups = new Dictionary<string,List<string>>(StringComparer.Ordinal);
            foreach (var source in context.Roster.Source.Where(source => source.Collection == "academy_groups"))
            {
                var staffId = IdValue(ReadJson(source.Json), "coach_id");
                if (staffId is null) continue;
                if (!groups.TryGetValue(staffId, out var assigned)) groups.Add(staffId, assigned = []);
                assigned.Add(source.SourceId);
            }
            CoachGroups = groups.ToDictionary(pair => pair.Key,
                pair => pair.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        }
        public void SetAccounts(IReadOnlyList<Row> rows)
        {
            AdminsById = rows.Where(row => row.Valid && row.Admin is not null).ToLookup(row => row.LegacyId!, StringComparer.Ordinal);
            // A quarantined competing source row still makes phone-only
            // selection ambiguous. Explicit verified IDs may select one row.
            AdminsByPhone = rows.Where(row => row.Source.Collection == "admin_users")
                .Select(row => (Row:row, Phone:SourceComparisonPhone(row.Json))).Where(row => row.Phone is not null)
                .ToLookup(row => row.Phone!, row => row.Row, StringComparer.Ordinal);
            FansByPhone = rows.Where(row => row.Valid && row.Fan is { IsActive:true }).ToLookup(row => row.Fan!.Phone, StringComparer.Ordinal);
        }
        public Dictionary<string,RosterPlayerInput> Players { get; }
        public Dictionary<string,StaffRelationshipInput> Staff { get; }
        public HashSet<string> TeamIds { get; }
        public HashSet<string> GroupIds { get; }
        public ILookup<string,Row> AdminsById { get; private set; } = Array.Empty<Row>().ToLookup(row => "", StringComparer.Ordinal);
        public ILookup<string,Row> AdminsByPhone { get; private set; } = Array.Empty<Row>().ToLookup(row => "", StringComparer.Ordinal);
        public ILookup<string,Row> FansByPhone { get; private set; } = Array.Empty<Row>().ToLookup(row => "", StringComparer.Ordinal);
        public ILookup<(string Kind,string Phone),PlannedContactBinding> Bindings { get; }
        public ILookup<string,string> StaffSourcePhones { get; }
        public ILookup<string,string> PlayerSourcePhones { get; }
        public Dictionary<string,string[]> CoachGroups { get; }
    }
}
