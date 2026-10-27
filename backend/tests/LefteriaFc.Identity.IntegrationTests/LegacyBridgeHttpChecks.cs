using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private const string LegacyExchangePath="/api/mobile/auth/exchange-legacy";
    private async Task LegacyBridgeHttpChecksAsync(AccountImportFixture input)
    {
        using(var disabled=Client())
        {
            var disabledClient=clientSequence;
            try
            {
                disabled.DefaultRequestHeaders.Authorization=new("Bearer","disabled-bridge-probe");
                using var response=await disabled.PostAsync(LegacyExchangePath,null);
                await ExpectAsync(response,503);
            }
            finally{await database.CleanupLegacyBridgeBucketsAsync(factory.OtpKey,[disabledClient]);}
        }
        await using var enabled=database.CreateLegacyBridgeFactory();
        var checks=new IdentityHttpChecks(enabled,database){clientSequence=100};
        try{await checks.RunLegacyBridgeHttpChecksAsync(input);}
        finally{await database.CleanupLegacyBridgeBucketsAsync(enabled.OtpKey,Enumerable.Range(101,checks.clientSequence-100));}
    }

    private async Task RunLegacyBridgeHttpChecksAsync(AccountImportFixture input)
    {
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var expires=now+1200;
        var fan=input.Plan.Mobiles.Single(row=>row.Role=="fan");
        var tokens=new Dictionary<string,string>(StringComparer.Ordinal);
        await database.AssertLegacyBridgeViewRecoveryAsync(input);
        await database.AssertLegacySubjectViewAsync(input);
        await using var context=database.CreateContext();
        var before=(await context.MobileIdentities.CountAsync(),await context.AdminAccounts.CountAsync(),await context.FanRegistrationEvidence.CountAsync(),
            await context.LegacyAccountMappings.CountAsync(),await context.AccountSourceDocuments.CountAsync());
        foreach(var account in input.Plan.Mobiles)
        {
            var legacy=BridgeLegacyToken(account.LegacyId,account.Phone,account.Role,now-60,expires,account.Role=="fan"?null:Guid.NewGuid().ToString("D"));
            tokens.Add(account.Role,legacy);
            using var client=Client();var login=await ExchangeAsync(client,legacy,200);
            Check.True(login.GetProperty("user").GetProperty("id").GetGuid()==account.Id&&login.GetProperty("role").GetString()==account.Role,
                "Legacy bridge selected a different imported subject or role.");
            var jwt=login.GetProperty("token").GetString()!;var claims=Claims(jwt);
            Check.Equal(expires,claims.GetProperty("exp").GetInt64(),"Bridge extended or shortened the original absolute expiration.");
            Check.True(claims.GetProperty("sid").GetString() is{Length:>0},"Bridge did not issue a server-controlled signed session identifier.");
            client.DefaultRequestHeaders.Authorization=new("Bearer",jwt);
            await GetAsync(client,"/api/mobile/auth/me",200);
        }
        // Neither historical no-jti nor current jti is a device identifier.
        // Exact compact-token replay binds the same server session; a different
        // signed compact token has a different token-bound fingerprint.
        using var replay=Client();var original=await ExchangeAsync(replay,tokens["fan"],200);
        var originalClaims=Claims(original.GetProperty("token").GetString()!);var sid=originalClaims.GetProperty("sid").GetString();
        var simultaneous=await Task.WhenAll(Enumerable.Range(0,4).Select(async _=>
        {
            using var client=Client();return await ExchangeAsync(client,tokens["fan"],200);
        }));
        foreach(var result in simultaneous)
        {
            var claims=Claims(result.GetProperty("token").GetString()!);
            Check.True(claims.GetProperty("sid").GetString()==sid&&claims.GetProperty("exp").GetInt64()==expires,"Concurrent exchange changed token-bound sid/deadline.");
        }
        using var different=Client();
        var changed=await ExchangeAsync(different,BridgeLegacyToken(fan.LegacyId,fan.Phone,"fan",now-59,expires,Guid.NewGuid().ToString("D")),200);
        Check.True(Claims(changed.GetProperty("token").GetString()!).GetProperty("sid").GetString()!=sid,"Different signed legacy tokens shared an inferred device session.");

        var denied=new List<string>
        {
            BridgeLegacyToken(fan.LegacyId,input.ParentPhone.Digits,"fan",now-60,expires),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"player",now-60,expires),
            BridgeLegacyToken("unmapped-subject-"+Guid.NewGuid().ToString("N"),fan.Phone,"fan",now-60,expires),
            BridgeLegacyToken(input.Prefix+"-conflict-a",input.ConflictPhone.Digits,"fan",now-60,expires),
            BridgeLegacyToken(input.Plan.FanEvidence.Single(row=>row.Phone==input.EvidencePhone.Digits).LegacyId,input.EvidencePhone.Digits,"fan",now-60,expires),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"admin",now-60,expires,type:"access"),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"fan",now-60,now-1),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"fan",now+3600,expires+7200),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"fan",now-60,expires,secret:"wrong-isolated-signing-secret"),
            BridgeLegacyToken(fan.LegacyId,fan.Phone,"fan",now-60,expires,algorithm:"HS512"),
            original.GetProperty("token").GetString()!
        };
        foreach(var compact in denied)
        {
            using var client=Client();var body=await ExchangeAsync(client,compact,401);
            Check.True(!body.GetRawText().Contains(compact,StringComparison.Ordinal)&&!body.GetRawText().Contains(factory.LegacySigningSecret,StringComparison.Ordinal),"Legacy rejection echoed protected token/key data.");
        }
        var nativePhone=database.NewPhone();var nativeId=await database.SeedIdentityAsync(nativePhone,MobileRole.Fan,"Native actor is not a legacy subject");
        try
        {
            using var client=Client();await ExchangeAsync(client,BridgeLegacyToken(nativeId.ToString("D"),nativePhone.Digits,"fan",now-60,expires),401);
            await database.AssertLegacySubjectViewAsync(input);
        }
        finally{await context.MobileIdentities.Where(row=>row.Id==nativeId).ExecuteDeleteAsync();}

        await context.MobileIdentities.Where(row=>row.Id==fan.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false));
        try{using var client=Client();await ExchangeAsync(client,tokens["fan"],401);}
        finally{await context.MobileIdentities.Where(row=>row.Id==fan.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true));}
        await context.RosterPlayers.Where(row=>row.Id==input.ChildId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,true));
        try{using var client=Client();await ExchangeAsync(client,tokens["parent"],401);}
        finally{await context.RosterPlayers.Where(row=>row.Id==input.ChildId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsArchived,false));}
        await context.Staff.Where(row=>row.Id==input.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,false));
        try
        {
            using var coach=Client();using var management=Client();await ExchangeAsync(coach,tokens["coach"],401);await ExchangeAsync(management,tokens["management"],401);
        }
        finally{await context.Staff.Where(row=>row.Id==input.StaffId).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.IsActive,true));}
        var admin=input.Plan.Admins.Single();
        await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.ParentPhone.Digits));
        try{using var client=Client();await ExchangeAsync(client,tokens["management"],401);}
        finally{await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.NormalizedPhone,input.StaffPhone.Digits));}
        await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.InvitePending,true).SetProperty(row=>row.IsActive,false));
        try{using var client=Client();await ExchangeAsync(client,tokens["management"],401);}
        finally{await context.AdminAccounts.Where(row=>row.Id==admin.Id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.InvitePending,false).SetProperty(row=>row.IsActive,true));}
        var link=await context.MobileRecordLinks.AsNoTracking().SingleAsync(row=>row.Role=="player");
        await context.MobileRecordLinks.Where(row=>row.IdentityId==link.IdentityId).ExecuteDeleteAsync();
        try{using var client=Client();await ExchangeAsync(client,tokens["player"],401);}
        finally{context.MobileRecordLinks.Add(link);await context.SaveChangesAsync();}
        await database.RemoveGrantAsync(admin.Id,OrganizationIds.Academy);
        try
        {
            using var client=Client();var login=await ExchangeAsync(client,tokens["management"],200);client.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("token").GetString());
            await PostAsync(client,"/api/mobile/management/organization",new{organization_id=OrganizationIds.Academy},403);
            Check.Equal(1,await context.ImportedAdminGrants.CountAsync(row=>row.AdminAccountId==admin.Id&&row.OrganizationId==OrganizationIds.Academy),"Bridge scope revocation removed historical grant evidence.");
        }
        finally{context.AdminOrganizationGrants.Add(new(){AdminAccountId=admin.Id,OrganizationId=OrganizationIds.Academy});await context.SaveChangesAsync();}

        // Up to this point exchanges changed only audit and rate buckets.
        Check.True((await database.ApplyAccountsAsync(input)).Noop,"Legacy exchange modified imported account/profile/evidence state.");
        Check.Equal(before,(await context.MobileIdentities.CountAsync(),await context.AdminAccounts.CountAsync(),await context.FanRegistrationEvidence.CountAsync(),
            await context.LegacyAccountMappings.CountAsync(),await context.AccountSourceDocuments.CountAsync()),"Exchange created accounts, evidence or source mappings.");
        foreach(var phone in input.Phones)Check.Equal(0,factory.Sms.Count(PhoneNumber.Parse(phone)),"Legacy exchange sent an SMS.");

        replay.DefaultRequestHeaders.Authorization=new("Bearer",original.GetProperty("token").GetString());
        var switched=await PostAsync(replay,"/api/mobile/auth/switch-role",new{role="player"},200);
        var switchedClaims=Claims(switched.GetProperty("token").GetString()!);
        Check.True(switchedClaims.GetProperty("exp").GetInt64()==expires&&switchedClaims.GetProperty("sid").GetString()==sid,"Bridge role switch extended expiry or reconstructed a different device.");
        using var limit=Client();
        var bursts=await Task.WhenAll(Enumerable.Range(0,11).Select(async _=>
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,LegacyExchangePath);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",tokens["fan"]);
            using var response=await limit.SendAsync(request);return (int)response.StatusCode;
        }));
        Check.True(bursts.Count(status=>status==200)==10&&bursts.Count(status=>status==429)==1,"Legacy bridge did not enforce ten requests per client per minute.");
        var actorIds=input.Plan.Mobiles.Select(mobile=>mobile.Id).ToArray();
        var events=await context.SecurityAuditEvents.AsNoTracking().Where(row=>row.Action=="mobile.legacy.exchange"&&actorIds.Contains(row.ActorIdentityId??Guid.Empty)).ToArrayAsync();
        Check.True(events.Length>0,"Successful legacy exchanges did not append audit events.");
        var audit=JsonSerializer.Serialize(events);
        Check.True(!audit.Contains(factory.LegacySigningSecret,StringComparison.Ordinal)&&!audit.Contains(factory.LegacyFingerprintKey,StringComparison.Ordinal)&&!audit.Contains("$2b$",StringComparison.Ordinal)&&
            !input.Phones.Any(phone=>audit.Contains(phone,StringComparison.Ordinal))&&!tokens.Values.Any(compact=>audit.Contains(compact,StringComparison.Ordinal)),"Legacy audit exposed raw tokens, keys, credential hashes or phones.");
    }

    private static async Task<JsonElement> ExchangeAsync(HttpClient client,string compact,int expected)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,LegacyExchangePath);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",compact);
        using var response=await client.SendAsync(request);Check.True(response.Headers.CacheControl?.NoStore==true,"Legacy exchange was cacheable.");
        return await ExpectAsync(response,expected);
    }
    private string BridgeLegacyToken(string subject,string phone,string role,long issued,long expires,string? jti=null,
        string type="mobile_access",string? secret=null,string algorithm="HS256")
    {
        var header=BridgeBase64(JsonSerializer.SerializeToUtf8Bytes(new{alg=algorithm,typ="JWT"}));
        var payload=new Dictionary<string,object>{{"sub",subject},{"phone",phone},{"role",role},{"type",type},{"iat",issued},{"exp",expires}};
        if(jti is not null)payload.Add("jti",jti);
        var input=header+"."+BridgeBase64(JsonSerializer.SerializeToUtf8Bytes(payload));
        return input+"."+BridgeBase64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret??factory.LegacySigningSecret),Encoding.ASCII.GetBytes(input)));
    }
    private static string BridgeBase64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
}
