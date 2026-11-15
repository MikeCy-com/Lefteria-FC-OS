using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private async Task AdminCoachTerritoryAsync()
    {
        var manager=await database.SeedAsync(role:"manager",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var roster=await database.SeedAudienceAsync();
        WebAdminDatabaseFixture.CoachFixture? coaches=null;
        WebAdminDatabaseFixture.AnnouncementTerritoryFixture? sites=null;
        using var client=Client();
        var login=await LoginAsync(client,manager);
        try
        {
            coaches=await database.SeedCoachesAsync(roster);

            // An organization-level manager without explicit football grants
            // must neither enumerate coaches nor rewrite their assignments.
            using(var fc=await GetAsync(client,FcCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(fc,200);
                Require(body.GetProperty("coaches").GetArrayLength()==0 &&
                    body.GetProperty("total").GetInt32()==0,
                    "Ungranted manager could enumerate First Team staff.");
            }
            using(var academy=await GetAsync(client,AcademyCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(academy,200);
                Require(body.GetProperty("coaches").GetArrayLength()==0 &&
                    body.GetProperty("total").GetInt32()==0,
                    "Ungranted manager could enumerate Academy staff.");
            }
            using(var denied=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coaches.Coach1,[roster.Team1],[roster.Team1]))
                await ExpectJsonAsync(denied,403,"not_authorized");

            // Real two-location Academy topology, with a manager delegated
            // exactly one First Team squad and only the second Academy site.
            sites=await database.SeedAnnouncementTerritoriesAsync(roster,manager);

            using(var first=await GetAsync(client,FcCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(first,200);
                Require(body.GetProperty("total").GetInt32()==1 &&
                    body.GetProperty("coaches").EnumerateArray().Any(c=>
                        c.GetProperty("id").GetString()==coaches.Coach1 &&
                        c.GetProperty("group_ids").GetArrayLength()==1 &&
                        c.GetProperty("group_ids")[0].GetString()==roster.Team1) &&
                    !body.GetRawText().Contains(roster.Team2,StringComparison.Ordinal),
                    "Team manager saw a foreign squad or lost the exact assigned coach.");
            }
            using(var academy=await GetAsync(client,AcademyCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(academy,200);
                Require(body.GetProperty("total").GetInt32()==1 &&
                    body.GetProperty("coaches").EnumerateArray().Any(c=>
                        c.GetProperty("id").GetString()==coaches.Coach1 &&
                        c.GetProperty("group_ids").GetArrayLength()==1 &&
                        c.GetProperty("group_ids")[0].GetString()==roster.Group2) &&
                    !body.GetRawText().Contains(roster.Group1,StringComparison.Ordinal),
                    "Location manager saw a sibling Academy site's coach assignment.");
            }

            // Even a manager who sees a coach at their site may not move that
            // identity into another site, or replace groups the coach holds
            // outside their territory.
            using(var saved=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coaches.Coach1,[roster.Team1],[roster.Team1]))
                await ExpectJsonAsync(saved,200);
            using(var denied=await PutCoach(client,login.Cookie,FcCoachAssignmentPath,
                coaches.Coach1,[roster.Team1],[roster.Team1,roster.Team2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutCoach(client,login.Cookie,AcademyCoachAssignmentPath,
                coaches.Coach1,[roster.Group1,roster.Group2],
                [roster.Group1,roster.Group2]))
                await ExpectJsonAsync(denied,403,"not_authorized");

            // City manager has authority over both locations of that city,
            // but never First Team squads outside the explicit team grant.
            await database.GrantAnnouncementCityAsync(manager,sites);
            using(var academy=await GetAsync(client,AcademyCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(academy,200);
                Require(body.GetProperty("coaches").EnumerateArray().Any(c=>
                    c.GetProperty("id").GetString()==coaches.Coach1 &&
                    c.GetProperty("group_ids").GetArrayLength()==2),
                    "City manager could not see both same-city coach assignments.");
            }
            await database.RevokeAnnouncementTerritoriesAsync(manager,sites);
            using(var fc=await GetAsync(client,FcCoachesPath,login.Cookie))
            {
                var body=await ExpectJsonAsync(fc,200);
                Require(body.GetProperty("total").GetInt32()==0,
                    "Revoked First Team grant still exposed coaches.");
            }
        }
        finally
        {
            if(coaches is not null)await database.CleanupCoachesAsync(coaches);
            if(sites is not null)await database.CleanupAnnouncementTerritoriesAsync(manager,sites);
            await database.CleanupAudienceAsync(roster);
        }
    }
}
