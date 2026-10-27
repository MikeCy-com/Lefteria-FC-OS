using System.Text;
using System.Text.Json.Nodes;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static Task AccountPlannerAsync()
    {
        const string commit="2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30";
        const string phone="+357 99 123456";
        var roster=RosterImportValidator.Validate(Encoding.UTF8.GetBytes("""
            {"schema_version":1,"source_repository":"MikeCy-com/Lefteria-FC","source_commit":"2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30","complete_collections":["teams","academy_groups","players"],"teams":[{"id":"account-team","name":"Fictional team"}],"academy_groups":[{"id":"account-group","name":"Fictional group","coach_id":"account-staff"}],"players":[{"id":"account-player","name":"Fictional player","number":7,"position":"Midfielder","team_type":"First Team","is_active":true,"team_id":"account-team","phone":"+357 99 123456"},{"id":"account-child","name":"Fictional child","number":3,"position":"Forward","team_type":"Academy","is_active":true,"academy_group_id":"account-group","parent_phone":"+357 99 123456"}]}
            """));
        var staffManifest=new JsonObject
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=commit,
            ["roster_sha256"]=roster.Digest,["complete_collections"]=new JsonArray("staff"),
            ["staff"]=new JsonArray(new JsonObject{["id"]="account-staff",["name"]="Fictional coach",["role"]="Head Coach",
                ["phone"]=phone,["team_id"]="account-team",["academy_group_id"]="account-group",["admin_user_id"]="account-admin"})
        };
        var staff=StaffRelationshipImportValidator.Validate(RosterBytes(staffManifest));
        var context=new AccountImportTrustedContext(roster.Digest,staff.Digest,commit,roster,staff);
        var manifest=new JsonObject
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=commit,
            ["roster_sha256"]=roster.Digest,["staff_sha256"]=staff.Digest,["exported_at"]="2026-10-26T00:00:00Z",
            ["complete_collections"]=new JsonArray("mobile_users","fans","admin_users"),
            ["mobile_users"]=new JsonArray(),["fans"]=new JsonArray(new JsonObject{["id"]="account-fan-evidence",["phone"]=phone,
                ["first_name"]="Mirror",["last_name"]="Preserved",["birthday"]="1990-01-01"}),
            ["admin_users"]=new JsonArray(new JsonObject{["id"]="account-admin",["username"]="fixture-admin",["name"]="Fictional admin",
                ["role"]="manager",["phone"]=phone,["password_hash"]="$2b$12$"+new string('A',53),["staff_id"]="account-staff",
                ["organization_ids"]=new JsonArray(OrganizationIds.Club,OrganizationIds.Academy)})
        };
        foreach(var role in new[]{"fan","parent","player","coach","management"})
        {
            var mobile=new JsonObject{["id"]="account-mobile-"+role,["phone"]=phone,["role"]=role,["name"]="Fictional "+role};
            if(role=="player")mobile["linked_player_id"]="account-player";
            if(role=="parent")mobile["linked_player_ids"]=new JsonArray("account-child");
            if(role=="coach")mobile["linked_staff_id"]="account-staff";
            if(role=="management")mobile["linked_admin_id"]="account-admin";
            manifest["mobile_users"]!.AsArray().Add(mobile);
        }
        AccountImportPlan Plan(JsonObject input)=>AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(input)),context);
        var plan=Plan(manifest);
        Check.Equal(0,plan.QuarantinedRows,"Clean account fixture was quarantined.");
        Check.Equal(5,plan.Mobiles.Count,"Same-phone separate roles were merged.");
        Check.Equal(5,plan.Mobiles.Select(account=>account.Id).Distinct().Count(),"Opaque identity IDs were not collection/subject specific.");
        Check.Equal(3,plan.Links.Count,"Self, guardian and staff links were not canonical.");
        Check.True(plan.Mobiles.Single(account=>account.Role=="fan").FirstName=="Mirror","Narrow missing fan fields did not use validated mirror evidence.");
        Check.True(Plan(manifest).Mappings.Select(mapping=>mapping.TargetId).SequenceEqual(plan.Mappings.Select(mapping=>mapping.TargetId)),"Account IDs changed on deterministic replay.");
        var fansOnly=manifest.DeepClone().AsObject();fansOnly["mobile_users"]=new JsonArray();
        Check.Equal(0,Plan(fansOnly).Mobiles.Count,"Pre-OTP fan evidence fabricated a mobile subject.");
        var duplicate=manifest.DeepClone().AsObject();var duplicateRow=duplicate["mobile_users"]![0]!.DeepClone();duplicateRow["id"]="competing-subject";duplicateRow["name"]=9;duplicate["mobile_users"]!.AsArray().Add(duplicateRow);
        var conflict=Plan(duplicate);
        Check.Equal(2,conflict.QuarantinedRows,"Every competing same-phone/role row must be quarantined, including a malformed profile.");
        Check.True(!conflict.Mobiles.Any(account=>account.Role=="fan"),"A duplicate phone-role chose a first winner.");
        var aliases=manifest.DeepClone().AsObject();aliases["mobile_users"]![4]!["linked_id"]="conflicting-admin";
        Check.True(Plan(aliases).Issues.Any(issue=>issue.Code=="conflicting_link_aliases"),"Contradictory management pointers fell through to phone matching.");
        var empty=manifest.DeepClone().AsObject();empty["admin_users"]![0]!["role"]="super_admin";empty["admin_users"]![0]!["organization_ids"]=new JsonArray();
        Check.Equal(0,Plan(empty).Admins.Single().Grants.Count,"Explicit empty grants enabled super-admin fallback.");
        empty["admin_users"]![0]!.AsObject().Remove("organization_ids");
        Check.True(Plan(empty).Admins.Single().Grants.All(grant=>grant.Provenance=="legacy_super_admin")&&Plan(empty).Admins.Single().Grants.Count==2,"Narrow legacy super-admin fallback was lost.");
        var legacy=manifest.DeepClone().AsObject();legacy["admin_users"]![0]!["role"]="admin";
        Check.True(Plan(legacy).Admins.Count==0&&!Plan(legacy).Mobiles.Any(account=>account.Role=="management"),"Historical admin role was elevated or linked despite quarantine.");
        var pending=manifest.DeepClone().AsObject();pending["admin_users"]![0]!["invite_pending"]=true;pending["admin_users"]![0]!["password_hash"]=null;
        Check.True(!Plan(pending).Admins.Single().IsActive&&Plan(pending).Admins.Single().InvitePending&&Plan(pending).Mobiles.Single(account=>account.Role=="management").IsActive,"Admin invitation status was flattened into the independent mobile account status.");
        var idAlias=manifest.DeepClone().AsObject();idAlias["mobile_users"]![0]!["id"]="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var second=idAlias["mobile_users"]![0]!.DeepClone();second["id"]="AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA";second["phone"]="+357 99 999998";idAlias["mobile_users"]!.AsArray().Add(second);
        Check.True(Plan(idAlias).Issues.Count(issue=>issue.Code=="target_id_collision")==2,"UUID aliases selected an arbitrary target subject.");
        Check.True(!plan.Admins.Single().ToString().Contains("$2b$",StringComparison.Ordinal)&&!plan.Manifest.Rows[0].ToString().Contains("357",StringComparison.Ordinal),"Protected record ToString leaked credentials or contacts.");
        return Task.CompletedTask;
    }
}
