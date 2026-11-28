using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private async Task PotmHttpBoundaryAsync()
    {
        const string path="/api/mobile/potm/ballot";
        using var client=Client();
        using(var unauthenticated=await client.GetAsync(path))
        {
            await ExpectAsync(unauthenticated,401);
            Check.True(unauthenticated.Headers.CacheControl?.NoStore==true,"Anonymous ballot response was cacheable.");
        }

        var phone=database.NewPhone();
        var fanId=await database.SeedIdentityAsync(phone,MobileRole.Fan,"CI private fan name");
        var parentId=await database.SeedIdentityAsync(phone,MobileRole.Parent);
        var login=await LoginAsync(client,phone,"fan");
        var fanToken=login.GetProperty("token").GetString()!;
        string month;
        using(var own=await client.GetAsync(path))
        {
            var result=await ExpectAsync(own,200);
            Check.True(own.Headers.CacheControl?.NoStore==true && own.Headers.CacheControl?.Private==true,
                "Own ballot response was not private/no-store.");
            Check.True(result.EnumerateObject().Select(x=>x.Name).OrderBy(x=>x,StringComparer.Ordinal)
                .SequenceEqual(new[]{"month_key","my_vote_player_id","revision","voting_open"}),"Unexpected private ballot fields.");
            month=result.GetProperty("month_key").GetString()!;
            Check.True(!result.GetProperty("voting_open").GetBoolean(),"Migration opened an unapproved period.");
            Check.Equal(0,result.GetProperty("revision").GetInt32(),"New Fan already has a ballot.");
            Check.True(result.GetProperty("my_vote_player_id").ValueKind==JsonValueKind.Null,"Unexpected existing vote.");
        }
        await GetAsync(client,path+"?identity_id="+parentId,400);
        foreach(var body in new[]{
            "{\"month_key\":\""+month+"\",\"expected_revision\":0}",
            "{\"month_key\":\""+month+"\",\"expected_revision\":0,\"player_id\":null,\"identity_id\":\""+parentId+"\"}",
            "{\"month_key\":\""+month+"\",\"expected_revision\":0,\"player_id\":null,\"player_id\":\"forged\"}",
            "{\"month_key\":\""+month+"\",\"expected_revision\":\"0\",\"player_id\":null}",
            "{\"month_key\":\""+month+"\",\"expected_revision\":-1,\"player_id\":null}",
            "{\"month_key\":\"2026-99\",\"expected_revision\":0,\"player_id\":null}",
            "{\"month_key\":\""+month+"\",\"expected_revision\":0,\"player_id\":\" \"}"
        })
        {
            using var invalid=await client.PutAsync(path,new StringContent(body,Encoding.UTF8,"application/json"));
            var fault=await ExpectAsync(invalid,400);
            Check.Equal("invalid_potm_input",fault.GetProperty("code").GetString(),"Unexpected ballot validation result.");
        }
        using(var oversized=await client.PutAsync(path,new StringContent(new string(' ',4097),Encoding.UTF8,"application/json")))
            await ExpectAsync(oversized,413);
        using(var closed=await client.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id="candidate"}))
        {
            var result=await ExpectAsync(closed,409);
            Check.Equal("potm_voting_closed",result.GetProperty("code").GetString(),"Closed vote gate did not reject mutation.");
        }
        using(var withdrawal=await client.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id=(string?)null}))
            await ExpectAsync(withdrawal,409);

        // A genuine role switch retains the same verified phone but cannot
        // access the separate Fan ballot while acting as Parent.
        var parent=await PostAsync(client,"/api/mobile/auth/switch-role",new{role="parent"},200);
        client.DefaultRequestHeaders.Authorization=new("Bearer",parent.GetProperty("token").GetString());
        await GetAsync(client,path,403);
        using(var forbidden=await client.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id="candidate"}))
            await ExpectAsync(forbidden,403);
        client.DefaultRequestHeaders.Authorization=new("Bearer",SignedToken(parentId,phone,
            IdentityApplicationFactory.Issuer,IdentityApplicationFactory.Audience,"mobile_access","fan",false));
        await GetAsync(client,path,403);

        var wrongPhone=database.NewPhone();
        client.DefaultRequestHeaders.Authorization=new("Bearer",SignedToken(fanId,wrongPhone,
            IdentityApplicationFactory.Issuer,IdentityApplicationFactory.Audience,"mobile_access","fan",false));
        await GetAsync(client,path,403);
        client.DefaultRequestHeaders.Authorization=new("Bearer",SignedToken(fanId,phone,
            IdentityApplicationFactory.Issuer,IdentityApplicationFactory.Audience,"mobile_access","fan",true));
        await GetAsync(client,path,401);
        client.DefaultRequestHeaders.Authorization=new("Bearer",fanToken);
        await database.SetIdentityActiveAsync(fanId,false);
        try
        {
            await GetAsync(client,path,403);
            using var denied=await client.PutAsJsonAsync(path,new{month_key=month,expected_revision=0,player_id="candidate"});
            await ExpectAsync(denied,403);
        }
        finally{await database.SetIdentityActiveAsync(fanId,true);}
        var unchanged=await GetAsync(client,path,200);
        Check.Equal(0,unchanged.GetProperty("revision").GetInt32(),"Denied HTTP writes mutated the private ballot.");
    }
}
