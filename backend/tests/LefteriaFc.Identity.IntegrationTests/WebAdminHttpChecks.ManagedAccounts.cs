using System.Net.Http.Json;
using Npgsql;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.Extensions.DependencyInjection;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private const string ManageAccountsPath="/api/admin/access/users";

    private static async Task<HttpResponseMessage> CreateManagedAsync(
        HttpClient client,string? cookie,object payload)
    {
        using var request=Mutation(ManageAccountsPath,JsonContent.Create(payload));
        if(cookie is not null)request.Headers.Add("Cookie",cookie);
        return await client.SendAsync(request);
    }

    private async Task ManagedAdminCredentialsAsync()
    {
        var username="managed-fixture-"+Guid.NewGuid().ToString("N");
        const string password="Direct-Test-Strong-Account-26";
        var body=new {
            username,name="Disposable Manager",role="manager",password,
            organizations=new[]{"lefteria_fc","lefteria_fc_academy"}
        };
        using(var anonymous=Client())
        using(var denied=await CreateManagedAsync(anonymous,null,body))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);
        using(var editorClient=Client())
        {
            var session=await LoginAsync(editorClient,editor);
            using var denied=await CreateManagedAsync(editorClient,session.Cookie,body);
            await ExpectJsonAsync(denied,403,"not_authorized");
        }

        var administrator=await database.SeedAsync(role:"super_admin",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        using var client=Client();
        var login=await LoginAsync(client,administrator);

        Guid createdId;
        using(var created=await CreateManagedAsync(client,login.Cookie,body))
        {
            var json=await ExpectJsonAsync(created,201);
            createdId=json.GetProperty("id").GetGuid();
            database.TrackManagedAccount(createdId,username);
            Require(json.GetProperty("username").GetString()==username &&
                json.GetProperty("role").GetString()=="manager" &&
                json.GetProperty("organizations").GetArrayLength()==2 &&
                !json.GetRawText().Contains(password,StringComparison.Ordinal) &&
                !json.GetRawText().Contains("password_hash",StringComparison.OrdinalIgnoreCase),
                "Managed account creation returned invalid or sensitive data.");
        }

        using(var scope=factory.Services.CreateScope())
        await using(var connection=await scope.ServiceProvider.GetRequiredService<WebAdminConnections>()
            .OpenAsync(CancellationToken.None))
        {
            // Even the web-admin auth role cannot INSERT credentials directly.
            var denied=false;
            try {
                await using var probe=new NpgsqlCommand(
                    "INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active) "+
                    "SELECT gen_random_uuid(),'permission_probe','Probe','manager',true WHERE false",connection);
                await probe.ExecuteNonQueryAsync();
            } catch(PostgresException e) when(e.SqlState=="42501"){denied=true;}
            Require(denied,"Admin-auth role unexpectedly possesses raw credential INSERT.");
            await using var select=new NpgsqlCommand("""
                SELECT a.role,a.is_active,a.invite_pending,a.password_hash,
                  COALESCE(array_agg(g.organization_id ORDER BY g.organization_id),ARRAY[]::text[])
                FROM identity.admin_accounts a
                LEFT JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                WHERE a.id=@id
                GROUP BY a.id,a.role,a.is_active,a.invite_pending,a.password_hash
                """,connection);
            select.Parameters.AddWithValue("id",createdId);
            await using var reader=await select.ExecuteReaderAsync();
            Require(await reader.ReadAsync() &&
                reader.GetString(0)=="manager" && reader.GetBoolean(1) &&
                !reader.GetBoolean(2) && reader.GetString(3).StartsWith("$2b$12$",StringComparison.Ordinal) &&
                reader.GetFieldValue<string[]>(4).SequenceEqual(
                    new[]{"lefteria_fc","lefteria_fc_academy"}) &&
                !await reader.ReadAsync(),
                "Managed credential or organizations were not saved atomically.");
        }

        using(var duplicate=await CreateManagedAsync(client,login.Cookie,body))
            await ExpectJsonAsync(duplicate,409,"managed_admin_username_exists");
        foreach(var invalid in new object[]{
            new {username="invalid-new-admin",name="No elevation",role="super_admin",password,
                organizations=new[]{"lefteria_fc"}},
            new {username="invalid-no-owner",name="No owner",role="manager",password,
                organizations=Array.Empty<string>()},
            new {username="invalid-dup-owner",name="Duplicate owner",role="manager",password,
                organizations=new[]{"lefteria_fc","lefteria_fc"}},
            new {username="invalid-cross-org",name="Foreign owner",role="coach",password,
                organizations=new[]{"malicious_org"}}
        })
            using(var rejected=await CreateManagedAsync(client,login.Cookie,invalid))
                await ExpectJsonAsync(rejected,422,"managed_admin_invalid");

        // Prove the account's credentials can actually authenticate as the
        // bounded manager with *only* the two explicitly granted organizations.
        using(var managedClient=Client())
        using(var response=await AttemptAsync(managedClient,username,password))
        {
            var authorized=await ExpectJsonAsync(response,200);
            var user=authorized.GetProperty("user");
            Require(user.GetProperty("role").GetString()=="manager" &&
                user.GetProperty("organizations").GetArrayLength()==2,
                "Created manager cannot authenticate with verified scoped role.");
        }

        await database.SetStateAsync(administrator,role:"manager");
        using(var denied=await CreateManagedAsync(client,login.Cookie,new{
            username="denied-after-downgrade",name="Denied",role="coach",password,
            organizations=new[]{"lefteria_fc"}}))
            await ExpectJsonAsync(denied,403,"not_authorized");
    }
}
