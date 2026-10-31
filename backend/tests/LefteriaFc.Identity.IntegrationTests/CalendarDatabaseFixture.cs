using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.CalendarImport;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class CalendarDatabaseFixture
{
    public required AccountImportFixture Accounts{get;init;}
    public required JsonObject Input{get;init;}
    public required ValidatedCalendarImport Manifest{get;init;}
    public required CalendarImportPlan Plan{get;init;}
    public string Id(string suffix)=>Accounts.Prefix+"-calendar-"+suffix;
    public string SecondGroup=>Accounts.Prefix+"-group-two";
    public string SecondChild=>Accounts.Prefix+"-child-two";
    public string FcChild=>Accounts.Prefix+"-child-fc";
    public override string ToString()=>"Synthetic protected calendar fixture";

    public static CalendarDatabaseFixture Create(AccountImportFixture accounts,JsonObject? replacement=null)
    {
        string Id(string suffix)=>accounts.Prefix+"-calendar-"+suffix;
        JsonObject Event(string suffix,string group,string day="2026-10-20")=>new()
        {
            ["id"]=Id(suffix),["title"]="Fictional "+suffix,["type"]="other",["date"]=day,
            ["academy_group_id"]=group,["description"]="Ασφαλής περιγραφή – café & \"quoted\" <text>",
            ["private_notes"]="PRIVATE_CALENDAR_SOURCE_MARKER"
        };
        var input=replacement??new JsonObject
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=AccountImportFixture.Commit,
            ["roster_sha256"]=accounts.Roster.Digest,["exported_at"]="2026-10-28T12:34:56.1234567Z",["time_zone"]="Asia/Nicosia",
            ["complete_collections"]=new JsonArray("fixtures","events","training_sessions","availability","event_attendance","attendance"),
            ["fixtures"]=new JsonArray(new JsonObject{["id"]=Id("shared"),["home_team"]="Fictional home",["away_team"]="Fictional away",["match_date"]="2026-10-12T09:30:00.1234567Z",["team_id"]=accounts.TeamId,["status"]="finished"}),
            ["events"]=new JsonArray(),["training_sessions"]=new JsonArray(),["availability"]=new JsonArray(),["event_attendance"]=new JsonArray(),["attendance"]=new JsonArray()
        };
        if(replacement is null)
        {
            var events=input["events"]!.AsArray();
            events.Add(new JsonObject{["id"]=Id("shared"),["title"]="Fictional shared event ID",["type"]="meeting",["date"]="2026-10-12",["team_id"]=accounts.TeamId});
            var host=Event("host",accounts.GroupId);host["guest_player_ids"]=new JsonArray(accounts.Prefix+"-child-two");events.Add(host);
            var outside=Event("outside",accounts.Prefix+"-group-two");outside["guest_player_ids"]=new JsonArray(accounts.ChildId);events.Add(outside);
            events.Add(Event("group-two",accounts.Prefix+"-group-two"));
            var cancel=Event("cancelled",accounts.GroupId);cancel["cancelled"]=true;cancel["cancel_reason"]="Fictional weather cancellation";events.Add(cancel);
            var archive=Event("archived",accounts.GroupId);archive["archived"]=true;events.Add(archive);
            events.Add(Event("past",accounts.GroupId,"2001-01-01"));
            for(var index=0;index<201;index++)events.Add(Event("page-"+index.ToString("D3"),accounts.GroupId,"2026-12-01"));
            input["training_sessions"]!.AsArray().Add(new JsonObject
            {
                ["id"]=Id("published"),["title"]="Staff draft training title",["date"]="2026-11-02",["start_time"]="18:00",["venue"]="Draft venue",["academy_group_id"]=accounts.GroupId,["content_status"]="draft",
                ["member_summary"]="UNPUBLISHED_SUMMARY_MARKER",["notes"]="PRIVATE_CALENDAR_SOURCE_MARKER",["description"]="PRIVATE_CALENDAR_SOURCE_MARKER",
                ["exercises"]=new JsonArray("PRIVATE_CALENDAR_SOURCE_MARKER"),
                ["published_snapshot"]=new JsonObject{["title"]="Approved frozen training",["date"]="2026-10-30",["start_time"]="17:00",["venue"]="Published venue",["member_summary"]="Approved summary",["session_journey"]="Approved journey",["preparation"]="Approved preparation",["notes"]="PRIVATE_CALENDAR_SOURCE_MARKER"}
            });
            input["training_sessions"]!.AsArray().Add(new JsonObject{["id"]=Id("draft"),["title"]="Unpublished draft training",["date"]="2026-10-21",["academy_group_id"]=accounts.GroupId,["content_status"]="draft",["notes"]="PRIVATE_CALENDAR_SOURCE_MARKER"});
            input["availability"]!.AsArray().Add(new JsonObject{["event_id"]=Id("shared"),["event_type"]="fixture",["player_id"]=accounts.PlayerId,["status"]="going",["updated_at"]="2026-10-12T09:30:00.1234567Z",["submitted_by"]="PRIVATE_CALENDAR_SOURCE_MARKER"});
            input["event_attendance"]!.AsArray().Add(new JsonObject{["event_id"]=Id("host"),["player_id"]=accounts.ChildId,["status"]="no_response",["marked_at"]="2026-10-20T09:30:00.1234567Z"});
            input["attendance"]!.AsArray().Add(new JsonObject{["event_id"]=Id("host"),["player_id"]=accounts.ChildId,["status"]="present"});
        }
        var manifest=CalendarImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(input,new JsonSerializerOptions{WriteIndented=true}));
        var plan=CalendarImportPlanner.Build(manifest,new(accounts.Roster.Digest,AccountImportFixture.Commit,accounts.Roster));
        return new(){Accounts=accounts,Input=input,Manifest=manifest,Plan=plan};
    }
}
