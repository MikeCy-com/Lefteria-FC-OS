using System.Text.Json;
using LefteriaFc.Application.CalendarImport;

namespace LefteriaFc.Infrastructure.ClubCalendar;

internal sealed class CalendarImportRows
{
    internal static readonly JsonSerializerOptions JsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    public required CalendarItemRecord[] Items{get;init;}
    public required CalendarItemScopeRecord[] Scopes{get;init;}
    public required CalendarItemGuestRecord[] Guests{get;init;}
    public required CalendarResponseRecord[] Responses{get;init;}
    public required CalendarSourceDocumentRecord[] Sources{get;init;}
    public required CalendarImportIssueRecord[] Issues{get;init;}
    public required string CountsJson{get;init;}

    public static CalendarImportRows From(CalendarImportPlan plan)
    {
        var items=plan.Items.ToDictionary(row=>(Collection(row.Kind),row.SourceOrdinal));
        var byId=plan.Items.ToDictionary(row=>(row.Kind,row.Id));
        var responses=plan.Responses.ToDictionary(row=>(row.Collection,row.SourceOrdinal));
        return new()
        {
            Items=plan.Items.Select(row=>new CalendarItemRecord
            {
                Kind=row.Kind,Id=row.Id,OrganizationId=row.OrganizationId,SourceCollection=Collection(row.Kind),SourceOrdinal=row.SourceOrdinal,
                EventType=row.EventType,Status=row.Status,IsCancelled=row.IsCancelled,IsArchived=row.IsArchived,
                CancelReason=row.CancelReason,ContentStatus=row.ContentStatus,
                StaffDay=row.Staff.Day,StaffEndDay=row.Staff.EndDay??row.Staff.Day,
                MemberDay=row.Member?.Day,MemberEndDay=row.Member is null?null:row.Member.EndDay??row.Member.Day,
                StaffProjectionJson=JsonSerializer.Serialize(row.Staff,JsonOptions),
                MemberProjectionJson=row.Member is null?null:JsonSerializer.Serialize(row.Member,JsonOptions)
            }).ToArray(),
            Scopes=plan.Items.SelectMany(item=>item.Scopes.Select(scope=>new CalendarItemScopeRecord
            {
                Kind=item.Kind,EventId=item.Id,OrganizationId=item.OrganizationId,ScopeKind=scope.Kind,RecordId=scope.RecordId,
                TeamId=scope.Kind=="team"?scope.RecordId:null,AcademyGroupId=scope.Kind=="group"?scope.RecordId:null
            })).ToArray(),
            Guests=plan.Items.SelectMany(item=>item.GuestPlayerIds.Select(player=>new CalendarItemGuestRecord
            {Kind=item.Kind,EventId=item.Id,OrganizationId=item.OrganizationId,PlayerId=player})).ToArray(),
            Responses=plan.Responses.Select(row=>new CalendarResponseRecord
            {
                Collection=row.Collection,Kind=row.Kind,EventId=row.EventId,PlayerId=row.PlayerId,
                OrganizationId=byId[(row.Kind,row.EventId)].OrganizationId,Status=row.Status,UpdatedAt=row.UpdatedAt,SourceOrdinal=row.SourceOrdinal
            }).ToArray(),
            Sources=plan.Manifest.Rows.Select(row=>
            {
                items.TryGetValue((row.Collection,row.Ordinal),out var item);
                responses.TryGetValue((row.Collection,row.Ordinal),out var response);
                return new CalendarSourceDocumentRecord
                {
                    Collection=row.Collection,Ordinal=row.Ordinal,ImportDigest=plan.Manifest.Digest,SourceJson=row.Json,
                    Disposition=item is not null?"item":response is not null?"response":"quarantined",
                    TargetKind=item?.Kind??response?.Kind,TargetId=item?.Id??response?.EventId
                };
            }).ToArray(),
            Issues=plan.Issues.Select(row=>new CalendarImportIssueRecord{Collection=row.Collection,Ordinal=row.Ordinal,Code=row.Code}).ToArray(),
            CountsJson=JsonSerializer.Serialize(new{items=plan.Items.Count,scopes=plan.Items.Sum(row=>row.Scopes.Count),
                guests=plan.Items.Sum(row=>row.GuestPlayerIds.Count),responses=plan.Responses.Count,sources=plan.Manifest.Rows.Count,
                issues=plan.Issues.Count,quarantined_rows=plan.QuarantinedRows})
        };
    }
    private static string Collection(string kind)=>kind switch{"match"=>"fixtures","event"=>"events","training"=>"training_sessions",_=>throw new InvalidOperationException("Invalid validated calendar kind.")};
}
