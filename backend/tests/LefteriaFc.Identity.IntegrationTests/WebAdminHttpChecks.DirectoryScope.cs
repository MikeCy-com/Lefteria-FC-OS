using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminHttpChecks
{
    private async Task<(HashSet<Guid> Ids, int Total)> ReadManagedDirectoryAsync(
        HttpClient client, string cookie)
    {
        var seen=new HashSet<Guid>();
        var total=-1;
        const int size=50;
        for(var offset=0;offset<=20000;offset+=size)
        {
            using var response=await GetAsync(client,
                $"/api/admin/access/users?offset={offset}&limit={size}",cookie);
            var json=await ExpectJsonAsync(response,200);
            Require(json.GetProperty("offset").GetInt32()==offset &&
                json.GetProperty("limit").GetInt32()==size,
                "The account directory did not honor bounded pagination.");
            var count=json.GetProperty("total").GetInt32();
            if(total<0)total=count;
            Require(total==count && total>=0,
                "The account directory total changed unexpectedly.");
            var rows=json.GetProperty("accounts").EnumerateArray().ToArray();
            Require(rows.Length<=size,
                "The account directory exceeded its page limit.");
            foreach(var entry in rows)
                Require(seen.Add(entry.GetProperty("id").GetGuid()),
                    "An account appeared twice on separate pages.");
            if(offset+rows.Length>=total)
            {
                Require(seen.Count==total,
                    "An account count includes hidden or unreturned records.");
                return (seen,total);
            }
            Require(rows.Length==size,
                "The directory returned a short page before its declared total.");
        }
        throw new InvalidOperationException("The directory exceeded the maximum bounded scan.");
    }

    private async Task ManagedDirectoryOrganizationScopeAsync()
    {
        // These accounts deliberately cover all four classes: first-team
        // only, Academy only, dual-owner and ownerless legacy profiles.
        var first=await database.SeedAsync(role:"manager",organizations:["lefteria_fc"]);
        var academy=await database.SeedAsync(role:"coach",organizations:["lefteria_fc_academy"]);
        var dual=await database.SeedAsync(role:"accountant",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var ownerless=await database.SeedAsync(role:"editor");
        var fcAdmin=await database.SeedAsync(role:"super_admin",organizations:["lefteria_fc"]);
        var allAdmin=await database.SeedAsync(role:"super_admin",
            organizations:["lefteria_fc","lefteria_fc_academy"]);
        var editor=await database.SeedAsync(role:"editor",organizations:["lefteria_fc"]);

        using(var anonymous=Client())
        using(var denied=await anonymous.GetAsync("/api/admin/access/users?offset=0&limit=50"))
            await ExpectJsonAsync(denied,401,"not_authenticated");

        using(var editorClient=Client())
        {
            var editorSession=await LoginAsync(editorClient,editor);
            using var denied=await GetAsync(editorClient,
                "/api/admin/access/users?offset=0&limit=50",editorSession.Cookie);
            await ExpectJsonAsync(denied,403,"not_authorized");
        }

        using(var client=Client())
        {
            var login=await LoginAsync(client,fcAdmin);
            var scoped=await ReadManagedDirectoryAsync(client,login.Cookie);
            Require(scoped.Ids.Contains(first.Id) && scoped.Ids.Contains(fcAdmin.Id) &&
                scoped.Ids.Contains(editor.Id),
                "First-team Super Admin cannot see exclusively owned accounts.");
            Require(!scoped.Ids.Contains(academy.Id) &&
                !scoped.Ids.Contains(dual.Id) &&
                !scoped.Ids.Contains(ownerless.Id) &&
                !scoped.Ids.Contains(allAdmin.Id),
                "First-team Super Admin saw a foreign, dual-owner or ungranted identity.");

            // Out-of-range pages still return a scoped count, never the
            // unrestricted table count or another organization's presence.
            using var beyond=await GetAsync(client,
                "/api/admin/access/users?offset=20000&limit=50",login.Cookie);
            var empty=await ExpectJsonAsync(beyond,200);
            Require(empty.GetProperty("accounts").GetArrayLength()==0 &&
                empty.GetProperty("total").GetInt32()==scoped.Total,
                "An empty account page leaked the unfiltered total.");
        }

        using(var client=Client())
        {
            var login=await LoginAsync(client,allAdmin);
            var global=await ReadManagedDirectoryAsync(client,login.Cookie);
            Require(global.Ids.Contains(first.Id) &&
                global.Ids.Contains(academy.Id) &&
                global.Ids.Contains(dual.Id) &&
                global.Ids.Contains(fcAdmin.Id) &&
                global.Ids.Contains(allAdmin.Id) &&
                !global.Ids.Contains(ownerless.Id),
                "Two-owner Super Admin did not receive the exact authorized union.");
        }
    }
}
