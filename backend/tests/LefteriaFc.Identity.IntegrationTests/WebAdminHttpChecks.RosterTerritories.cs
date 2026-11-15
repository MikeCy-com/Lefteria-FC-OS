namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private async Task AdminRosterTerritoryManagerAsync()
    {
        var manager=await database.SeedAsync(organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,manager);
        var audience=await database.SeedAudienceAsync();
        var sites=await database.SeedAnnouncementTerritoriesAsync(audience,manager);
        try
        {
            // One First Team squad and one Academy site only.
            var firstTeam=await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie);
            Require(firstTeam.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(1)) &&
                !firstTeam.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(3)),
                "The delegated First Team manager saw an unassigned player or lost its own assigned player.");
            var academy=await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie);
            Require(academy.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(6)) &&
                !academy.Rows.Any(x=>x.GetProperty("id").GetString()==audience.Player(4)),
                "Location manager viewed the child's mixed-location Academy memberships.");

            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                audience.Player(1),[audience.Team1],[audience.Team2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,FcAssignmentPath,
                audience.Player(3),[],[audience.Team1]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(4),[audience.Group1,audience.Group2],[audience.Group2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(6),[audience.Group2],[audience.Group1]))
                await ExpectJsonAsync(denied,403,"not_authorized");

            await database.RevokeAnnouncementTerritoriesAsync(manager,sites);
            Require((await ReadRosterPagesAsync(client,FcPlayersPath,login.Cookie)).Rows.Count==0 &&
                (await ReadRosterPagesAsync(client,AcademyPlayersPath,login.Cookie)).Rows.Count==0,
                "A revoked territory grant still exposed a manager roster.");
            using(var denied=await PutAssignment(client,login.Cookie,AcademyAssignmentPath,
                audience.Player(6),[audience.Group2],[audience.Group2]))
                await ExpectJsonAsync(denied,403,"not_authorized");
        }
        finally
        {
            await database.CleanupAnnouncementTerritoriesAsync(manager,sites);
            await database.CleanupAudienceAsync(audience);
        }
    }
}
