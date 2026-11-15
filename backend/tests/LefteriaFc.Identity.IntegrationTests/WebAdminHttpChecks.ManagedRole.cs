using System.Net.Http.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private static async Task<HttpResponseMessage> ChangeManagedRoleAsync(
        HttpClient client,string? cookie,object payload)
    {
        using var request=Mutation("/api/admin/access/users/role",JsonContent.Create(payload));
        if(cookie is not null)request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task ManagedAccountRoleChangeAsync()
    {
        var target=await database.SeedAsync(role:"manager",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var allAdmin=await database.SeedAsync(role:"super_admin",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var limited=await database.SeedAsync(role:"super_admin",organizations:["lefteria_fc"]);
        var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
        using var client=Client();
        var adminLogin=await LoginAsync(client,allAdmin);
        using var targetClient=Client();
        var targetLogin=await LoginAsync(targetClient,target);
        var change=new {id=target.Id,expected_role="manager",
            expected_active=true,next_role="coach"};

        using(var anonymous=Client())
        using(var denied=await ChangeManagedRoleAsync(anonymous,null,change))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        using(var editorClient=Client())
        {
            var editorLogin=await LoginAsync(editorClient,editor);
            using var denied=await ChangeManagedRoleAsync(editorClient,editorLogin.Cookie,change);
            await ExpectJsonAsync(denied,403,"not_authorized");
        }

        using(var self=await ChangeManagedRoleAsync(client,adminLogin.Cookie,
            new {id=allAdmin.Id,expected_role="manager",expected_active=true,next_role="coach"}))
            await ExpectJsonAsync(self,422,"managed_admin_role_invalid");
        using(var elevating=await ChangeManagedRoleAsync(client,adminLogin.Cookie,
            new {id=target.Id,expected_role="manager",expected_active=true,next_role="super_admin"}))
            await ExpectJsonAsync(elevating,422,"managed_admin_role_invalid");
        using(var otherSuper=await ChangeManagedRoleAsync(client,adminLogin.Cookie,
            new {id=limited.Id,expected_role="manager",expected_active=true,next_role="coach"}))
            await ExpectJsonAsync(otherSuper,403,"managed_admin_role_denied");

        using(var limitedClient=Client())
        {
            var limitedLogin=await LoginAsync(limitedClient,limited);
            using var denied=await ChangeManagedRoleAsync(limitedClient,limitedLogin.Cookie,change);
            await ExpectJsonAsync(denied,403,"managed_admin_role_denied");
        }

        using(var stale=await ChangeManagedRoleAsync(client,adminLogin.Cookie,
            new {id=target.Id,expected_role="editor",expected_active=true,next_role="coach"}))
            await ExpectJsonAsync(stale,409,"managed_admin_role_conflict");
        using(var inactiveExpected=await ChangeManagedRoleAsync(client,adminLogin.Cookie,
            new {id=target.Id,expected_role="manager",expected_active=false,next_role="coach"}))
            await ExpectJsonAsync(inactiveExpected,409,"managed_admin_role_conflict");

        Console.WriteLine("CHECK managed roles: blocked by city assignment");
        var territory=await database.SeedManagedRoleTerritoryAsync(target);
        try
        {
            using(var blocked=await ChangeManagedRoleAsync(client,adminLogin.Cookie,change))
                await ExpectJsonAsync(blocked,409,"managed_admin_role_assignments_exist");
            using(var retained=await GetAsync(targetClient,MePath,targetLogin.Cookie))
            {
                var existing=await ExpectJsonAsync(retained,200);
                Require(existing.GetProperty("user").GetProperty("role").GetString()=="manager",
                    "Rejected role change revoked or modified target authority.");
            }
            Require(await database.CountManagedRoleAuditAsync(target)==0,
                "Rejected role change was audited as a successful mutation.");
        }
        finally
        {
            await database.ClearManagedRoleTerritoryAsync(target,territory);
        }

        Console.WriteLine("CHECK managed roles: safe role change and session revocation");
        using(var changed=await ChangeManagedRoleAsync(client,adminLogin.Cookie,change))
        {
            var json=await ExpectJsonAsync(changed,200);
            Require(json.GetProperty("id").GetGuid()==target.Id &&
                json.GetProperty("role").GetString()=="coach" &&
                json.GetProperty("active").GetBoolean(),
                "Role-change response did not confirm exact state.");
        }
        using(var revoked=await GetAsync(targetClient,MePath,targetLogin.Cookie))
            await ExpectJsonAsync(revoked,401,"not_authenticated");
        using(var stale=await ChangeManagedRoleAsync(client,adminLogin.Cookie,change))
            await ExpectJsonAsync(stale,409,"managed_admin_role_conflict");
        using(var fresh=await AttemptAsync(targetClient,target.Username,WebAdminDatabaseFixture.Password))
        {
            var json=await ExpectJsonAsync(fresh,200);
            Require(json.GetProperty("user").GetProperty("role").GetString()=="coach" &&
                json.GetProperty("user").GetProperty("organizations").GetArrayLength()==2,
                "New role authentication lost or gained unauthorized organizations.");
        }
        Require(await database.CountManagedRoleAuditAsync(target)==1,
            "Role transition did not create exactly one successful security audit event.");
    }
}
