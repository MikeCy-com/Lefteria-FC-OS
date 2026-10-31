using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.ClubCalendar;

internal static class CalendarModel
{
    public static void Configure(ModelBuilder model)
    {
        var state=model.Entity<CalendarImportStateRecord>();
        state.ToTable("calendar_import_state","club",table=>
        {
            table.HasCheckConstraint("ck_calendar_initial","id='initial' AND digest ~ '^[0-9a-f]{64}$' AND roster_digest ~ '^[0-9a-f]{64}$' AND source_commit ~ '^[0-9a-f]{40}$' AND environment IN ('development','staging')");
            table.HasCheckConstraint("ck_calendar_manifest_bytes","octet_length(manifest_bytes) BETWEEN 1 AND 33554432");
            table.HasCheckConstraint("ck_calendar_counts_json","jsonb_typeof(counts_json)='object'");
            table.HasCheckConstraint("ck_calendar_complete","counts_json ? 'quarantined_rows' AND jsonb_typeof(counts_json->'quarantined_rows')='number' AND (counts_json->>'quarantined_rows')::integer>=0 AND is_complete = ((counts_json->>'quarantined_rows')::integer = 0)");
        });
        state.HasKey(row=>row.Id);state.HasAlternateKey(row=>row.Digest);
        state.Property(row=>row.Id).HasMaxLength(16).ValueGeneratedNever();
        state.Property(row=>row.Digest).HasMaxLength(64);state.Property(row=>row.RosterDigest).HasMaxLength(64);
        state.Property(row=>row.SourceCommit).HasMaxLength(40);state.Property(row=>row.Environment).HasMaxLength(16);
        state.Property(row=>row.TimeZone).HasMaxLength(100);state.Property(row=>row.CountsJson).HasColumnType("jsonb");
        var source=model.Entity<CalendarSourceDocumentRecord>();
        source.ToTable("calendar_source_documents","migration",table=>
        {
            table.HasCheckConstraint("ck_calendar_source_collection","collection IN ('fixtures','events','training_sessions','availability','event_attendance','attendance') AND ordinal>=0");
            table.HasCheckConstraint("ck_calendar_source_json","json_typeof(source_json::json)='object'");
            table.HasCheckConstraint("ck_calendar_source_disposition","disposition IN ('item','response','quarantined')");
        });
        source.HasKey(row=>new{row.Collection,row.Ordinal});source.Property(row=>row.Collection).HasMaxLength(32);
        source.Property(row=>row.ImportDigest).HasMaxLength(64);source.Property(row=>row.Disposition).HasMaxLength(16);
        source.Property(row=>row.TargetKind).HasMaxLength(16);source.Property(row=>row.TargetId).HasMaxLength(128).UseCollation("C");
        source.HasOne<CalendarImportStateRecord>().WithMany().HasForeignKey(row=>row.ImportDigest).HasPrincipalKey(row=>row.Digest).OnDelete(DeleteBehavior.Restrict);
        var issue=model.Entity<CalendarImportIssueRecord>();
        issue.ToTable("calendar_import_issues","migration");issue.HasKey(row=>new{row.Collection,row.Ordinal,row.Code});
        issue.Property(row=>row.Collection).HasMaxLength(32);issue.Property(row=>row.Code).HasMaxLength(80);
        issue.HasOne<CalendarSourceDocumentRecord>().WithMany().HasForeignKey(row=>new{row.Collection,row.Ordinal}).OnDelete(DeleteBehavior.Restrict);
        var item=model.Entity<CalendarItemRecord>();
        item.ToTable("calendar_items","club",table=>
        {
            table.HasCheckConstraint("ck_calendar_item_kind","kind IN ('event','match','training')");
            table.HasCheckConstraint("ck_calendar_item_projection","jsonb_typeof(staff_projection_json)='object' AND staff_end_day>=staff_day AND ((member_projection_json IS NULL AND member_day IS NULL AND member_end_day IS NULL) OR (jsonb_typeof(member_projection_json)='object' AND member_day IS NOT NULL AND member_end_day IS NOT NULL AND member_end_day>=member_day))");
            table.HasCheckConstraint("ck_calendar_item_origin","(kind='match' AND source_collection='fixtures') OR (kind='event' AND source_collection='events') OR (kind='training' AND source_collection='training_sessions')");
        });
        item.HasKey(row=>new{row.Kind,row.Id});item.HasAlternateKey(row=>new{row.OrganizationId,row.Kind,row.Id});
        item.Property(row=>row.Kind).HasMaxLength(16);item.Property(row=>row.Id).HasMaxLength(128).UseCollation("C");
        item.Property(row=>row.OrganizationId).HasMaxLength(32);item.Property(row=>row.SourceCollection).HasMaxLength(32);
        item.Property(row=>row.EventType).HasMaxLength(80);item.Property(row=>row.Status).HasMaxLength(32);
        item.Property(row=>row.ContentStatus).HasMaxLength(32);item.Property(row=>row.CancelReason).HasMaxLength(1200);
        item.Property(row=>row.StaffProjectionJson).HasColumnType("jsonb");item.Property(row=>row.MemberProjectionJson).HasColumnType("jsonb");
        item.HasIndex(row=>new{row.OrganizationId,row.StaffDay,row.Kind,row.Id});item.HasIndex(row=>new{row.OrganizationId,row.MemberDay,row.Kind,row.Id});
        item.HasOne<OrganizationRecord>().WithMany().HasForeignKey(row=>row.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<CalendarSourceDocumentRecord>().WithMany().HasForeignKey(row=>new{row.SourceCollection,row.SourceOrdinal}).OnDelete(DeleteBehavior.Restrict);
        var scope=model.Entity<CalendarItemScopeRecord>();
        scope.ToTable("calendar_item_scopes","club",table=>table.HasCheckConstraint("ck_calendar_scope_kind","(scope_kind='team' AND team_id=record_id AND team_id IS NOT NULL AND academy_group_id IS NULL) OR (scope_kind='group' AND academy_group_id=record_id AND academy_group_id IS NOT NULL AND team_id IS NULL)"));
        scope.HasKey(row=>new{row.Kind,row.EventId,row.ScopeKind,row.RecordId});
        scope.Property(row=>row.Kind).HasMaxLength(16);scope.Property(row=>row.EventId).HasMaxLength(128).UseCollation("C");
        scope.Property(row=>row.OrganizationId).HasMaxLength(32);scope.Property(row=>row.ScopeKind).HasMaxLength(16);
        scope.Property(row=>row.RecordId).HasMaxLength(128).UseCollation("C");scope.Property(row=>row.TeamId).HasMaxLength(128).UseCollation("C");scope.Property(row=>row.AcademyGroupId).HasMaxLength(128).UseCollation("C");
        scope.HasOne<CalendarItemRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.Kind,row.EventId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Kind,row.Id}).OnDelete(DeleteBehavior.Restrict);
        scope.HasOne<RosterTeamRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.TeamId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Id}).OnDelete(DeleteBehavior.Restrict);
        scope.HasOne<RosterAcademyGroupRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.AcademyGroupId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Id}).OnDelete(DeleteBehavior.Restrict);
        var guest=model.Entity<CalendarItemGuestRecord>();
        guest.ToTable("calendar_item_guests","club");guest.HasKey(row=>new{row.Kind,row.EventId,row.PlayerId});
        guest.Property(row=>row.Kind).HasMaxLength(16);guest.Property(row=>row.EventId).HasMaxLength(128).UseCollation("C");guest.Property(row=>row.PlayerId).HasMaxLength(128).UseCollation("C");guest.Property(row=>row.OrganizationId).HasMaxLength(32);
        guest.HasOne<CalendarItemRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.Kind,row.EventId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Kind,row.Id}).OnDelete(DeleteBehavior.Restrict);
        guest.HasOne<RosterPlayerRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.PlayerId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Id}).OnDelete(DeleteBehavior.Restrict);
        var response=model.Entity<CalendarResponseRecord>();
        response.ToTable("calendar_responses","club",table=>table.HasCheckConstraint("ck_calendar_response_status","(collection='availability' AND status IN ('going','not_going','maybe')) OR (collection='event_attendance' AND status IN ('going','not_going','maybe','no_response')) OR (collection='attendance' AND status IN ('present','absent'))"));
        response.HasKey(row=>new{row.Collection,row.Kind,row.EventId,row.PlayerId});
        response.Property(row=>row.Collection).HasMaxLength(32);response.Property(row=>row.Kind).HasMaxLength(16);response.Property(row=>row.EventId).HasMaxLength(128).UseCollation("C");response.Property(row=>row.PlayerId).HasMaxLength(128).UseCollation("C");response.Property(row=>row.OrganizationId).HasMaxLength(32);response.Property(row=>row.Status).HasMaxLength(16);
        response.HasOne<CalendarItemRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.Kind,row.EventId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Kind,row.Id}).OnDelete(DeleteBehavior.Restrict);
        response.HasOne<RosterPlayerRecord>().WithMany().HasForeignKey(row=>new{row.OrganizationId,row.PlayerId}).HasPrincipalKey(row=>new{row.OrganizationId,row.Id}).OnDelete(DeleteBehavior.Restrict);
        response.HasOne<MobileIdentityRecord>().WithMany().HasForeignKey(row=>row.ActorIdentityId).OnDelete(DeleteBehavior.Restrict);
        response.HasOne<CalendarSourceDocumentRecord>().WithMany().HasForeignKey(row=>new{row.Collection,row.SourceOrdinal}).OnDelete(DeleteBehavior.Restrict);
    }
}
