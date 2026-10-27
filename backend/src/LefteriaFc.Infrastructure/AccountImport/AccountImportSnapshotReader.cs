using System.Text.Json;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.StaffRelationships;
using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.AccountImport;

// Called only inside the explicit owner's import transaction/advisory lock.
internal static class AccountImportSnapshotReader
{
    public static async Task<AccountImportTrustedContext> ReadAsync(LefteriaDbContext database,
        ValidatedAccountImport manifest,string environment,CancellationToken token)
    {
        var roster=await BoundRosterArchive.ReadAsync(database,manifest.RosterDigest,manifest.SourceCommit,token);
        var state=await database.StaffRelationshipImportStates.AsNoTracking().SingleOrDefaultAsync(token);
        if(state is null)throw Refused("account_staff_snapshot_required");
        if(state.Digest!=manifest.StaffDigest||state.RosterDigest!=manifest.RosterDigest||state.SourceCommit!=manifest.SourceCommit||state.Environment!=environment)
            throw Refused("account_base_snapshot_mismatch");
        var rosterState=await database.RosterImportStates.AsNoTracking().SingleAsync(token);
        if(rosterState.Environment!=environment)throw Refused("account_base_environment_mismatch");
        var documents=await database.StaffSourceDocuments.AsNoTracking().ToArrayAsync(token);
        if(documents.Any(row=>row.ImportDigest!=state.Digest))throw Refused("account_staff_archive_mismatch");
        using var bytes=new MemoryStream();
        using(var writer=new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();writer.WriteNumber("schema_version",1);
            writer.WriteString("source_repository",ValidatedRosterImport.Repository);writer.WriteString("source_commit",state.SourceCommit);
            writer.WriteString("roster_sha256",state.RosterDigest);writer.WriteStartArray("complete_collections");writer.WriteStringValue("staff");writer.WriteEndArray();
            writer.WriteStartArray("staff");
            foreach(var row in documents.OrderBy(row=>row.SourceId,StringComparer.Ordinal))writer.WriteRawValue(row.SourceJson);
            writer.WriteEndArray();writer.WriteEndObject();
        }
        ValidatedStaffRelationshipImport staff;
        try{staff=StaffRelationshipImportValidator.Validate(bytes.ToArray());}
        catch(RosterImportValidationException){throw Refused("account_staff_archive_invalid");}
        if(staff.Staff.Count!=state.StaffCount||staff.Source.Count!=documents.Length||
            documents.Any(row=>!staff.Source.Any(source=>source.SourceId==row.SourceId&&source.Json==row.SourceJson)))throw Refused("account_staff_archive_mismatch");
        var people=await database.Staff.AsNoTracking().ToArrayAsync(token);
        if(!Same(people.Select(row=>(row.Id,row.Name,row.JobRole,row.TeamType,row.IsActive,row.IsArchived,row.HasExplicitActive,row.HasExplicitArchived)),
            staff.Staff.Select(row=>(row.Id,row.Name,row.JobRole,row.TeamType,row.IsActive,row.IsArchived,row.HasExplicitActive,row.HasExplicitArchived))))throw Refused("account_staff_reconciliation_failed");
        var wantedTeams=staff.Staff.SelectMany(person=>person.Teams.Select(team=>(person.Id,team.Id,team.FromPlural,team.FromSingular))).ToArray();
        var teams=await database.StaffTeams.AsNoTracking().ToArrayAsync(token);
        if(teams.Any(row=>row.OrganizationId!="lefteria_fc")||!Same(teams.Select(row=>(row.StaffId,row.TeamId,row.FromPlural,row.FromSingular)),wantedTeams))throw Refused("account_staff_reconciliation_failed");
        var expectedGroups=new Dictionary<(string Staff,string Group),(bool Plural,bool Singular,bool Coach)>();
        foreach(var person in staff.Staff)foreach(var group in person.Groups)expectedGroups.Add((person.Id,group.Id),(group.FromPlural,group.FromSingular,false));
        var wantedCoaches=new List<(string Group,string Staff)>();
        var contacts=staff.Contacts.ToList();
        foreach(var source in roster.Source)
        {
            using var document=JsonDocument.Parse(source.Json);var row=document.RootElement;
            if(source.Collection=="academy_groups"&&StaffRelationshipImportValidator.OptionalId(row,"coach_id") is{ } coach)
            {
                wantedCoaches.Add((source.SourceId,coach));
                var key=(coach,source.SourceId);var before=expectedGroups.GetValueOrDefault(key);
                expectedGroups[key]=(before.Plural,before.Singular,true);
            }
            if(source.Collection=="players")foreach(var field in new[]{"phone","parent_phone","parent_email","parent_name"})
                contacts.Add(StaffRelationshipImportValidator.ReadContact(row,"players",source.SourceId,field,field=="phone"?"player_own":"guardian",field=="parent_email"?320:field=="parent_name"?200:128));
        }
        var groups=await database.StaffAcademyGroups.AsNoTracking().ToArrayAsync(token);
        var coaches=await database.AcademyGroupCoaches.AsNoTracking().ToArrayAsync(token);
        if(groups.Any(row=>row.OrganizationId!="lefteria_fc_academy")||coaches.Any(row=>row.OrganizationId!="lefteria_fc_academy")||
            !Same(groups.Select(row=>(row.StaffId,row.AcademyGroupId,row.FromPlural,row.FromSingular,row.FromGroupCoach)),expectedGroups.Select(row=>(row.Key.Staff,row.Key.Group,row.Value.Plural,row.Value.Singular,row.Value.Coach)))||
            !Same(coaches.Select(row=>(row.AcademyGroupId,row.StaffId)),wantedCoaches))throw Refused("account_staff_reconciliation_failed");
        var storedContacts=await database.ContactProvenance.AsNoTracking().ToArrayAsync(token);
        if(state.TeamAssignmentsCount!=teams.Length||state.GroupAssignmentsCount!=groups.Length||state.GroupCoachesCount!=coaches.Length||state.ContactsCount!=contacts.Count||
            !Same(storedContacts.Select(row=>(row.SourceCollection,row.SourceId,row.Field,row.Kind,row.Presence,row.RawValue,row.PlayerId,row.StaffId,row.SourceDigest,row.ImportDigest)),
                contacts.Select(row=>(row.Collection,row.SourceId,row.Field,row.Kind,row.Presence,row.RawValue,row.Collection=="players"?row.SourceId:null,row.Collection=="staff"?row.SourceId:null,row.Collection=="players"?state.RosterDigest:state.Digest,state.Digest))))
            throw Refused("account_contact_reconciliation_failed");
        return new(state.RosterDigest,state.Digest,state.SourceCommit,roster,staff);
    }
    private static bool Same<T>(IEnumerable<T> actual,IEnumerable<T> expected)
    {var left=actual.ToArray();var right=expected.ToArray();return left.Length==right.Length&&left.ToHashSet().SetEquals(right);}
    private static RosterImportApplyException Refused(string code)=>new(code);
}
