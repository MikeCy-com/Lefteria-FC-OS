namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportPlanner
{
    private static void ResolveStaffAdminLinks(IReadOnlyList<Row> rows,AccountImportTrustedContext context)
    {
        var staff=context.Staff.Staff.ToDictionary(person=>person.Id,StringComparer.Ordinal);
        var pointers=new Dictionary<string,string?>(StringComparer.Ordinal);
        var invalid=new HashSet<string>(StringComparer.Ordinal);
        foreach(var source in context.Staff.Source)
        {
            try{pointers.Add(source.SourceId,IdValue(ReadJson(source.Json),"admin_user_id"));}
            catch(RowProblem){invalid.Add(source.SourceId);}
        }
        var reversePointers=pointers.Where(pair=>pair.Value is not null).ToLookup(pair=>pair.Value!,pair=>pair.Key,StringComparer.Ordinal);
        var intents=new List<(Row Admin,string Staff)>();
        foreach(var row in rows.Where(row=>row.Source.Collection=="admin_users"&&row.LegacyId is not null))
        {
            Attempt(row,()=>
            {
                if(IdValue(row.Json,"staff_id") is { } direct)
                {
                    if(!staff.ContainsKey(direct)||invalid.Contains(direct))throw Problem("admin_staff_reference");
                    intents.Add((row,direct));
                    if(pointers.GetValueOrDefault(direct) is { } reverse&&reverse!=row.LegacyId)row.Issues.Add("staff_admin_link_conflict");
                }
                foreach(var reverse in reversePointers[row.LegacyId!])intents.Add((row,reverse));
            });
        }
        foreach(var group in intents.GroupBy(pair=>pair.Staff,StringComparer.Ordinal).Where(group=>group.Select(pair=>pair.Admin.LegacyId).Distinct(StringComparer.Ordinal).Count()>1))
            foreach(var pair in group)pair.Admin.Issues.Add("staff_admin_link_conflict");
        var assignments=intents.ToLookup(pair=>pair.Admin,pair=>pair.Staff);
        foreach(var row in rows.Where(row=>row.Valid&&row.Admin is not null))
        {
            var assigned=assignments[row].Distinct(StringComparer.Ordinal).ToArray();
            if(assigned.Length>1){row.Issues.Add("staff_admin_link_conflict");continue;}
            if(assigned.SingleOrDefault() is { } id)
            {
                var person=staff[id];
                if(row.Admin!.IsActive&&(!person.IsActive||person.IsArchived)){row.Issues.Add("staff_admin_status_conflict");continue;}
                row.Admin=row.Admin with{StaffId=id};
            }
        }
    }
    private static List<PlannedContactBinding> ContactBindings(AccountImportTrustedContext context)
    {
        var result=new List<PlannedContactBinding>();
        foreach(var source in context.Roster.Source.Where(source=>source.Collection=="players"))
        {
            var row=ReadJson(source.Json);
            Add("players",source.SourceId,"player_own",row,"phone");
            Add("players",source.SourceId,"guardian",row,"parent_phone");
        }
        foreach(var source in context.Staff.Source)Add("staff",source.SourceId,"staff_own",ReadJson(source.Json),"phone");
        return result;
        void Add(string collection,string id,string kind,System.Text.Json.JsonElement row,string field)
        {
            try{if(Phone(row,field,collection) is { } phone)result.Add(new(collection,id,kind,phone));}
            catch(RowProblem){/* An invalid raw contact cannot create a canonical binding. */}
        }
    }
}
