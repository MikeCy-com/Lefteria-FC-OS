using System.Text.Json;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

internal static class CalendarImportReconciliation
{
    public static async Task VerifyAsync(LefteriaDbContext database,CalendarImportPlan plan,CalendarImportRows expected,
        CalendarImportStateRecord state,string environment,CancellationToken token)
    {
        if(state.Digest!=plan.Manifest.Digest||state.RosterDigest!=plan.Manifest.RosterDigest||state.SourceCommit!=plan.Manifest.SourceCommit||
            state.TimeZone!=plan.Manifest.TimeZone||state.IsComplete!=(plan.QuarantinedRows==0)||state.Environment!=environment||state.ExportedAt!=plan.Manifest.ExportedAt||
            !state.ManifestBytes.AsSpan().SequenceEqual(plan.Manifest.CopySourceBytes())||Normalize(state.CountsJson)!=Normalize(expected.CountsJson))throw Refused();
        var items=await database.CalendarItems.AsNoTracking().ToArrayAsync(token);
        if(!Same(items.Select(row=>(row.Kind,row.Id,row.OrganizationId,row.SourceCollection,row.SourceOrdinal,row.EventType,row.Status,
                row.IsCancelled,row.IsArchived,row.CancelReason,row.ContentStatus,row.StaffDay,row.StaffEndDay,row.MemberDay,row.MemberEndDay,
                Normalize(row.StaffProjectionJson),Normalize(row.MemberProjectionJson))),
            expected.Items.Select(row=>(row.Kind,row.Id,row.OrganizationId,row.SourceCollection,row.SourceOrdinal,row.EventType,row.Status,
                row.IsCancelled,row.IsArchived,row.CancelReason,row.ContentStatus,row.StaffDay,row.StaffEndDay,row.MemberDay,row.MemberEndDay,
                Normalize(row.StaffProjectionJson),Normalize(row.MemberProjectionJson)))))throw Refused();
        var scopes=await database.CalendarItemScopes.AsNoTracking().ToArrayAsync(token);
        var guests=await database.CalendarItemGuests.AsNoTracking().ToArrayAsync(token);
        var responses=await database.CalendarResponses.AsNoTracking().ToArrayAsync(token);
        if(!Same(scopes.Select(row=>(row.Kind,row.EventId,row.OrganizationId,row.ScopeKind,row.RecordId,row.TeamId,row.AcademyGroupId)),
                expected.Scopes.Select(row=>(row.Kind,row.EventId,row.OrganizationId,row.ScopeKind,row.RecordId,row.TeamId,row.AcademyGroupId)))||
            !Same(guests.Select(row=>(row.Kind,row.EventId,row.OrganizationId,row.PlayerId)),expected.Guests.Select(row=>(row.Kind,row.EventId,row.OrganizationId,row.PlayerId)))||
            !Same(responses.Select(row=>(row.Collection,row.Kind,row.EventId,row.PlayerId,row.OrganizationId,row.Status,row.UpdatedAt,row.ActorIdentityId,row.SourceOrdinal)),
                expected.Responses.Select(row=>(row.Collection,row.Kind,row.EventId,row.PlayerId,row.OrganizationId,row.Status,row.UpdatedAt,row.ActorIdentityId,row.SourceOrdinal))))throw Refused();
        var sources=await database.CalendarSourceDocuments.AsNoTracking().ToArrayAsync(token);
        var issues=await database.CalendarImportIssues.AsNoTracking().ToArrayAsync(token);
        if(!Same(sources.Select(row=>(row.Collection,row.Ordinal,row.ImportDigest,row.Disposition,row.TargetKind,row.TargetId,row.SourceJson)),
                expected.Sources.Select(row=>(row.Collection,row.Ordinal,row.ImportDigest,row.Disposition,row.TargetKind,row.TargetId,row.SourceJson)))||
            !Same(issues.Select(row=>(row.Collection,row.Ordinal,row.Code)),expected.Issues.Select(row=>(row.Collection,row.Ordinal,row.Code)))||
            await database.SecurityAuditEvents.CountAsync(row=>row.Action=="calendar.import"&&row.Outcome=="success"&&row.SubjectDigest==state.Digest&&row.OccurredAt==state.AppliedAt,token)!=1)throw Refused();
    }
    private static bool Same<T>(IEnumerable<T> actual,IEnumerable<T> expected)
    {var left=actual.ToArray();var right=expected.ToArray();return left.Length==right.Length&&left.ToHashSet().SetEquals(right);}
    private static string? Normalize(string? json)
    {
        if(json is null)return null;
        try
        {
            using var document=JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement.EnumerateObject().OrderBy(row=>row.Name,StringComparer.Ordinal)
                .ToDictionary(row=>row.Name,row=>row.Value,StringComparer.Ordinal));
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException){return "\0invalid";}
    }
    private static RosterImportApplyException Refused()=>new("calendar_target_reconciliation_failed");
}
