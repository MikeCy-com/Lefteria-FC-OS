using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static Task CalendarImportPlannerAsync()
    {
        var (manifest,context)=CalendarManifestFixture();
        CalendarImportPlan Plan(JsonObject value)=>CalendarImportPlanner.Build(CalendarImportValidator.Validate(RosterBytes(value)),context);
        var bytes=RosterBytes(manifest);var validated=CalendarImportValidator.Validate(bytes);var plan=Plan(manifest);
        Check.Equal(0,plan.QuarantinedRows,"Source-shaped calendar fixture was quarantined.");
        Check.Equal(5,plan.Items.Count,"Fixture, event and training source kinds were flattened.");
        Check.Equal(3,plan.Responses.Count,"Independent historical RSVP and actual attendance rows were lost.");
        var fixture=plan.Items.Single(item=>item.Id=="calendar-match");
        Check.Equal("completed",fixture.Status!,"Source MatchStatus finished alias was not canonicalized.");
        Check.Equal("Home – Away",fixture.Staff.Title,"Fixture title was fabricated from an unrelated title field.");
        Check.Equal(new DateOnly(2026,3,1),fixture.Staff.Day,"Aware fixture instant was not converted across the club month boundary.");
        Check.Equal("19:30",fixture.Staff.LocalTime!,"Explicit match_time did not win within the converted club day.");
        Check.Equal(new DateTimeOffset(2026,3,1,17,30,0,TimeSpan.Zero),fixture.Staff.StartsAt!.Value,"Club wall-clock fixture time used the machine zone.");
        var generic=plan.Items.Single(item=>item.Id=="calendar-event");
        Check.True(generic.Staff.StartsAt is null&&generic.Staff.LocalTime is null,"A date-only all-day event invented a midnight instant.");
        Check.Equal(new DateOnly(2026,3,3),generic.Staff.EndDay!.Value,"Generic event end_date was dropped.");
        Check.Equal("A public event description",generic.Staff.Description!,"Generic event description was lost.");
        var academy=plan.Items.Single(item=>item.Id=="calendar-academy-event");
        Check.True(academy.OrganizationId==OrganizationIds.Academy&&academy.Scopes.Count==2&&academy.Scopes.All(scope=>scope.Kind=="group"),"Legacy team_id group alias or plural group union created an FC team.");
        var training=plan.Items.Single(item=>item.Kind=="training");
        Check.Equal(new DateOnly(2026,3,1),training.Staff.Day,"Staff calendar lost the current training draft date.");
        Check.Equal(new DateOnly(2026,2,28),training.Member!.Day,"Frozen published date was replaced by the newer draft date.");
        Check.Equal("Published training",training.Member.Title,"Draft title leaked into an existing published snapshot.");
        Check.Equal("Approved summary",training.Member.Summary!,"Published member text was not trimmed.");
        Check.True(training.Staff.Summary is null&&training.Staff.Description is null&&training.Member.Description is null,"Training private notes or coaching descriptions entered calendar projections.");
        Check.True(plan.Manifest.Rows.Any(row=>row.Json.Contains("PRIVATE_CALENDAR_MARKER",StringComparison.Ordinal)),"Unknown and private source metadata were dropped from the owner-only archive.");
        Check.True(validated.CopySourceBytes().SequenceEqual(bytes),"Calendar source bytes changed during validation.");
        bytes[0]=0;var copy=validated.CopySourceBytes();copy[0]=0;
        Check.True(validated.CopySourceBytes()[0]=='{',"Caller could mutate protected calendar archive bytes.");
        Check.True(!training.ToString().Contains("Published training",StringComparison.Ordinal)&&!training.Member.ToString().Contains("Approved summary",StringComparison.Ordinal)&&
            !validated.Rows[0].ToString().Contains("PRIVATE_CALENDAR_MARKER",StringComparison.Ordinal),"Calendar record ToString leaked source content.");
        Check.True(Plan(manifest).Items.Select(item=>(item.Kind,item.Id,item.Staff.StartsAt)).SequenceEqual(plan.Items.Select(item=>(item.Kind,item.Id,item.Staff.StartsAt))),"Calendar plan was not deterministic.");

        foreach(var status in new[]{"draft","review_pending","rejected"})
        {
            var draft=manifest.DeepClone().AsObject();var row=draft["training_sessions"]![0]!;row["content_status"]=status;row.AsObject().Remove("published_snapshot");
            Check.True(Plan(draft).Items.Single(item=>item.Kind=="training").Member is null,"Unpublished workflow state exposed a member training projection.");
        }
        foreach(var status in new JsonNode?[]{null,JsonValue.Create("")})
        {
            var legacy=manifest.DeepClone().AsObject();var row=legacy["training_sessions"]![0]!;row["content_status"]=status?.DeepClone();row.AsObject().Remove("published_snapshot");
            var member=Plan(legacy).Items.Single(item=>item.Kind=="training").Member;
            Check.True(member is not null&&member.Summary is null&&member.Journey is null&&member.Preparation is null,"Legacy training copied unpublished member text.");
        }
        var missing=manifest.DeepClone().AsObject();missing["training_sessions"]![0]!.AsObject().Remove("content_status");missing["training_sessions"]![0]!.AsObject().Remove("published_snapshot");
        Check.True(Plan(missing).Items.Single(item=>item.Kind=="training").Member is not null,"Missing legacy workflow status hid a safe schedule.");
        var invalidSnapshot=manifest.DeepClone().AsObject();invalidSnapshot["training_sessions"]![0]!["published_snapshot"]=new JsonObject{["title"]="Incomplete"};
        Check.True(!Plan(invalidSnapshot).Items.Any(item=>item.Kind=="training"),"Invalid published snapshot fell back to a private draft.");
        var caps=manifest.DeepClone().AsObject();var published=caps["training_sessions"]![0]!["published_snapshot"]!;
        published["title"]=new string('t',501);published["member_summary"]=new string('s',1201);published["session_journey"]=new string('j',1201);published["preparation"]=new string('p',601);
        var capped=Plan(caps).Items.Single(item=>item.Kind=="training").Member!;
        Check.True(capped.Title.Length==500&&capped.Summary!.Length==1200&&capped.Journey!.Length==1200&&capped.Preparation!.Length==600,"Member safe field caps differ from the source allowlist.");

        foreach(var (value,code) in new[]{("2026-03-29T03:30:00","time_zone_gap"),("2026-10-25T03:30:00","time_zone_ambiguous"),("March 1, 2026","date_value")})
        {
            var invalid=manifest.DeepClone().AsObject();invalid["events"]![0]!["date"]=value;
            Check.True(Plan(invalid).Issues.Any(issue=>issue.Collection=="events"&&issue.Ordinal==0&&issue.Code==code),"Invalid or ambiguous club local time was guessed.");
        }
        foreach(var (source,expectedHour) in new[]{("2026-10-25T03:30:00+03:00",0),("2026-10-25T03:30:00+02:00",1)})
        {
            var overlap=manifest.DeepClone().AsObject();overlap["events"]![0]!["date"]=source;overlap["events"]![0]!["end_date"]=null;
            var projection=Plan(overlap).Items.Single(item=>item.Id=="calendar-event").Staff;
            Check.True(projection.StartsAt==new DateTimeOffset(2026,10,25,expectedHour,30,0,TimeSpan.Zero)&&projection.LocalTime=="03:30"&&projection.SourceDate==source,
                "An offset-qualified DST-fold instant was replaced by a guessed club offset.");
        }
        var aware=manifest.DeepClone().AsObject();aware["events"]![0]!["date"]="2026-10-25T03:30:00+03:00";aware["events"]![0]!["end_date"]=null;
        aware["events"]![0]!["start_time"]="03:30";
        Check.True(Plan(aware).Issues.Any(issue=>issue.Code=="time_zone_ambiguous"),"An explicit ambiguous wall time inherited an arbitrary source offset.");
        foreach(var (source,code) in new[]{("2026-03-29T02:30:00+02:00","time_zone_gap"),("2026-10-25T04:30:00+02:00","time_zone_ambiguous")})
        {
            var overridden=manifest.DeepClone().AsObject();overridden["events"]![0]!["date"]=source;overridden["events"]![0]!["end_date"]=null;overridden["events"]![0]!["start_time"]="03:30";
            Check.True(Plan(overridden).Issues.Any(issue=>issue.Collection=="events"&&issue.Ordinal==0&&issue.Code==code),
                "A conflicting explicit wall time inherited an offset instead of rejecting the club DST gap or fold.");
        }
        var fractional=manifest.DeepClone().AsObject();fractional["events"]![0]!["date"]="2026-03-01T10:12:13.1234567Z";fractional["events"]![0]!["end_date"]=null;
        var precision=Plan(fractional).Items.Single(item=>item.Id=="calendar-event").Staff;
        Check.True(precision.StartsAt!.Value.Ticks%10==0&&precision.SourceDate=="2026-03-01T10:12:13.1234567Z","PostgreSQL instant precision or raw source date preservation changed.");
        var backwards=manifest.DeepClone().AsObject();backwards["events"]![0]!["end_date"]="2026-02-28";
        Check.True(Plan(backwards).Issues.Any(issue=>issue.Code=="end_before_start"),"A backwards event range was accepted.");

        var duplicate=manifest.DeepClone().AsObject();var competing=duplicate["fixtures"]![0]!.DeepClone();competing["match_date"]=42;duplicate["fixtures"]!.AsArray().Add(competing);
        Check.Equal(2,Plan(duplicate).Issues.Count(issue=>issue.Code=="duplicate_id"),"Malformed duplicate schedule row permitted an arbitrary first winner.");
        var ambiguous=manifest.DeepClone().AsObject();var competingKind=ambiguous["events"]![0]!.DeepClone();competingKind["id"]="calendar-match";competingKind["date"]="invalid";ambiguous["events"]!.AsArray().Add(competingKind);
        ambiguous["availability"]![0]!.AsObject().Remove("event_type");
        Check.True(Plan(ambiguous).Issues.Any(issue=>issue.Code=="event_type_ambiguous"),"Malformed cross-kind competitor erased source event ID ambiguity.");
        ambiguous["availability"]![0]!["event_type"]="fixture";
        Check.True(Plan(ambiguous).Responses.Any(response=>response.Collection=="availability"&&response.Kind=="match"),"A typed fixture alias lost a valid unambiguous typed FK.");
        var duplicateResponse=manifest.DeepClone().AsObject();var peer=duplicateResponse["availability"]![0]!.DeepClone();peer["status"]="invented";duplicateResponse["availability"]!.AsArray().Add(peer);
        Check.Equal(2,Plan(duplicateResponse).Issues.Count(issue=>issue.Code=="duplicate_response"),"Invalid response competitor permitted an arbitrary winner.");

        void Quarantines(Action<JsonObject> change,string code)
        {
            var invalid=manifest.DeepClone().AsObject();change(invalid);
            Check.True(Plan(invalid).Issues.Any(issue=>issue.Code==code),"Calendar semantic boundary failed: "+code);
        }
        Quarantines(value=>value["events"]![1]!["organization_id"]=OrganizationIds.Club,"group_reference");
        Quarantines(value=>value["events"]![1]!["group_id"]="calendar-group-b","conflicting_group_aliases");
        Quarantines(value=>value["events"]![1]!["team_id"]="calendar-team","team_reference");
        Quarantines(value=>value["events"]![1]!["guest_player_ids"]=new JsonArray("calendar-player"),"guest_player_reference");
        Quarantines(value=>value["availability"]![0]!["player_id"]="calendar-child","player_reference");
        Quarantines(value=>value["availability"]![0]!["organization_id"]=OrganizationIds.Academy,"response_organization");
        Quarantines(value=>value["fixtures"]![0]!["status"]="unknown","match_status_unknown");
        Quarantines(value=>value["fixtures"]![0]!["status"]="","match_status_unknown");
        Quarantines(value=>value["availability"]![0]!["event_type"]="","event_type_unknown");
        Quarantines(value=>value["availability"]![0]!["status"]="no_response","response_status_unknown");
        Quarantines(value=>value["attendance"]![0]!["status"]="going","response_status_unknown");
        Quarantines(value=>value["events"]![0]!["title"]=42,"string_type");
        Quarantines(value=>value["events"]![0]!["archived"]="false","boolean_flag");
        Quarantines(value=>value["training_sessions"]![0]!["duration_minutes"]=true,"duration_minutes");
        Quarantines(value=>value["training_sessions"]![0]!["published_snapshot"]=new JsonArray(),"published_snapshot_type");
        Quarantines(value=>value["events"]![0]!["location_url"]="javascript:alert(1)","location_url");
        var allScopes=manifest.DeepClone().AsObject();allScopes["events"]![0]!["team_ids"]=new JsonArray("calendar-team-b");
        Check.Equal(2,Plan(allScopes).Items.Single(item=>item.Id=="calendar-event").Scopes.Count,"Plural team scope replaced the singular team.");
        var legacyGroup=manifest.DeepClone().AsObject();legacyGroup["events"]![1]!.AsObject().Remove("academy_group_id");legacyGroup["events"]![1]!.AsObject().Remove("academy_group_ids");
        var legacyItem=Plan(legacyGroup).Items.Single(item=>item.Id=="calendar-academy-event");
        Check.True(legacyItem.OrganizationId==OrganizationIds.Academy&&legacyItem.Scopes.Single() is {Kind:"group",RecordId:"calendar-group-a"},"A legacy event's only team_id group pointer was classified as an FC team.");
        var explicitOrganization=manifest.DeepClone().AsObject();explicitOrganization["events"]![0]!["organization_id"]=OrganizationIds.Club;explicitOrganization["events"]![0]!["team_type"]="Academy";
        Check.Equal(OrganizationIds.Club,Plan(explicitOrganization).Items.Single(item=>item.Id=="calendar-event").OrganizationId,"Known explicit organization lost precedence to a legacy marker.");
        foreach(var (collection,field,id,marker) in new[]{("events","team_type","calendar-event","aCaDeMy"),("fixtures","competition","calendar-match","ΑΚΑΔΗΜΙΑ")})
        {
            var longMarker=manifest.DeepClone().AsObject();var row=longMarker[collection]![0]!;row.AsObject().Remove("team_id");row[field]=new string('x',600)+marker;
            Check.Equal(OrganizationIds.Academy,Plan(longMarker).Items.Single(item=>item.Id==id).OrganizationId,
                "Display truncation hid a source Academy organization marker.");
            row["organization_id"]=OrganizationIds.Club;
            Check.Equal(OrganizationIds.Club,Plan(longMarker).Items.Single(item=>item.Id==id).OrganizationId,
                "A full source Academy marker overrode a known explicit organization.");
        }
        Quarantines(value=>value["events"]![0]!["id"]=" calendar-event","id_value");
        var opaqueId=manifest.DeepClone().AsObject();opaqueId["events"]![0]!["id"]="Calendar Event / CaseSensitive";
        Check.True(Plan(opaqueId).Items.Any(item=>item.Id=="Calendar Event / CaseSensitive"),"A valid opaque source identifier was normalized.");
        var cancellation=manifest.DeepClone().AsObject();cancellation["fixtures"]![0]!["status"]="CANCELED";cancellation["fixtures"]![0]!["archived"]=true;
        Check.True(Plan(cancellation).Items.Single(item=>item.Id=="calendar-match") is {IsCancelled:true,IsArchived:true},"Cancellation and archive flags were flattened.");
        var noStatus=manifest.DeepClone().AsObject();noStatus["fixtures"]![0]!.AsObject().Remove("status");
        Check.True(Plan(noStatus).Items.Single(item=>item.Id=="calendar-match").Status is null,"Missing historical match status was fabricated.");
        var actors=manifest.DeepClone().AsObject();actors["availability"]![0]!["role"]="super_admin";actors["availability"]![0]!["linked_player_ids"]=new JsonArray("calendar-child");
        Check.Equal(3,Plan(actors).Responses.Count,"Unrelated source actor metadata changed canonical response ownership.");

        void Rejects(byte[] input,string code)
        {
            try{CalendarImportValidator.Validate(input);throw new InvalidOperationException("Invalid calendar envelope was accepted: "+code);}
            catch(RosterImportValidationException exception){Check.Equal(code,exception.Issue.Code,"Calendar envelope returned an unsafe or incorrect issue code.");}
        }
        foreach(var (field,replacement,code) in new (string,JsonNode?,string)[]{("schema_version",JsonValue.Create("1"),"schema_version"),("time_zone",JsonValue.Create("Cyprus Standard Time"),"time_zone"),
            ("exported_at",JsonValue.Create("2026-03-01T00:00:00+00:00"),"exported_at"),("roster_sha256",JsonValue.Create("bad"),"roster_sha256"),
            ("complete_collections",new JsonArray("fixtures"),"complete_collections"),("events",new JsonObject(),"collection_shape")})
        {var invalid=manifest.DeepClone().AsObject();invalid[field]=replacement;Rejects(RosterBytes(invalid),code);}
        var missingCollection=manifest.DeepClone().AsObject();missingCollection.Remove("attendance");Rejects(RosterBytes(missingCollection),"collection_shape");
        var unknownManifest=manifest.DeepClone().AsObject();unknownManifest["private_note"]="PRIVATE_CALENDAR_MARKER";Rejects(RosterBytes(unknownManifest),"unknown_manifest_field");
        var wrongRow=manifest.DeepClone().AsObject();wrongRow["events"]!.AsArray().Add(42);Rejects(RosterBytes(wrongRow),"row_object_required");
        Rejects(new byte[CalendarImportValidator.MaximumBytes+1],"input_size");
        var json=Encoding.UTF8.GetString(RosterBytes(manifest));
        Rejects(Encoding.UTF8.GetBytes(json.Replace("\"schema_version\":1","\"schema_version\":1,\"schema_version\":1",StringComparison.Ordinal)),"duplicate_json_key");
        Rejects(Encoding.UTF8.GetBytes(json.Replace("\"title\":\"Public event\"","\"title\":\"Public event\",\"title\":\"duplicate\"",StringComparison.Ordinal)),"duplicate_json_key");
        Rejects([0x7b,0x22,0xff,0x22,0x3a,0x31,0x7d],"invalid_utf8");
        try{CalendarImportPlanner.Build(validated,context with{RosterDigest=new string('0',64)});throw new InvalidOperationException("Calendar import accepted an unbound roster digest.");}
        catch(RosterImportApplyException exception){Check.Equal("calendar_base_snapshot_mismatch",exception.Code,"Calendar context mismatch was not safely refused.");}
        // The owner archive reader verifies the original digest against import state,
        // then reconstructs JSON from its retained source documents. Its new byte
        // serialization has its own digest; the explicit context hash stays bound
        // to the original verified import rather than to these reconstructed bytes.
        var reconstructed=new JsonObject
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=context.SourceCommit,
            ["complete_collections"]=new JsonArray("teams","academy_groups","players")
        };
        foreach(var collection in new[]{"teams","academy_groups","players"})
            reconstructed[collection]=new JsonArray(context.Roster.Source.Where(row=>row.Collection==collection).Select(row=>JsonNode.Parse(row.Json)).ToArray());
        var reconstructedRoster=RosterImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(reconstructed,new JsonSerializerOptions{WriteIndented=true}));
        Check.True(reconstructedRoster.Digest!=context.RosterDigest&&reconstructedRoster.SourceCommit==context.SourceCommit,
            "Reconstructed roster regression did not exercise a new byte digest at the trusted source commit.");
        var reconstructedPlan=CalendarImportPlanner.Build(validated,context with{Roster=reconstructedRoster});
        Check.True(reconstructedPlan.QuarantinedRows==0&&reconstructedPlan.Items.Count==plan.Items.Count&&reconstructedPlan.Responses.Count==plan.Responses.Count,
            "A verified owner roster archive was rejected because reconstruction changed its byte digest.");
        try{CalendarImportPlanner.Build(validated,context with{SourceCommit=new string('0',40)});throw new InvalidOperationException("Calendar import accepted a different trusted source commit.");}
        catch(RosterImportApplyException exception){Check.Equal("calendar_base_snapshot_mismatch",exception.Code,"Calendar source commit mismatch was not safely refused.");}
        reconstructed["source_commit"]=new string('0',40);
        var wrongCommitRoster=RosterImportValidator.Validate(RosterBytes(reconstructed));
        try{CalendarImportPlanner.Build(validated,context with{Roster=wrongCommitRoster});throw new InvalidOperationException("Calendar import accepted roster fields from a different source commit.");}
        catch(RosterImportApplyException exception){Check.Equal("calendar_base_snapshot_mismatch",exception.Code,"Calendar roster source commit mismatch was not safely refused.");}
        return Task.CompletedTask;
    }

    private static (JsonObject Manifest,CalendarImportContext Context) CalendarManifestFixture()
    {
        const string commit="a52d66b8e01c010cfe95ce53fa139e126bae4d84";
        var roster=RosterImportValidator.Validate(Encoding.UTF8.GetBytes("""
            {"schema_version":1,"source_repository":"MikeCy-com/Lefteria-FC","source_commit":"a52d66b8e01c010cfe95ce53fa139e126bae4d84","complete_collections":["teams","academy_groups","players"],"teams":[{"id":"calendar-team","name":"Fictional team"},{"id":"calendar-team-b","name":"Fictional second team"}],"academy_groups":[{"id":"calendar-group-a","name":"Fictional group A"},{"id":"calendar-group-b","name":"Fictional group B"}],"players":[{"id":"calendar-player","name":"Fictional inactive player","number":7,"position":"Midfielder","team_type":"First Team","is_active":false,"team_id":"calendar-team"},{"id":"calendar-child","name":"Fictional child","number":3,"position":"Forward","team_type":"Academy","is_active":true,"academy_group_id":"calendar-group-a","academy_group_ids":["calendar-group-a","calendar-group-b"]}]}
            """));
        var manifest=new JsonObject
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=commit,["roster_sha256"]=roster.Digest,
            ["exported_at"]="2026-10-28T00:00:00Z",["time_zone"]="Asia/Nicosia",
            ["complete_collections"]=new JsonArray("fixtures","events","training_sessions","availability","event_attendance","attendance"),
            ["fixtures"]=new JsonArray(
                new JsonObject{["id"]="calendar-match",["home_team"]="Home",["away_team"]="Away",["match_date"]="2026-02-28T23:30:00Z",["match_time"]="19:30",["venue"]="Club ground",["status"]="fInIsHeD",["team_id"]="calendar-team",["raw_extra"]="PRIVATE_CALENDAR_MARKER"},
                new JsonObject{["id"]="calendar-academy-match",["home_team"]="Young home",["away_team"]="Young away",["match_date"]="2026-03-01",["competition"]="Ακαδημία",["academy_group_id"]="calendar-group-a"}),
            ["events"]=new JsonArray(
                new JsonObject{["id"]="calendar-event",["title"]="Public event",["type"]="meeting",["date"]="2026-03-01",["end_date"]="2026-03-03",["team_id"]="calendar-team",["description"]="A public event description"},
                new JsonObject{["id"]="calendar-academy-event",["title"]="Academy event",["type"]="other",["date"]="2026-03-01T09:00:00",["team_id"]="calendar-group-a",["academy_group_id"]="calendar-group-a",["academy_group_ids"]=new JsonArray("calendar-group-b"),["guest_player_ids"]=new JsonArray("calendar-child")}),
            ["training_sessions"]=new JsonArray(new JsonObject{["id"]="calendar-training",["title"]="Private draft title",["date"]="2026-03-01",["start_time"]="10:00",["duration_minutes"]=90,["venue"]="Draft venue",["academy_group_id"]="calendar-group-a",["content_status"]="draft",
                ["member_summary"]="Unpublished member summary",["notes"]="PRIVATE_CALENDAR_MARKER",["description"]="PRIVATE_CALENDAR_MARKER",["exercises"]=new JsonArray("PRIVATE_CALENDAR_MARKER"),
                ["published_snapshot"]=new JsonObject{["title"]="Published training",["date"]="2026-02-28",["start_time"]="11:00",["venue"]="Published venue",["member_summary"]=" Approved summary ",["session_journey"]="Approved journey",["preparation"]="Approved preparation",["notes"]="PRIVATE_CALENDAR_MARKER"}}),
            ["availability"]=new JsonArray(new JsonObject{["event_id"]="calendar-match",["event_type"]="match",["player_id"]="calendar-player",["status"]="going",["updated_at"]="2026-03-01T00:00:00.1234567Z",["submitted_by"]="PRIVATE_CALENDAR_MARKER"}),
            ["event_attendance"]=new JsonArray(new JsonObject{["event_id"]="calendar-academy-event",["player_id"]="calendar-child",["status"]="no_response",["marked_at"]="2026-03-01T00:00:00+00:00"}),
            ["attendance"]=new JsonArray(new JsonObject{["event_id"]="calendar-training",["player_id"]="calendar-child",["status"]="present"})
        };
        return(manifest,new(roster.Digest,commit,roster));
    }
}
