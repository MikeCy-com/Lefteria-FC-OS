using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class AccountImportFixture
{
    public const string Commit="c83fd6dfa798cfaada8ae3483f7c0148b245b89c";
    public required string Prefix{get;init;}
    public required PhoneNumber PlayerPhone{get;init;}
    public required PhoneNumber ParentPhone{get;init;}
    public required PhoneNumber StaffPhone{get;init;}
    public required PhoneNumber EvidencePhone{get;init;}
    public required PhoneNumber InactivePhone{get;init;}
    public required PhoneNumber StalePhone{get;init;}
    public required PhoneNumber ConflictPhone{get;init;}
    public required JsonObject Input{get;init;}
    public required ValidatedRosterImport Roster{get;init;}
    public required ValidatedStaffRelationshipImport Staff{get;init;}
    public required ValidatedAccountImport Manifest{get;init;}
    public required AccountImportPlan Plan{get;init;}
    public string TeamId=>Prefix+"-team";
    public string GroupId=>Prefix+"-group";
    public string PlayerId=>Prefix+"-player";
    public string ChildId=>Prefix+"-child";
    public string StaffId=>Prefix+"-staff";
    public string[] Phones=>[PlayerPhone.Digits,ParentPhone.Digits,StaffPhone.Digits,EvidencePhone.Digits,InactivePhone.Digits,StalePhone.Digits,ConflictPhone.Digits];
    public override string ToString()=>"Synthetic protected account fixture";
    public static AccountImportFixture Create(TestDatabase database,bool calendarScenario=false)
    {
        var prefix="account-e2e-"+Guid.NewGuid().ToString("N");
        var player=database.NewPhone();var parent=database.NewPhone();var staffPhone=database.NewPhone();
        var evidence=database.NewPhone();var inactive=database.NewPhone();var stale=database.NewPhone();var conflict=database.NewPhone();
        JsonObject Header(params string[] collections)=>new()
        {
            ["schema_version"]=1,["source_repository"]="MikeCy-com/Lefteria-FC",["source_commit"]=Commit,
            ["complete_collections"]=new JsonArray(collections.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())
        };
        var rosterInput=Header("teams","academy_groups","players");
        rosterInput["teams"]=new JsonArray(new JsonObject{["id"]=prefix+"-team",["name"]="Fictional account team"});
        rosterInput["academy_groups"]=new JsonArray(new JsonObject{["id"]=prefix+"-group",["name"]="Fictional account group",["coach_id"]=prefix+"-staff"});
        rosterInput["players"]=new JsonArray(
            new JsonObject{["id"]=prefix+"-player",["name"]="Fictional own player",["number"]=7,["position"]="Midfielder",["team_type"]="First Team",["is_active"]=true,["team_id"]=prefix+"-team",["phone"]=player.WireValue,["quick_notes"]="owner-only-account-marker"},
            new JsonObject{["id"]=prefix+"-child",["name"]="Fictional child",["number"]=3,["position"]="Forward",["team_type"]="Academy",["is_active"]=true,["academy_group_id"]=prefix+"-group",["parent_phone"]=parent.WireValue,["parent_email"]="private-guardian@example.invalid"});
        if(calendarScenario)
        {
            rosterInput["academy_groups"]!.AsArray().Add(new JsonObject{["id"]=prefix+"-group-two",["name"]="Fictional second calendar group"});
            rosterInput["players"]!.AsArray().Add(new JsonObject{["id"]=prefix+"-child-two",["name"]="Fictional second child",["number"]=4,["position"]="Defender",["team_type"]="Academy",["is_active"]=true,["academy_group_id"]=prefix+"-group-two",["parent_phone"]=parent.WireValue});
            rosterInput["players"]!.AsArray().Add(new JsonObject{["id"]=prefix+"-child-fc",["name"]="Fictional FC child",["number"]=8,["position"]="Goalkeeper",["team_type"]="First Team",["is_active"]=true,["team_id"]=prefix+"-team",["parent_phone"]=parent.WireValue});
            // Group one plus an invited second-group child produces exactly 201
            // response rows without granting any extra phone or mobile identity.
            for(var index=0;index<199;index++)rosterInput["players"]!.AsArray().Add(new JsonObject{["id"]=prefix+"-peer-"+index.ToString("D3"),["name"]="Fictional roster peer "+index.ToString("D3"),["number"]=index+10,["position"]="Midfielder",["team_type"]="Academy",["is_active"]=true,["academy_group_id"]=prefix+"-group"});
        }
        var roster=RosterImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(rosterInput));
        var staffInput=Header("staff");staffInput["roster_sha256"]=roster.Digest;
        staffInput["staff"]=new JsonArray(new JsonObject{["id"]=prefix+"-staff",["name"]="Fictional shared coach",["role"]="Head Coach",["phone"]=staffPhone.WireValue,
            ["is_active"]=true,["team_id"]=prefix+"-team",["academy_group_id"]=prefix+"-group",["admin_user_id"]=prefix+"-admin",["email"]="private-staff@example.invalid"});
        var staff=StaffRelationshipImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(staffInput));
        var input=Header("mobile_users","fans","admin_users");input["roster_sha256"]=roster.Digest;input["staff_sha256"]=staff.Digest;
        input["exported_at"]="2026-10-26T12:00:00.1234567Z";input["mobile_users"]=new JsonArray();
        foreach(var role in new[]{"fan","player","parent","coach","management"})
        {
            var row=new JsonObject{["id"]=prefix+"-mobile-"+role,["phone"]=(role is "fan" or "player"?player:role=="parent"?parent:staffPhone).WireValue,
                ["role"]=role,["name"]="Imported "+role,["created_at"]="2026-10-24T00:00:00.1234567Z",["updated_at"]="2026-10-25T01:02:03.7654321Z",
                ["is_active"]=true,["social_principal_id"]="private-principal-marker"};
            if(role=="player")row["linked_player_id"]=prefix+"-player";
            if(role=="parent"){row["linked_player_id"]=prefix+"-child";row["linked_player_ids"]=calendarScenario?new JsonArray(prefix+"-child",prefix+"-child-two",prefix+"-child-fc"):new JsonArray(prefix+"-child");}
            if(role=="coach")row["linked_staff_id"]=prefix+"-staff";
            if(role=="management")row["linked_admin_id"]=prefix+"-admin";
            input["mobile_users"]!.AsArray().Add(row);
        }
        foreach(var suffix in new[]{"a","b"})input["mobile_users"]!.AsArray().Add(new JsonObject{["id"]=prefix+"-conflict-"+suffix,["phone"]=conflict.WireValue,["role"]="fan",["name"]="Quarantined duplicate"});
        JsonObject Fan(string id,PhoneNumber phone,bool active)=>new(){["id"]=prefix+id,["phone"]=phone.WireValue,["first_name"]="Evidence",["last_name"]="Supporter",["birthday"]="1990-01-02",
            ["is_active"]=active,["avatar_url"]="/api/uploads/avatars/legacy-fixture.jpg",["favourite_player_id"]=prefix+"-player",["notification_prefs"]=new JsonObject{["club_news"]=false}};
        input["fans"]=new JsonArray(Fan("-fan-mirror",player,true),Fan("-fan-evidence",evidence,true),Fan("-fan-inactive",inactive,false),Fan("-fan-stale",stale,true));
        input["admin_users"]=new JsonArray(new JsonObject{["id"]=prefix+"-admin",["username"]=prefix+"-user",["name"]="Fictional imported management",["role"]="manager",["phone"]=staffPhone.WireValue,
            ["password_hash"]="$2b$12$"+new string('A',53),["organization_ids"]=new JsonArray(OrganizationIds.Club,OrganizationIds.Academy),["active_organization_id"]=OrganizationIds.Club,["is_active"]=true});
        var manifest=AccountImportValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(input));
        var plan=AccountImportPlanner.Build(manifest,new(roster.Digest,staff.Digest,Commit,roster,staff));
        return new(){Prefix=prefix,PlayerPhone=player,ParentPhone=parent,StaffPhone=staffPhone,EvidencePhone=evidence,InactivePhone=inactive,StalePhone=stale,ConflictPhone=conflict,
            Input=input,Roster=roster,Staff=staff,Manifest=manifest,Plan=plan};
    }
}
