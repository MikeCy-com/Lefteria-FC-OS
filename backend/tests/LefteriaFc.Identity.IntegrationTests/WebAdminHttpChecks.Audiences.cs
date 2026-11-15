using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string FcAudiencePath="/api/admin/football/audiences?organization_id=lefteria_fc";
    private const string AcademyAudiencePath="/api/admin/football/audiences?organization_id=lefteria_fc_academy";

    private static JsonElement GetGroup(JsonElement directory,string groupId)
    {
        var group=directory.GetProperty("groups").EnumerateArray()
            .FirstOrDefault(row=>row.GetProperty("id").GetString()==groupId);
        Require(group.ValueKind==JsonValueKind.Object,"Expected synthetic live group missing.");
        return group;
    }

    private async Task AdminAudienceDirectoryAsync()
    {
        using var anonymous=Client();
        using(var response=await anonymous.GetAsync(FcAudiencePath))
            await ExpectJsonAsync(response,401,"not_authenticated");

        var admin=await database.SeedAsync(role:"super_admin",organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,admin);

        using(var scope=factory.Services.CreateScope())
        await using(var auth=await scope.ServiceProvider.GetRequiredService<WebAdminConnections>()
            .OpenAsync(CancellationToken.None))
        {
            var refused=false;
            try
            {
                await using var cmd=new NpgsqlCommand(
                    "SELECT count(*) FROM club.players",auth);
                await cmd.ExecuteScalarAsync();
            }
            catch(PostgresException e) when(e.SqlState=="42501"){refused=true;}
            Require(refused,"Admin auth acquired raw private roster read rights.");
            refused=false;
            try
            {
                await using var cmd=new NpgsqlCommand(
                    "SELECT club.read_admin_audiences(repeat('0',64),'lefteria_fc')",auth);
                await cmd.ExecuteScalarAsync();
            }
            catch(PostgresException e) when(e.SqlState=="P0001"){refused=true;}
            Require(refused,"Admin audience reader accepted an unbound digest.");
        }

        var fixture=await database.SeedAudienceAsync();
        var scopedManager=await database.SeedAsync(role:"manager",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var territory=await database.SeedAnnouncementTerritoriesAsync(fixture,scopedManager);
        try
        {
            using(var fc=await GetAsync(client,FcAudiencePath,login.Cookie))
            {
                var directory=await ExpectJsonAsync(fc,200);
                Require(directory.GetProperty("organization_id").GetString()=="lefteria_fc" &&
                    directory.GetProperty("ready").GetBoolean() &&
                    directory.EnumerateObject().Count()==4 &&
                    GetGroup(directory,fixture.Team1).GetProperty("active_players").GetInt32()==1 &&
                    GetGroup(directory,fixture.Team2).GetProperty("active_players").GetInt32()==0 &&
                    GetGroup(directory,fixture.Team1).GetProperty("kind").GetString()=="team" &&
                    directory.GetProperty("unassigned_players").GetInt32()>=1,
                    "FC team membership totals or readiness are not from canonical roster.");
                Require(!directory.GetRawText().Contains("Private synthetic roster child",StringComparison.Ordinal) &&
                    !directory.GetRawText().Contains("phone",StringComparison.OrdinalIgnoreCase),
                    "Private youth identity/contact data leaked through audience summary.");
            }
            int academyUnassigned;
            using(var academy=await GetAsync(client,AcademyAudiencePath,login.Cookie))
            {
                var directory=await ExpectJsonAsync(academy,200);
                academyUnassigned=directory.GetProperty("unassigned_players").GetInt32();
                Require(directory.GetProperty("organization_id").GetString()=="lefteria_fc_academy" &&
                    directory.GetProperty("ready").GetBoolean() &&
                    GetGroup(directory,fixture.Group1).GetProperty("active_players").GetInt32()==1 &&
                    GetGroup(directory,fixture.Group2).GetProperty("active_players").GetInt32()==2 &&
                    GetGroup(directory,fixture.Group1).GetProperty("kind").GetString()=="academy_group" &&
                    academyUnassigned>=1 &&
                    !directory.GetRawText().Contains(fixture.Team1,StringComparison.Ordinal),
                    "Academy primary/plural membership union or tenant boundary invalid.");
            }
            // A single tenant/organization grant does not permit discovery
            // of another First Team squad or a sibling Academy location.
            using(var managerClient=Client())
            {
                var scoped=await LoginAsync(managerClient,scopedManager);
                using(var fc=await GetAsync(managerClient,FcAudiencePath,scoped.Cookie))
                {
                    var listing=await ExpectJsonAsync(fc,200);
                    Require(listing.GetProperty("groups").GetArrayLength()==1 &&
                        GetGroup(listing,fixture.Team1).GetProperty("active_players").GetInt32()==1 &&
                        !listing.GetRawText().Contains(fixture.Team2,StringComparison.Ordinal) &&
                        listing.GetProperty("unassigned_players").GetInt32()==0,
                        "First Team manager received an unrelated squad or global unassigned totals.");
                }
                using(var academy=await GetAsync(managerClient,AcademyAudiencePath,scoped.Cookie))
                {
                    var listing=await ExpectJsonAsync(academy,200);
                    Require(listing.GetProperty("groups").GetArrayLength()==1 &&
                        GetGroup(listing,fixture.Group2).GetProperty("active_players").GetInt32()==2 &&
                        !listing.GetRawText().Contains(fixture.Group1,StringComparison.Ordinal) &&
                        listing.GetProperty("unassigned_players").GetInt32()==0,
                        "Location manager discovered a sibling Academy site in the same city.");
                }

                // A distinct manager has an organization grant but no
                // delegated team/city/location permissions.
                var outsider=await database.SeedAsync(role:"manager",
                    organizations:["lefteria_fc","lefteria_fc_academy"]);
                using(var otherClient=Client())
                {
                    var other=await LoginAsync(otherClient,outsider);
                    foreach(var path in new[]{FcAudiencePath,AcademyAudiencePath})
                        using(var empty=await GetAsync(otherClient,path,other.Cookie))
                        {
                            var listing=await ExpectJsonAsync(empty,200);
                            Require(listing.GetProperty("groups").GetArrayLength()==0 &&
                                listing.GetProperty("unassigned_players").GetInt32()==0,
                                "An unscoped manager enumerated private youth audience counts.");
                        }
                }

                // Revocation of exact team/location grants must apply to
                // the SAME still-valid browser cookie, not at next login.
                await database.RevokeAnnouncementTerritoriesAsync(scopedManager,territory);
                foreach(var path in new[]{FcAudiencePath,AcademyAudiencePath})
                    using(var revoked=await GetAsync(managerClient,path,scoped.Cookie))
                    {
                        var listing=await ExpectJsonAsync(revoked,200);
                        Require(listing.GetProperty("groups").GetArrayLength()==0 &&
                            listing.GetProperty("unassigned_players").GetInt32()==0,
                            "Revoked manager retained an old audience selector.");
                    }
            }

            await database.RemoveAudienceMembershipAsync(fixture);
            using(var changed=await GetAsync(client,AcademyAudiencePath,login.Cookie))
            {
                var directory=await ExpectJsonAsync(changed,200);
                Require(GetGroup(directory,fixture.Group2).GetProperty("active_players").GetInt32()==1 &&
                    directory.GetProperty("unassigned_players").GetInt32()==academyUnassigned+1,
                    "Live roster membership change did not invalidate audience counts.");
            }

            foreach(var invalid in new[]{
                "/api/admin/football/audiences",
                "/api/admin/football/audiences?organization_id=all",
                "/api/admin/football/audiences?organization_id=unknown"})
                using(var bad=await GetAsync(client,invalid,login.Cookie))
                    await ExpectJsonAsync(bad,400,"invalid_organization");

            using(var disallowed=Mutation(FcAudiencePath,JsonContent.Create(new{})))
            {
                disallowed.Headers.Add("Cookie",login.Cookie);
                using var response=await client.SendAsync(disallowed);
                await ExpectStatusAsync(response,405);
            }

            var coach=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
            using(var coachClient=Client())
            {
                var coachLogin=await LoginAsync(coachClient,coach);
                using var denied=await GetAsync(coachClient,AcademyAudiencePath,coachLogin.Cookie);
                await ExpectJsonAsync(denied,403,"not_authorized");
            }
            var onlyAcademy=await database.SeedAsync(role:"super_admin",organizations:["lefteria_fc_academy"]);
            using(var academyClient=Client())
            {
                var session=await LoginAsync(academyClient,onlyAcademy);
                using(var denied=await GetAsync(academyClient,FcAudiencePath,session.Cookie))
                    await ExpectJsonAsync(denied,403,"not_authorized");
                using(var permitted=await GetAsync(academyClient,AcademyAudiencePath,session.Cookie))
                    await ExpectJsonAsync(permitted,200);
            }

            await database.SetStateAsync(admin,role:"coach");
            using(var revokedRole=await GetAsync(client,FcAudiencePath,login.Cookie))
                await ExpectJsonAsync(revokedRole,403,"not_authorized");
            await database.SetStateAsync(admin,role:"manager");
            await database.RevokeOrganizationAsync(admin,"lefteria_fc");
            using(var removed=await GetAsync(client,FcAudiencePath,login.Cookie))
                await ExpectJsonAsync(removed,403,"not_authorized");
            await database.ExpireSessionAsync(admin,login.Token);
            using(var expired=await GetAsync(client,AcademyAudiencePath,login.Cookie))
                await ExpectJsonAsync(expired,401,"not_authenticated");
        }
        finally
        {
            await database.CleanupAnnouncementTerritoriesAsync(scopedManager,territory);
            await database.CleanupAudienceAsync(fixture);
        }
    }
}
