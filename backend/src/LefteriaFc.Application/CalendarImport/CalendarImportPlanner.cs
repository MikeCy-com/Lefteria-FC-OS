using System.Text.Json;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Application.CalendarImport;

public static partial class CalendarImportPlanner
{
    public static CalendarImportPlan Build(ValidatedCalendarImport manifest,CalendarImportContext context)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(context);
        if(manifest.RosterDigest!=context.RosterDigest||manifest.SourceCommit!=context.SourceCommit||
            context.Roster.SourceCommit!=context.SourceCommit)
            throw new RosterImportApplyException("calendar_base_snapshot_mismatch");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(manifest.TimeZone);
        var rows=manifest.Rows.Select(value=>new Row(value)).ToArray();
        var schedule=rows.Where(row=>row.Kind is not null).ToArray();
        foreach(var row in schedule)Attempt(row,()=>row.Id=Id(row.Json,"id",true));
        // Malformed competitors still take part in duplicate and ambiguity checks.
        foreach(var group in schedule.Where(row=>row.Id is not null).GroupBy(row=>(row.Kind,row.Id)).Where(group=>group.Count()>1))
            foreach(var row in group)row.Issues.Add("duplicate_id");
        var index=new Index(context.Roster,schedule);
        foreach(var row in schedule.Where(row=>row.Valid))Attempt(row,()=>row.Item=Item(row,index,zone));
        index.SetItems(schedule);
        var responses=rows.Where(row=>row.Kind is null).ToArray();
        foreach(var row in responses)
        {
            Attempt(row,()=>row.EventId=Id(row.Json,"event_id",true));
            Attempt(row,()=>row.PlayerId=Id(row.Json,"player_id",true));
            Attempt(row,()=>row.ResponseKind=ResponseKind(row.Json,row.EventId,index));
        }
        foreach(var group in responses.Where(row=>row.EventId is not null&&row.PlayerId is not null&&row.ResponseKind is not null)
            .GroupBy(row=>(row.Source.Collection,row.ResponseKind,row.EventId,row.PlayerId)).Where(group=>group.Count()>1))
            foreach(var row in group)row.Issues.Add("duplicate_response");
        foreach(var row in responses.Where(row=>row.Valid))Attempt(row,()=>row.Response=Response(row,index));
        return new(manifest,
            Array.AsReadOnly(schedule.Where(row=>row.Valid&&row.Item is not null).Select(row=>row.Item!).ToArray()),
            Array.AsReadOnly(responses.Where(row=>row.Valid&&row.Response is not null).Select(row=>row.Response!).ToArray()),
            Array.AsReadOnly(rows.SelectMany(row=>row.Issues.Order(StringComparer.Ordinal).Select(code=>new CalendarImportIssue(row.Source.Collection,row.Source.Ordinal,code))).ToArray()));
    }

    private static CalendarItemInput Item(Row row,Index index,TimeZoneInfo zone)
    {
        var json=row.Json;var kind=row.Kind!;
        var groups=Ids(json,"academy_group_ids",128);groups.UnionWith(Ids(json,"group_ids",128));
        var academy=Id(json,"academy_group_id");var alias=Id(json,"group_id");
        if(academy is not null&&alias is not null&&academy!=alias)throw Problem("conflicting_group_aliases");
        if(academy is not null)groups.Add(academy);if(alias is not null)groups.Add(alias);
        var teams=Ids(json,"team_ids",128);var team=Id(json,"team_id");if(team is not null)teams.Add(team);
        // Classification must inspect complete source strings. Display caps must
        // never hide an organization marker later in a bounded manifest value.
        var teamType=Text(json,"team_type",int.MaxValue);var competition=Text(json,"competition",int.MaxValue);
        var legacyGroup=kind=="event"&&team is not null&&index.Groups.ContainsKey(team);
        var inferred=groups.Count>0||AcademyMarker(teamType)||kind=="match"&&AcademyMarker(competition)||legacyGroup?OrganizationIds.Academy:OrganizationIds.Club;
        var organization=Organization(json,inferred);
        // Historical event writers stored the group in team_id. Only this exact
        // group alias is safe to normalize; a different team pointer remains an FK.
        if(legacyGroup&&(groups.Count==0||groups.Contains(team!)))
        {
            groups.Add(team!);teams.Remove(team!);
        }
        foreach(var id in groups)if(!index.Groups.TryGetValue(id,out var target)||target.OrganizationId!=organization)throw Problem("group_reference");
        foreach(var id in teams)if(!index.Teams.TryGetValue(id,out var target)||target.OrganizationId!=organization)throw Problem("team_reference");
        var guests=Ids(json,"guest_player_ids",500);
        foreach(var id in guests)if(!index.Players.TryGetValue(id,out var target)||target.OrganizationId!=organization)throw Problem("guest_player_reference");
        var status=Status(json,kind);var cancelled=Flag(json,"cancelled")|Flag(json,"is_cancelled")|status=="cancelled";
        var archived=Flag(json,"archived")|Flag(json,"is_archived");
        var cancelReason=Text(json,"cancel_reason",1200);
        string? contentStatus=null;
        CalendarProjectionInput staff;CalendarProjectionInput? member;
        if(kind=="training")
        {
            contentStatus=Text(json,"content_status",32);
            if(contentStatus is not (null or "draft" or "review_pending" or "rejected" or "published" or "legacy"))throw Problem("content_status_unknown");
            staff=Projection(json,kind,zone);
            if(json.TryGetProperty("published_snapshot",out var snapshot)&&snapshot.ValueKind==JsonValueKind.Object)
                member=Projection(snapshot,kind,zone,true);
            else
            {
                if(snapshot.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))throw Problem("published_snapshot_type");
                member=contentStatus is "draft" or "review_pending" or "rejected"?null:Projection(json,kind,zone);
            }
        }
        else{staff=Projection(json,kind,zone);member=staff;}
        var eventType=kind=="event"?Text(json,"type",80)??"training":kind;
        return new(kind,row.Id!,organization,row.Source.Ordinal,eventType,status,cancelled,archived,cancelReason,contentStatus,staff,member,
            Array.AsReadOnly(groups.Order(StringComparer.Ordinal).Select(id=>new CalendarScopeInput("group",id))
                .Concat(teams.Order(StringComparer.Ordinal).Select(id=>new CalendarScopeInput("team",id))).ToArray()),
            Array.AsReadOnly(guests.Order(StringComparer.Ordinal).ToArray()));
    }

    private static CalendarResponseInput Response(Row row,Index index)
    {
        var json=row.Json;
        if(!index.Items.TryGetValue((row.ResponseKind!,row.EventId!),out var item))throw Problem("event_reference");
        if(!index.Players.TryGetValue(row.PlayerId!,out var player)||player.OrganizationId!=item.OrganizationId)throw Problem("player_reference");
        if(Organization(json,item.OrganizationId)!=item.OrganizationId)throw Problem("response_organization");
        var status=Text(json,"status",32,true)!;
        var allowed=row.Source.Collection switch
        {
            "availability"=>status is "going" or "not_going" or "maybe",
            "event_attendance"=>status is "going" or "not_going" or "maybe" or "no_response",
            "attendance"=>status is "present" or "absent",
            _=>false
        };
        if(!allowed)throw Problem("response_status_unknown");
        _=Flag(json,"archived");_=Flag(json,"is_archived");
        var updated=Text(json,"updated_at",80)??Text(json,"marked_at",80);
        return new(row.Source.Collection,row.Source.Ordinal,row.ResponseKind!,row.EventId!,row.PlayerId!,status,updated is null?null:UtcInstant(updated));
    }

    private static string ResponseKind(JsonElement json,string? id,Index index)
    {
        var explicitKind=Text(json,"event_type",32);
        if(json.TryGetProperty("event_type",out var value)&&value.ValueKind==JsonValueKind.String&&explicitKind is null)throw Problem("event_type_unknown");
        if(explicitKind is not null)return explicitKind switch{"fixture" or "match"=>"match","event"=>"event","training"=>"training",_=>throw Problem("event_type_unknown")};
        if(id is null)throw Problem("event_reference");
        if(!index.SourceKinds.TryGetValue(id,out var kinds))throw Problem("event_reference");
        if(kinds.Count!=1)throw Problem("event_type_ambiguous");
        return kinds.Single();
    }

    private sealed class Index(ValidatedRosterImport roster,IEnumerable<Row> source)
    {
        public Dictionary<string,RosterGroupInput> Groups{get;}=roster.Groups.ToDictionary(row=>row.Id,StringComparer.Ordinal);
        public Dictionary<string,RosterTeamInput> Teams{get;}=roster.Teams.ToDictionary(row=>row.Id,StringComparer.Ordinal);
        public Dictionary<string,RosterPlayerInput> Players{get;}=roster.Players.ToDictionary(row=>row.Id,StringComparer.Ordinal);
        public Dictionary<string,HashSet<string>> SourceKinds{get;}=source.Where(row=>row.Id is not null).GroupBy(row=>row.Id!,StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.Select(row=>row.Kind!).ToHashSet(StringComparer.Ordinal),StringComparer.Ordinal);
        public Dictionary<(string Kind,string Id),CalendarItemInput> Items{get;private set;}=[];
        public void SetItems(IEnumerable<Row> rows)=>Items=rows.Where(row=>row.Valid&&row.Item is not null).ToDictionary(row=>(row.Kind!,row.Id!),row=>row.Item!);
    }
    private sealed class Row
    {
        public Row(CalendarSourceRow source)
        {
            Source=source;using var document=JsonDocument.Parse(source.Json);Json=document.RootElement.Clone();
            Kind=source.Collection switch{"fixtures"=>"match","events"=>"event","training_sessions"=>"training",_=>null};
        }
        public CalendarSourceRow Source{get;}
        public JsonElement Json{get;}
        public string? Kind{get;}
        public string? Id{get;set;}
        public string? EventId{get;set;}
        public string? PlayerId{get;set;}
        public string? ResponseKind{get;set;}
        public CalendarItemInput? Item{get;set;}
        public CalendarResponseInput? Response{get;set;}
        public HashSet<string> Issues{get;}=new(StringComparer.Ordinal);
        public bool Valid=>Issues.Count==0;
        public override string ToString()=>"Protected calendar planning row";
    }
    private sealed class RowProblem(string code):Exception("Calendar source row requires review."){public string Code{get;}=code;}
    private static RowProblem Problem(string code)=>new(code);
    private static void Attempt(Row row,Action action){try{action();}catch(RowProblem problem){row.Issues.Add(problem.Code);}}
}
