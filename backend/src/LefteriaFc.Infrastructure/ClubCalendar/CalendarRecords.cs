namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed class CalendarItemRecord
{
    public string Kind{get;set;}="";
    public string Id{get;set;}="";
    public string OrganizationId{get;set;}="";
    public string SourceCollection{get;set;}="";
    public int SourceOrdinal{get;set;}
    public string EventType{get;set;}="";
    public string? Status{get;set;}
    public bool IsCancelled{get;set;}
    public bool IsArchived{get;set;}
    public string? CancelReason{get;set;}
    public string? ContentStatus{get;set;}
    public DateOnly StaffDay{get;set;}
    public DateOnly StaffEndDay{get;set;}
    public DateOnly? MemberDay{get;set;}
    public DateOnly? MemberEndDay{get;set;}
    public string StaffProjectionJson{get;set;}="{}";
    public string? MemberProjectionJson{get;set;}
}

public sealed class CalendarItemScopeRecord
{
    public string Kind{get;set;}="";
    public string EventId{get;set;}="";
    public string OrganizationId{get;set;}="";
    public string ScopeKind{get;set;}="";
    public string RecordId{get;set;}="";
    public string? TeamId{get;set;}
    public string? AcademyGroupId{get;set;}
}

public sealed class CalendarItemGuestRecord
{
    public string Kind{get;set;}="";
    public string EventId{get;set;}="";
    public string OrganizationId{get;set;}="";
    public string PlayerId{get;set;}="";
}

public sealed class CalendarResponseRecord
{
    public string Collection{get;set;}="";
    public string Kind{get;set;}="";
    public string EventId{get;set;}="";
    public string PlayerId{get;set;}="";
    public string OrganizationId{get;set;}="";
    public string Status{get;set;}="";
    public DateTimeOffset? UpdatedAt{get;set;}
    public Guid? ActorIdentityId{get;set;}
    public int? SourceOrdinal{get;set;}
}

public sealed class CalendarImportStateRecord
{
    public string Id{get;set;}="initial";
    public string Digest{get;set;}="";
    public string RosterDigest{get;set;}="";
    public string SourceCommit{get;set;}="";
    public string Environment{get;set;}="";
    public string TimeZone{get;set;}="";
    public bool IsComplete{get;set;}
    public DateTimeOffset ExportedAt{get;set;}
    public DateTimeOffset AppliedAt{get;set;}
    public string CountsJson{get;set;}="{}";
    public byte[] ManifestBytes{get;set;}=[];
}

public sealed class CalendarSourceDocumentRecord
{
    public string Collection{get;set;}="";
    public int Ordinal{get;set;}
    public string ImportDigest{get;set;}="";
    public string Disposition{get;set;}="";
    public string? TargetKind{get;set;}
    public string? TargetId{get;set;}
    public string SourceJson{get;set;}="{}";
}

public sealed class CalendarImportIssueRecord
{
    public string Collection{get;set;}="";
    public int Ordinal{get;set;}
    public string Code{get;set;}="";
}
