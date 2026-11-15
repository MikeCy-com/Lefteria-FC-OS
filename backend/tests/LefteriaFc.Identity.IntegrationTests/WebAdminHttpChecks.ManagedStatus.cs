using System.Net.Http.Json;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private static async Task<HttpResponseMessage> ChangeStatusAsync(
        HttpClient client,string? cookie,object payload)
    {
        using var request=Mutation("/api/admin/access/users/status",JsonContent.Create(payload));
        if(cookie is not null)request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task ManagedAccountStatusAsync()
    {
        var target=await database.SeedAsync(role:"manager",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var administrator=await database.SeedAsync(role:"super_admin",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var limitedAdmin=await database.SeedAsync(role:"super_admin",
            organizations:["lefteria_fc"]);
        var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
        using var client=Client();
        var adminLogin=await LoginAsync(client,administrator);
        using var targetClient=Client();
        var targetLogin=await LoginAsync(targetClient,target);
        var suspend=new {id=target.Id,expectedRole="manager",expectedActive=true,active=false};

        using(var anonymous=Client())
        using(var denied=await ChangeStatusAsync(anonymous,null,suspend))
            await ExpectJsonAsync(denied,401,"not_authenticated");
        using(var editorClient=Client())
        {
            var editorLogin=await LoginAsync(editorClient,editor);
            using var denied=await ChangeStatusAsync(editorClient,editorLogin.Cookie,suspend);
            await ExpectJsonAsync(denied,403,"not_authorized");
        }
        using(var self=await ChangeStatusAsync(client,adminLogin.Cookie,
            new {id=administrator.Id,expectedRole="manager",expectedActive=true,active=false}))
            await ExpectJsonAsync(self,422,"managed_admin_status_invalid");
        using(var otherSuperAdmin=await ChangeStatusAsync(client,adminLogin.Cookie,
            new {id=limitedAdmin.Id,expectedRole="manager",expectedActive=true,active=false}))
            await ExpectJsonAsync(otherSuperAdmin,403,"managed_admin_status_denied");
        using(var limitedClient=Client())
        {
            var limitedLogin=await LoginAsync(limitedClient,limitedAdmin);
            using var denied=await ChangeStatusAsync(limitedClient,limitedLogin.Cookie,suspend);
            await ExpectJsonAsync(denied,403,"managed_admin_status_denied");
        }
        using(var wrongRole=await ChangeStatusAsync(client,adminLogin.Cookie,
            new {id=target.Id,expectedRole="coach",expectedActive=true,active=false}))
            await ExpectJsonAsync(wrongRole,409,"managed_admin_status_conflict");
        using(var unknown=await ChangeStatusAsync(client,adminLogin.Cookie,
            new {id=Guid.NewGuid(),expectedRole="manager",expectedActive=true,active=false}))
            await ExpectJsonAsync(unknown,403,"managed_admin_status_denied");

        using(var changed=await ChangeStatusAsync(client,adminLogin.Cookie,suspend))
        {
            var json=await ExpectJsonAsync(changed,200);
            Require(json.GetProperty("id").GetGuid()==target.Id &&
                json.GetProperty("role").GetString()=="manager" &&
                json.GetProperty("active").GetBoolean()==false,
                "Managed status writer returned unconfirmed state.");
        }
        using(var expired=await GetAsync(targetClient,MePath,targetLogin.Cookie))
            await ExpectJsonAsync(expired,401,"not_authenticated");
        using(var rejected=await AttemptAsync(targetClient,target.Username,WebAdminDatabaseFixture.Password))
            await ExpectJsonAsync(rejected,401,"credentials_invalid");
        using(var stale=await ChangeStatusAsync(client,adminLogin.Cookie,suspend))
            await ExpectJsonAsync(stale,409,"managed_admin_status_conflict");

        // Historical cookies must stay invalid after reactivation; users
        // authenticate again using their existing, unchanged credential.
        using(var restored=await ChangeStatusAsync(client,adminLogin.Cookie,
            new {id=target.Id,expectedRole="manager",expectedActive=false,active=true}))
        {
            var json=await ExpectJsonAsync(restored,200);
            Require(json.GetProperty("active").GetBoolean(),"Account was not restored.");
        }
        using(var oldCookie=await GetAsync(targetClient,MePath,targetLogin.Cookie))
            await ExpectJsonAsync(oldCookie,401,"not_authenticated");
        using(var fresh=await AttemptAsync(targetClient,target.Username,WebAdminDatabaseFixture.Password))
        {
            var json=await ExpectJsonAsync(fresh,200);
            Require(json.GetProperty("user").GetProperty("role").GetString()=="manager" &&
                json.GetProperty("user").GetProperty("organizations").GetArrayLength()==2,
                "Restored account lost or gained unauthorized organization grants.");
        }
        using(var scope=factory.Services.CreateScope())
        await using(var connection=await scope.ServiceProvider
            .GetRequiredService<WebAdminConnections>().OpenAsync(CancellationToken.None))
        {
            await using var command=new NpgsqlCommand("""
                SELECT count(*) FROM identity.admin_sessions
                WHERE admin_account_id=@target AND revoked_at IS NULL
                """,connection);
            command.Parameters.AddWithValue("target",target.Id);
            // Only the one fresh login survives, never the old session.
            Require(Convert.ToInt64(await command.ExecuteScalarAsync())==1,
                "Suspension/reactivation resurrected a historical admin session.");
        }
    }
}
