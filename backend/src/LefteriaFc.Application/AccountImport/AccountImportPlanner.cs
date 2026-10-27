using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private static readonly string[] AdminRoles=["super_admin","manager","editor","marketing","coach","matchday_editor"];
    private static readonly string[] CoachTitles=["Head Coach","Assistant Coach","Goalkeeper Coach","Fitness Coach","Youth Coach"];
    public static AccountImportPlan Build(ValidatedAccountImport manifest,AccountImportTrustedContext context)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(context);
        if(manifest.RosterDigest!=context.RosterDigest||manifest.StaffDigest!=context.StaffDigest||
            manifest.SourceCommit!=context.SourceCommit||context.Roster.SourceCommit!=context.SourceCommit||context.Staff.SourceCommit!=context.SourceCommit||
            context.Staff.RosterDigest!=context.RosterDigest)
            throw new RosterImportApplyException("account_base_snapshot_mismatch");
        var rows=manifest.Rows.Select(source=>new Row(source)).ToArray();
        foreach(var row in rows)
        {
            Attempt(row,()=>
            {
                row.LegacyId=IdValue(row.Json,"id")??throw Problem("legacy_id_required");
                if(Guid.TryParseExact(row.LegacyId,"D",out var id)&&id==Guid.Empty)throw Problem("legacy_id_invalid");
                row.Id=TargetId(row.Source.Collection,row.LegacyId);
            });
            Attempt(row,()=>row.CanonicalPhone=Phone(row.Json,"phone",row.Source.Collection,row.Source.Collection!="admin_users"));
            if(row.Source.Collection=="mobile_users")Attempt(row,()=>
            {
                row.Role=Value(row.Json,"role",16,true);
                if(!MobileRoles.TryParse(row.Role,out _))throw Problem("mobile_role_unknown");
            });
            if(row.Source.Collection=="admin_users")Attempt(row,()=>row.Username=Value(row.Json,"username",80,true));
            if(row.Source.Collection=="mobile_users")Attempt(row,()=>ValidateLinkShapes(row));
        }
        Conflict(rows.Where(row=>row.LegacyId is not null),row=>(row.Source.Collection,row.LegacyId),"duplicate_legacy_id");
        Conflict(rows.Where(row=>row.Id!=Guid.Empty),row=>(row.Source.Collection,row.Id),"target_id_collision");
        Conflict(rows.Where(row=>row.Source.Collection=="mobile_users"&&row.CanonicalPhone is not null&&row.Role is not null),row=>(row.CanonicalPhone,row.Role),"duplicate_phone_role");
        Conflict(rows.Where(row=>row.Source.Collection=="fans"&&row.CanonicalPhone is not null),row=>row.CanonicalPhone,"duplicate_fan_phone");
        Conflict(rows.Where(row=>row.Source.Collection=="admin_users"&&row.Username is not null),row=>row.Username,"duplicate_admin_username");
        var bindings=ContactBindings(context);
        var index=new PlanningIndex(context,bindings);
        foreach(var row in rows.Where(row=>row.Valid&&row.Source.Collection=="fans"))Attempt(row,()=>row.Fan=ParseFan(row,manifest.ExportedAt));
        foreach(var row in rows.Where(row=>row.Valid&&row.Source.Collection=="admin_users"))Attempt(row,()=>row.Admin=ParseAdmin(row,index));
        ResolveStaffAdminLinks(rows,context);
        index.SetAccounts(rows);
        foreach(var row in rows.Where(row=>row.Valid&&row.Source.Collection=="mobile_users"))Attempt(row,()=>row.Mobile=ParseMobile(row,manifest.ExportedAt,index));
        var links=new List<PlannedAccountLink>();
        foreach(var row in rows.Where(row=>row.Valid&&row.Mobile is not null))Attempt(row,()=>links.AddRange(ResolveMobileLinks(row,index)));
        var validIds=rows.Where(row=>row.Valid&&row.Mobile is not null).Select(row=>row.Id).ToHashSet();
        return new()
        {
            Manifest=manifest,
            Mobiles=Array.AsReadOnly(rows.Where(row=>row.Valid&&row.Mobile is not null).Select(row=>row.Mobile!).ToArray()),
            Admins=Array.AsReadOnly(rows.Where(row=>row.Valid&&row.Admin is not null).Select(row=>row.Admin!).ToArray()),
            FanEvidence=Array.AsReadOnly(rows.Where(row=>row.Valid&&row.Fan is not null).Select(row=>row.Fan!).ToArray()),
            Mappings=Array.AsReadOnly(rows.Where(row=>row.Valid).Select(row=>new PlannedAccountMapping(row.Source.Collection,row.Source.Ordinal,row.LegacyId!,row.Id,
                row.Source.Collection=="mobile_users"?"mobile":row.Source.Collection=="admin_users"?"admin":"fan_evidence")).ToArray()),
            ContactBindings=Array.AsReadOnly(bindings.ToArray()),
            Links=Array.AsReadOnly(links.Where(link=>validIds.Contains(link.IdentityId)).ToArray()),
            Issues=Array.AsReadOnly(rows.SelectMany(row=>row.Issues.Order(StringComparer.Ordinal).Select(code=>new AccountImportIssue(row.Source.Collection,row.Source.Ordinal,code))).ToArray())
        };
    }
    private static void Attempt(Row row,Action action){try{action();}catch(RowProblem issue){row.Issues.Add(issue.Code);}}
    private static void ValidateLinkShapes(Row row)
    {
        foreach(var field in new[]{"linked_player_id","linked_staff_id","linked_admin_id","admin_user_id","linked_id","team_id","academy_group_id"})
            _=IdValue(row.Json,field);
        foreach(var field in new[]{"linked_player_ids","academy_group_ids","team_ids"}) _=Ids(row.Json,field);
    }
    private static void Conflict<TKey>(IEnumerable<Row> rows,Func<Row,TKey> key,string code)
    {foreach(var group in rows.GroupBy(key).Where(group=>group.Count()>1))foreach(var row in group)row.Issues.Add(code);}
}
