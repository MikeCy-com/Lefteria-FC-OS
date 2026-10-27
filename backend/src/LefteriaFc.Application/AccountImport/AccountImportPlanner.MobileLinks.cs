namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private static IReadOnlyList<PlannedAccountLink> ResolveMobileLinks(Row row,PlanningIndex index)
    {
        var mobile=row.Mobile!;
        if(mobile.Role=="fan")return [];
        if(mobile.Role=="management")
        {
            var explicitId=Alias(row.Json,"linked_admin_id","admin_user_id","linked_id");
            var candidates=(explicitId is null?index.AdminsByPhone[mobile.Phone]:index.AdminsById[explicitId]).ToArray();
            if(candidates.Length!=1||!candidates[0].Valid||candidates[0].Admin is null||candidates[0].Admin!.Phone!=mobile.Phone)throw Problem("management_admin_ambiguous");
            var admin=candidates[0].Admin!;
            row.Mobile=mobile with{AdminId=admin.Id};
            return [];
        }
        if(mobile.Role=="coach")
        {
            var explicitId=Alias(row.Json,"linked_staff_id","linked_id");
            if(explicitId is null&&index.StaffSourcePhones[mobile.Phone].Take(2).Count()!=1)throw Problem("coach_staff_ambiguous");
            var ids=index.Bindings[("staff_own",mobile.Phone)].Where(binding=>
                (explicitId is null||binding.SourceId==explicitId)).Select(binding=>binding.SourceId).ToArray();
            if(ids.Length!=1)throw Problem("coach_staff_ambiguous");
            var staff=index.Staff[ids[0]];
            if(!CoachTitles.Contains(staff.JobRole,StringComparer.Ordinal))throw Problem("coach_job_role_review");
            var teams=staff.Teams.Select(team=>team.Id).ToArray();
            var groups=staff.Groups.Select(group=>group.Id).Concat(index.CoachGroups.GetValueOrDefault(staff.Id)??[]).Distinct(StringComparer.Ordinal).ToArray();
            CheckLegacyScope(row,teams,groups);
            return [new(mobile.Id,mobile.Role,mobile.Phone,staff.Id,"staff_own",true)];
        }
        var kind=mobile.Role=="player"?"player_own":"guardian";
        var matching=index.Bindings[(kind,mobile.Phone)].Select(binding=>binding.SourceId).ToArray();
        var primary=mobile.Role=="player"?Alias(row.Json,"linked_player_id","linked_id"):IdValue(row.Json,"linked_player_id");
        var declared=Ids(row.Json,"linked_player_ids");
        if(primary is not null&&!matching.Contains(primary,StringComparer.Ordinal)||declared.Any(id=>!matching.Contains(id,StringComparer.Ordinal)))throw Problem("player_phone_link_conflict");
        if(mobile.Role=="player")
        {
            if(primary is null&&(matching.Length!=1||index.PlayerSourcePhones[mobile.Phone].Take(2).Count()!=1))throw Problem("player_link_ambiguous");
            matching=[primary??matching[0]];
            if(declared.Any(id=>id!=matching[0]))throw Problem("player_link_conflict");
        }
        if(matching.Length==0)throw Problem("guardian_link_missing");
        var players=matching.Where(index.Players.ContainsKey).Select(id=>index.Players[id]).OrderBy(player=>player.Id,StringComparer.Ordinal).ToArray();
        if(players.Length!=matching.Length)throw Problem("player_reference");
        CheckLegacyScope(row,players.Where(player=>player.TeamId is not null).Select(player=>player.TeamId!).ToArray(),players.SelectMany(player=>player.Groups).Distinct(StringComparer.Ordinal).ToArray());
        primary??=players[0].Id;
        return Array.AsReadOnly(players.Select(player=>new PlannedAccountLink(mobile.Id,mobile.Role,mobile.Phone,player.Id,kind,player.Id==primary)).ToArray());
    }
    private static void CheckLegacyScope(Row row,IReadOnlyCollection<string> teams,IReadOnlyCollection<string> groups)
    {
        if(IdValue(row.Json,"team_id") is { } team&&!teams.Contains(team,StringComparer.Ordinal))throw Problem("legacy_team_link_conflict");
        if(Ids(row.Json,"academy_group_ids").Any(id=>!groups.Contains(id,StringComparer.Ordinal)))throw Problem("legacy_group_link_conflict");
    }
}
