using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Infrastructure.StaffRelationships;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

public sealed record CalendarImportOutcome(bool Applied,bool Noop,bool Reviewed,string Digest,
    string CountsJson,IReadOnlyDictionary<string,int> IssueCounts)
{
    public override string ToString()=>"Protected calendar import outcome";
}

// Explicit initial owner tool only; never registered in the API container.
public static class PostgresCalendarImport
{
    public static async Task<CalendarImportOutcome> ApplyAsync(ValidatedCalendarImport manifest,string connectionString,
        string expectedDigest,string expectedDatabase,string environment,bool reviewOnly=false,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await using var database=await OwnerImportTarget.OpenAsync(connectionString,manifest.Digest,expectedDigest,expectedDatabase,environment,token);
        await using var transaction=await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlRawAsync("""
            SET LOCAL lock_timeout='10s';
            SELECT pg_advisory_xact_lock(5495884701258);
            LOCK TABLE club.calendar_import_state,club.calendar_items,club.calendar_item_scopes,
              club.calendar_item_guests,club.calendar_responses,migration.calendar_source_documents,
              migration.calendar_import_issues IN SHARE ROW EXCLUSIVE MODE;
            LOCK TABLE club.roster_import_state,club.teams,club.academy_groups,club.players,
              club.player_academy_groups,migration.roster_source_documents IN SHARE MODE;
            """,token);
        var previous=await database.CalendarImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if(previous is not null&&previous.Digest!=manifest.Digest)throw Refused("calendar_initial_snapshot_already_applied");
        if(previous is null&&(await database.CalendarItems.AnyAsync(token)||await database.CalendarItemScopes.AnyAsync(token)||
            await database.CalendarItemGuests.AnyAsync(token)||await database.CalendarResponses.AnyAsync(token)||
            await database.CalendarSourceDocuments.AnyAsync(token)||await database.CalendarImportIssues.AnyAsync(token)))
            throw Refused("calendar_uninitialized_target_not_empty");
        var roster=await BoundRosterArchive.ReadAsync(database,manifest.RosterDigest,manifest.SourceCommit,token);
        var rosterState=await database.RosterImportStates.AsNoTracking().SingleAsync(token);
        if(rosterState.Environment!=environment)throw Refused("calendar_base_environment_mismatch");
        var plan=CalendarImportPlanner.Build(manifest,new(rosterState.Digest,rosterState.SourceCommit,roster));
        var rows=CalendarImportRows.From(plan);
        if(previous is not null)
        {
            await CalendarImportReconciliation.VerifyAsync(database,plan,rows,previous,environment,token);
            await transaction.CommitAsync(token);return Outcome(false,true,reviewOnly);
        }
        if(reviewOnly){await transaction.RollbackAsync(token);return Outcome(false,false,true);}
        var now=DateTimeOffset.UtcNow;now=now.AddTicks(-(now.Ticks%10));
        database.CalendarImportStates.Add(new()
        {
            Digest=manifest.Digest,RosterDigest=manifest.RosterDigest,SourceCommit=manifest.SourceCommit,
            TimeZone=manifest.TimeZone,Environment=environment,ExportedAt=manifest.ExportedAt,AppliedAt=now,
            IsComplete=plan.QuarantinedRows==0,
            CountsJson=rows.CountsJson,ManifestBytes=manifest.CopySourceBytes()
        });
        database.CalendarSourceDocuments.AddRange(rows.Sources);database.CalendarImportIssues.AddRange(rows.Issues);
        database.CalendarItems.AddRange(rows.Items);database.CalendarItemScopes.AddRange(rows.Scopes);
        database.CalendarItemGuests.AddRange(rows.Guests);database.CalendarResponses.AddRange(rows.Responses);
        database.SecurityAuditEvents.Add(new SecurityAuditRecord{Id=Guid.NewGuid(),OccurredAt=now,
            Action="calendar.import",Outcome="success",ReasonCode="initial_snapshot",SubjectDigest=manifest.Digest});
        await database.SaveChangesAsync(token);await transaction.CommitAsync(token);return Outcome(true,false,false);

        CalendarImportOutcome Outcome(bool applied,bool noop,bool reviewed)=>new(applied,noop,reviewed,manifest.Digest,rows.CountsJson,
            plan.Issues.GroupBy(row=>row.Code,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.Count(),StringComparer.Ordinal));
    }
    private static RosterImportApplyException Refused(string code)=>new(code);
}
