using System.Text.Json;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private const string ChatDirectoryPath="/api/mobile/chat/groups";

    private async Task ChatGroupDirectoryHttpChecksAsync(AccountImportFixture input,
        HttpClient parent,HttpClient player,HttpClient coach,HttpClient management,HttpClient fan)
    {
        using(var anonymous=Client())
        using(var denied=await anonymous.GetAsync(ChatDirectoryPath))
        {
            Check.Equal(401,(int)denied.StatusCode,"Unauthenticated visitor entered member group directory.");
            Check.True(denied.Headers.CacheControl?.NoStore==true,
                "Unauthenticated chat response is cacheable.");
        }
        var fanDenied=await GetAsync(fan,ChatDirectoryPath,403);
        Check.Equal("account_unavailable",fanDenied.GetProperty("code").GetString(),
            "Fan was not rejected from private member chat groups.");

        var parentResult=await GetAsync(parent,ChatDirectoryPath,200);
        var playerResult=await GetAsync(player,ChatDirectoryPath,200);
        var coachResult=await GetAsync(coach,ChatDirectoryPath,200);
        var managementResult=await GetAsync(management,ChatDirectoryPath,200);

        AssertGroups(parentResult,[(input.GroupId,OrganizationIds.Academy,"academy_group")]);
        AssertGroups(playerResult,[(input.TeamId,OrganizationIds.Club,"team")]);
        AssertGroups(coachResult,[
            (input.TeamId,OrganizationIds.Club,"team"),
            (input.GroupId,OrganizationIds.Academy,"academy_group")]);
        AssertGroups(managementResult,[(input.TeamId,OrganizationIds.Club,"team")]);

        foreach(var row in new[]{parentResult,playerResult,coachResult,managementResult})
        {
            var body=row.GetRawText();
            Check.True(!body.Contains("private-principal-marker",StringComparison.Ordinal) &&
                !body.Contains("private-staff@example.invalid",StringComparison.Ordinal) &&
                !body.Contains(input.ParentPhone.Digits,StringComparison.Ordinal) &&
                !body.Contains(input.StaffPhone.Digits,StringComparison.Ordinal) &&
                !body.Contains(input.ChildId,StringComparison.Ordinal),
                "Scoped app chat directory exposed private identities or children.");
        }

        // Management sees only their currently selected, granted organization.
        await PostAsync(management,"/api/mobile/management/organization",
            new{organization_id=OrganizationIds.Academy},200);
        try
        {
            AssertGroups(await GetAsync(management,ChatDirectoryPath,200),
                [(input.GroupId,OrganizationIds.Academy,"academy_group")]);
        }
        finally
        {
            await PostAsync(management,"/api/mobile/management/organization",
                new{organization_id=OrganizationIds.Club},200);
        }
    }

    private static void AssertGroups(JsonElement payload,
        (string Id,string Owner,string Kind)[] expected)
    {
        Check.True(payload.GetProperty("ready").GetBoolean(),
            "Chat directory advertised an uninitialized roster.");
        var records=payload.GetProperty("groups").EnumerateArray().ToArray();
        Check.Equal(expected.Length,records.Length,"Chat group directory has incorrect current scope.");
        foreach(var row in records)
        {
            Check.Equal(4,row.EnumerateObject().Count(),
                "Chat directory exposed unexpected private group properties.");
            Check.True(row.GetProperty("name").GetString() is {Length:>0},
                "Chat group lacks its canonical displayed name.");
        }
        foreach(var entry in expected)
            Check.True(records.Any(row=>
                row.GetProperty("id").GetString()==entry.Id &&
                row.GetProperty("organization_id").GetString()==entry.Owner &&
                row.GetProperty("kind").GetString()==entry.Kind),
                "Chat directory leaked or omitted a group after canonical authorization.");
    }
}
