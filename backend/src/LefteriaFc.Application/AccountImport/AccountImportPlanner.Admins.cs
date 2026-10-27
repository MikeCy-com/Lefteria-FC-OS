using System.Text.Json;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private static PlannedAdminAccount ParseAdmin(Row row,PlanningIndex index)
    {
        var source=row.Json;var role=Value(source,"role",24,true)!;
        if(!AdminRoles.Contains(role,StringComparer.Ordinal))throw Problem("admin_role_unknown");
        var pending=Flag(source,"invite_pending",false);var active=Flag(source,"is_active",true,true)&&!pending;
        if(pending&&source.TryGetProperty("is_active",out var explicitActive)&&explicitActive.ValueKind==JsonValueKind.True)throw Problem("pending_active_admin_conflict");
        var hash=Value(source,"password_hash",128);
        if(hash is not null&&!Bcrypt().IsMatch(hash))throw Problem("password_hash_invalid");
        if(active&&hash is null)throw Problem("active_admin_credential_missing");
        // A deliberately disabled historical actor may have no credential.
        // Retain that inactive record; absence/default active never qualifies.
        if(!pending&&hash is null&&(!source.TryGetProperty("is_active",out var inactive)||inactive.ValueKind!=JsonValueKind.False))throw Problem("admin_credential_missing");
        var twoFactor=Flag(source,"otp_2fa_enabled",false);
        if(twoFactor&&row.CanonicalPhone is null)throw Problem("two_factor_phone_required");
        var grants=AdminOrganizations(source,role,active,index);
        var selected=IdValue(source,"active_organization_id");
        if(!grants.Any(grant=>grant.OrganizationId==selected))selected=grants.Count==1?grants[0].OrganizationId:null;
        return new(row.Id,row.LegacyId!,row.Username!,Value(source,"name",200)??"",role,hash,row.CanonicalPhone,active,pending,twoFactor,
            IdValue(source,"staff_id"),grants,selected);
    }
    private static IReadOnlyList<PlannedAdminGrant> AdminOrganizations(JsonElement source,string role,bool active,PlanningIndex index)
    {
        var assignedGroup=IdValue(source,"assigned_academy_group_id");
        var assignedTeam=IdValue(source,"assigned_team_id");
        if(assignedGroup is not null&&!index.GroupIds.Contains(assignedGroup))throw Problem("admin_group_reference");
        if(assignedTeam is not null&&!index.TeamIds.Contains(assignedTeam))throw Problem("admin_team_reference");
        var ids=new List<string>();var origin="explicit_plural";
        if(source.TryGetProperty("organization_ids",out var plural)&&plural.ValueKind!=JsonValueKind.Null)
        {
            if(plural.ValueKind==JsonValueKind.String){if(plural.GetString() is {Length:>0} id)ids.Add(id);}
            else if(plural.ValueKind==JsonValueKind.Array)
            {foreach(var value in plural.EnumerateArray()){if(value.ValueKind!=JsonValueKind.String)throw Problem("organization_type");ids.Add(value.GetString()!);}}
            else throw Problem("organization_type");
        }
        else if(source.TryGetProperty("organization_id",out var singular)&&singular.ValueKind!=JsonValueKind.Null)
        {
            origin="explicit_singular";
            if(singular.ValueKind!=JsonValueKind.String)throw Problem("organization_type");
            if(singular.GetString() is {Length:>0} id)ids.Add(id);
        }
        else if(role=="super_admin"&&active){origin="legacy_super_admin";ids.Add(OrganizationIds.Club);ids.Add(OrganizationIds.Academy);}
        else if(role=="coach")
        {
            if(assignedGroup is { } group)
            {if(!index.GroupIds.Contains(group))throw Problem("admin_group_reference");ids.Add(OrganizationIds.Academy);origin="coach_group_backfill";}
            else if(assignedTeam is { } team)
            {if(!index.TeamIds.Contains(team))throw Problem("admin_team_reference");ids.Add(OrganizationIds.Club);origin="coach_team_backfill";}
        }
        if(ids.Any(id=>!OrganizationIds.IsKnown(id))||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Count)throw Problem("organization_invalid");
        return Array.AsReadOnly(ids.Order(StringComparer.Ordinal).Select(id=>new PlannedAdminGrant(id,origin)).ToArray());
    }
}
