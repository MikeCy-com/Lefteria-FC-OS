using LefteriaFc.Application.Roster;

namespace LefteriaFc.Application.CalendarImport;

public sealed record CalendarSourceRow(string Collection,int Ordinal,string Json)
{
    public override string ToString()=>"Protected calendar source row";
}

public sealed class ValidatedCalendarImport
{
    private readonly byte[] sourceBytes;
    public ValidatedCalendarImport(string digest,string sourceCommit,string rosterDigest,string timeZone,
        DateTimeOffset exportedAt,IReadOnlyList<CalendarSourceRow> rows,byte[] bytes)
    {
        Digest=digest;SourceCommit=sourceCommit;RosterDigest=rosterDigest;TimeZone=timeZone;
        ExportedAt=exportedAt;Rows=rows;sourceBytes=(byte[])bytes.Clone();
    }
    public string Digest{get;}
    public string SourceCommit{get;}
    public string RosterDigest{get;}
    public string TimeZone{get;}
    public DateTimeOffset ExportedAt{get;}
    public IReadOnlyList<CalendarSourceRow> Rows{get;}
    public byte[] CopySourceBytes()=>(byte[])sourceBytes.Clone();
    public override string ToString()=>"Validated protected calendar manifest";
}

public sealed record CalendarImportContext(string RosterDigest,string SourceCommit,ValidatedRosterImport Roster);
public sealed record CalendarImportIssue(string Collection,int Ordinal,string Code);
public sealed record CalendarProjectionInput(DateOnly Day,string? LocalTime,DateTimeOffset? StartsAt,
    string SourceDate,string Title,string? Location,string? LocationUrl,string? ArrivalTime,int? DurationMinutes,
    string? Summary=null,string? Journey=null,string? Preparation=null,
    DateOnly? EndDay=null,DateTimeOffset? EndsAt=null,string? SourceEndDate=null,string? Description=null)
{
    public override string ToString()=>"Validated calendar audience projection";
}
public sealed record CalendarScopeInput(string Kind,string RecordId)
{
    public override string ToString()=>"Validated calendar scope";
}
public sealed record CalendarItemInput(string Kind,string Id,string OrganizationId,int SourceOrdinal,
    string EventType,string? Status,bool IsCancelled,bool IsArchived,string? CancelReason,string? ContentStatus,
    CalendarProjectionInput Staff,CalendarProjectionInput? Member,
    IReadOnlyList<CalendarScopeInput> Scopes,IReadOnlyList<string> GuestPlayerIds)
{
    public override string ToString()=>"Validated calendar item";
}
public sealed record CalendarResponseInput(string Collection,int SourceOrdinal,string Kind,string EventId,
    string PlayerId,string Status,DateTimeOffset? UpdatedAt)
{
    public override string ToString()=>"Validated calendar response";
}
public sealed record CalendarImportPlan(ValidatedCalendarImport Manifest,IReadOnlyList<CalendarItemInput> Items,
    IReadOnlyList<CalendarResponseInput> Responses,IReadOnlyList<CalendarImportIssue> Issues)
{
    public int QuarantinedRows=>Issues.Select(row=>(row.Collection,row.Ordinal)).Distinct().Count();
    public override string ToString()=>"Protected calendar import plan";
}
