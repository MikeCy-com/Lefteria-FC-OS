using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LefteriaFc.Application.AccountImport;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.StaffRelationships;
using LefteriaFc.Domain.Organizations;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static Task AccountPlannerAdditionalAsync()
    {
        var (manifest, context) = AdditionalAccountFixture();
        AccountImportPlan Plan(JsonObject input) => AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(input)), context);
        foreach (var status in new[] { "Disabled", " INACTIVE ", "sUsPeNdEd" })
        {
            var disabled = manifest.DeepClone().AsObject(); disabled["mobile_users"]![0]!["account_status"] = status;
            Check.True(!Plan(disabled).Mobiles.Single().IsActive, "Revocation status normalization activated an account.");
        }
        foreach (var status in new[] { "pending_review", "   " })
        {
            var unknown = manifest.DeepClone().AsObject(); unknown["mobile_users"]![0]!["account_status"] = status;
            Check.True(Plan(unknown).Issues.Any(row => row.Code == "account_status_unknown"), "Unknown status silently became active.");
        }
        var malformed = manifest.DeepClone().AsObject(); malformed["mobile_users"]![0]!["linked_staff_id"] = 42;
        Check.Equal(0, Plan(malformed).Mobiles.Count, "An irrelevant malformed known link field bypassed shape validation.");
        var invalidBoundary = manifest.DeepClone().AsObject(); invalidBoundary["exported_at"] = "October 26 2026 00:00:00Z";
        try { AccountImportValidator.Validate(RosterBytes(invalidBoundary)); throw new InvalidOperationException("Non-ISO manifest boundary was accepted."); }
        catch (RosterImportValidationException) { }
        var future = manifest.DeepClone().AsObject(); future["mobile_users"]![0]!["birthday"] = "2099-01-01";
        Check.True(Plan(future).Issues.Any(row => row.Code == "birthday_future"), "Future profile date was accepted.");

        var blankName = manifest.DeepClone().AsObject(); blankName["admin_users"]![0]!["name"] = "";
        Check.Equal("", Plan(blankName).Admins.Single().Name, "Source-supported blank admin name was quarantined or replaced.");
        blankName["admin_users"]![0]!.AsObject().Remove("name"); blankName["admin_users"]![0]!["invite_pending"] = true; blankName["admin_users"]![0]!["password_hash"] = null;
        Check.True(Plan(blankName).Admins.Single() is { Name:"", IsActive:false, InvitePending:true }, "Unnamed pending admin was activated or discarded.");
        var disabledCredential=manifest.DeepClone().AsObject();disabledCredential["admin_users"]![0]!["is_active"]=false;
        Check.True(!Plan(disabledCredential).Admins.Single().IsActive,"A valid disabled credential was reactivated.");
        disabledCredential["admin_users"]![0]!["password_hash"]=null;
        Check.True(!Plan(disabledCredential).Admins.Single().IsActive&&Plan(disabledCredential).Admins.Single().PasswordHash is null,"An explicitly disabled credentialless historical actor was lost or activated.");
        disabledCredential["admin_users"]![0]!.AsObject().Remove("is_active");
        Check.True(Plan(disabledCredential).Issues.Any(row=>row.Code=="active_admin_credential_missing"),"Missing active status plus null credentials activated an account.");
        var staleAssignment = manifest.DeepClone().AsObject(); staleAssignment["admin_users"]![0]!["assigned_team_id"] = "missing-team";
        Check.True(Plan(staleAssignment).Issues.Any(row => row.Code == "admin_team_reference"), "Explicit grants hid a dangling assignment FK.");
        var scope = manifest.DeepClone().AsObject(); var admin = scope["admin_users"]![0]!;
        admin["organization_ids"] = ""; admin["organization_id"] = OrganizationIds.Academy;
        Check.Equal(0, Plan(scope).Admins.Single().Grants.Count, "Explicit empty plural assignment fell through to singular.");
        admin["organization_ids"] = OrganizationIds.Club;
        Check.Equal(OrganizationIds.Club, Plan(scope).Admins.Single().Grants.Single().OrganizationId, "Plural string precedence changed.");
        admin["organization_ids"] = null; admin["organization_id"] = OrganizationIds.Academy;
        Check.Equal(OrganizationIds.Academy, Plan(scope).Admins.Single().Grants.Single().OrganizationId, "Null plural did not fall back to singular.");
        admin.AsObject().Remove("organization_ids"); admin.AsObject().Remove("organization_id"); admin["role"] = "coach";
        admin["assigned_academy_group_id"] = "extra-group"; admin["assigned_team_id"] = "extra-team";
        Check.Equal("coach_group_backfill", Plan(scope).Admins.Single().Grants.Single().Provenance, "Coach backfill did not prefer validated Academy group.");

        var mirror = manifest.DeepClone().AsObject(); var own = mirror["mobile_users"]![0]!;
        own["first_name"] = "Own"; own["last_name"] = ""; own["birthday"] = "1991-01-01"; own["notification_prefs"] = new JsonObject();
        own["favorite_team"] = "academy"; own["email"] = "mobile@example.invalid";
        var mirrored = Plan(mirror).Mobiles.Single();
        Check.True(mirrored.LastName == "" && mirrored.FavouritePlayerId is null && mirrored.NotificationPreferencesJson == "{}", "Fan mirror ran outside its birthday/first-name gate.");
        own["birthday"] = null; mirrored = Plan(mirror).Mobiles.Single();
        Check.True(mirrored.FirstName == "Own" && mirrored.LastName == "Evidence" && mirrored.FavouritePlayerId == "extra-player" &&
            mirrored.Email == "mobile@example.invalid" && mirrored.FavoriteTeam == "academy", "Narrow fan fallback overwrote mobile-owned values.");

        var management = manifest.DeepClone().AsObject(); management["mobile_users"] = new JsonArray(AdditionalMobile("management"));
        var competing = AdditionalAdmin("extra-admin-b", "different-admin"); competing["password_hash"] = "malformed";
        management["admin_users"]!.AsArray().Add(competing);
        var ambiguous = Plan(management);
        Check.True(!ambiguous.Mobiles.Any() && ambiguous.Issues.Any(row => row.Code == "management_admin_ambiguous"), "Quarantined competitor erased source phone ambiguity.");
        management["mobile_users"]![0]!["linked_admin_id"] = "extra-admin";
        Check.Equal(1, Plan(management).Mobiles.Count, "Consistent explicit admin pointer lost precedence over phone-only ambiguity.");
        foreach(var raw in new[]{"35799123456","35799123456letters"})
        {
            var rawCompetitor=manifest.DeepClone().AsObject();rawCompetitor["mobile_users"]=new JsonArray(AdditionalMobile("management"));
            var invalidPeer=AdditionalAdmin("raw-admin","raw-user");invalidPeer["phone"]=raw;rawCompetitor["admin_users"]!.AsArray().Add(invalidPeer);
            Check.True(!Plan(rawCompetitor).Mobiles.Any(),"Target phone rejection erased an ambiguous source-normalized admin candidate.");
            rawCompetitor["mobile_users"]![0]!["linked_admin_id"]="extra-admin";
            Check.Equal(1,Plan(rawCompetitor).Mobiles.Count,"Source ambiguity denied a strong canonical admin pointer.");
        }
        foreach(var digit in new[]{"\u0660","\U0001D7CE"})
        {
            var unicodePeer=manifest.DeepClone().AsObject();unicodePeer["mobile_users"]=new JsonArray(AdditionalMobile("management"));
            var invalidPeer=AdditionalAdmin("unicode-admin","unicode-user");invalidPeer["phone"]="35799123456"+digit;unicodePeer["admin_users"]!.AsArray().Add(invalidPeer);
            Check.Equal(1,Plan(unicodePeer).Mobiles.Count,"A retained source Unicode Nd digit was dropped into a different ASCII phone.");
        }
        var newerUnicode=manifest.DeepClone().AsObject();newerUnicode["mobile_users"]=new JsonArray(AdditionalMobile("management"));
        var newerPeer=AdditionalAdmin("newer-unicode-admin","newer-unicode-user");newerPeer["phone"]="35799123456\U00011F50";newerUnicode["admin_users"]!.AsArray().Add(newerPeer);
        Check.Equal(0,Plan(newerUnicode).Mobiles.Count,"Newer runtime Unicode tables hid an ASCII-comparable Python 3.11 candidate.");

        var (explicitManifest, explicitContext) = AdditionalAccountFixture(duplicateContacts:true);
        AccountImportPlan ExplicitPlan(JsonObject input) => AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(input)), explicitContext);
        explicitManifest["mobile_users"] = new JsonArray(AdditionalMobile("player"));
        Check.Equal(0, ExplicitPlan(explicitManifest).Mobiles.Count, "Ambiguous own-phone players selected a first winner.");
        explicitManifest["mobile_users"]![0]!["linked_player_id"] = "extra-player";
        Check.Equal("extra-player", ExplicitPlan(explicitManifest).Links.Single().RecordId, "Verified explicit player pointer lost precedence.");
        var (rawPlayerManifest,rawPlayerContext)=AdditionalAccountFixture(secondPlayerPhone:"35799123456letters");
        rawPlayerManifest["mobile_users"]=new JsonArray(AdditionalMobile("player"));
        AccountImportPlan RawPlayerPlan()=>AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(rawPlayerManifest)),rawPlayerContext);
        Check.Equal(0,RawPlayerPlan().Mobiles.Count,"Rejected own-player phone erased a source-comparable competitor.");
        rawPlayerManifest["mobile_users"]![0]!["linked_player_id"]="extra-player";
        Check.Equal("extra-player",RawPlayerPlan().Links.Single().RecordId,"Strong own-player pointer lost precedence over an unsafe raw-contact peer.");
        explicitManifest["mobile_users"] = new JsonArray(AdditionalMobile("coach"));
        Check.Equal(0, ExplicitPlan(explicitManifest).Mobiles.Count, "Ambiguous staff phone selected a first winner.");
        explicitManifest["mobile_users"]![0]!["linked_staff_id"] = "extra-staff";
        Check.Equal("extra-staff", ExplicitPlan(explicitManifest).Links.Single().RecordId, "Verified explicit staff pointer lost precedence.");
        var(rawStaffManifest,rawStaffContext)=AdditionalAccountFixture(secondStaffPhone:"35799123456letters");
        rawStaffManifest["mobile_users"]=new JsonArray(AdditionalMobile("coach"));
        AccountImportPlan RawStaffPlan()=>AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(rawStaffManifest)),rawStaffContext);
        Check.Equal(0,RawStaffPlan().Mobiles.Count,"Rejected staff contact erased source phone ambiguity.");
        rawStaffManifest["mobile_users"]![0]!["linked_staff_id"]="extra-staff";
        Check.Equal(1,RawStaffPlan().Mobiles.Count,"Strong staff pointer lost precedence over invalid raw-contact peer.");
        var(inactiveManifest,inactiveContext)=AdditionalAccountFixture(inactiveStaff:true);
        inactiveManifest["mobile_users"]=new JsonArray(AdditionalMobile("coach"));inactiveManifest["mobile_users"]![0]!["linked_staff_id"]="extra-staff";
        Check.True(AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(inactiveManifest)),inactiveContext).Mobiles.Single().IsActive,
            "Current roster lifecycle was flattened into independent source mobile status.");
        var (reverseManifest, reverseContext) = AdditionalAccountFixture(reverseAdmin:"extra-admin");
        reverseManifest["admin_users"]!.AsArray().Add(AdditionalAdmin("extra-admin-b", "another-admin"));
        reverseManifest["admin_users"]![1]!["staff_id"] = "extra-staff";
        reverseManifest["mobile_users"] = new JsonArray(AdditionalMobile("management")); reverseManifest["mobile_users"]![0]!["linked_admin_id"] = "extra-admin";
        var reversePlan = AccountImportPlanner.Build(AccountImportValidator.Validate(RosterBytes(reverseManifest)), reverseContext);
        Check.True(reversePlan.Admins.Count == 0 && reversePlan.Mobiles.Count == 0 && reversePlan.Issues.Count(row => row.Code == "staff_admin_link_conflict") == 2,
            "Contradictory reverse staff-admin links failed to quarantine every competitor and dependent management subject.");

        // Thousands of unrelated fan pairs exercise indexed fallback, rather
        // than a full source scan for each mobile identity. No timing threshold.
        var many = manifest.DeepClone().AsObject(); many["mobile_users"] = new JsonArray(); many["fans"] = new JsonArray(); many["admin_users"] = new JsonArray();
        for (var index = 0; index < 5000; index++)
        {
            var phone = "+357" + (90000000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            many["mobile_users"]!.AsArray().Add(new JsonObject { ["id"] = "many-mobile-"+index, ["phone"] = phone, ["role"] = "fan", ["name"] = "Own" });
            many["fans"]!.AsArray().Add(new JsonObject { ["id"] = "many-fan-"+index, ["phone"] = phone, ["first_name"] = "Mirror", ["last_name"] = "Evidence", ["birthday"] = "1990-01-01" });
        }
        var largePlan = Plan(many);
        Check.True(largePlan.QuarantinedRows == 0 && largePlan.Mobiles.Count == 5000 && largePlan.Mobiles.All(row => row.FirstName == "Mirror"), "Indexed fan fallback lost or crossed unrelated identities.");
        var bytes = AccountImportValidator.Validate(RosterBytes(manifest)); var copy = bytes.CopySourceBytes(); copy[0] = 0;
        Check.True(bytes.CopySourceBytes()[0] == (byte)'{', "Manifest source-byte copy exposed mutable archive state.");
        return Task.CompletedTask;
    }

    private static (JsonObject Manifest, AccountImportTrustedContext Context) AdditionalAccountFixture(bool duplicateContacts=false, string? reverseAdmin=null,
        string? secondStaffPhone=null,bool inactiveStaff=false,string? secondPlayerPhone=null)
    {
        const string commit = "2986c570458a3990c6d9d1b7aca1d1fe6d7a3c30";
        var rosterManifest = new JsonObject
        {
            ["schema_version"] = 1, ["source_repository"] = "MikeCy-com/Lefteria-FC", ["source_commit"] = commit,
            ["complete_collections"] = new JsonArray("teams","academy_groups","players"),
            ["teams"] = new JsonArray(new JsonObject { ["id"] = "extra-team", ["name"] = "Team" }),
            ["academy_groups"] = new JsonArray(new JsonObject { ["id"] = "extra-group", ["name"] = "Group", ["coach_id"] = "extra-staff" }),
            ["players"] = new JsonArray(new JsonObject { ["id"] = "extra-player", ["name"] = "Player", ["number"] = 1, ["position"] = "Forward", ["phone"] = "+35799123456", ["team_id"] = "extra-team" })
        };
        if (duplicateContacts||secondPlayerPhone is not null) rosterManifest["players"]!.AsArray().Add(new JsonObject { ["id"] = "extra-player-b", ["name"] = "Other", ["number"] = 2, ["position"] = "Forward", ["phone"] = secondPlayerPhone??"+35799123456" });
        var roster = RosterImportValidator.Validate(RosterBytes(rosterManifest));
        var staffManifest = new JsonObject
        {
            ["schema_version"] = 1, ["source_repository"] = "MikeCy-com/Lefteria-FC", ["source_commit"] = commit, ["roster_sha256"] = roster.Digest,
            ["complete_collections"] = new JsonArray("staff"), ["staff"] = new JsonArray(new JsonObject { ["id"] = "extra-staff", ["name"] = "Staff", ["role"] = "Head Coach", ["phone"] = "+35799123456", ["team_id"] = "extra-team", ["admin_user_id"] = reverseAdmin })
        };
        if(inactiveStaff)staffManifest["staff"]![0]!["is_active"]=false;
        if (duplicateContacts||secondStaffPhone is not null) staffManifest["staff"]!.AsArray().Add(new JsonObject { ["id"] = "extra-staff-b", ["name"] = "Other", ["role"] = "Youth Coach", ["phone"] = secondStaffPhone??"+35799123456" });
        var staff = StaffRelationshipImportValidator.Validate(RosterBytes(staffManifest));
        var manifest = new JsonObject
        {
            ["schema_version"] = 1, ["source_repository"] = "MikeCy-com/Lefteria-FC", ["source_commit"] = commit,
            ["roster_sha256"] = roster.Digest, ["staff_sha256"] = staff.Digest, ["exported_at"] = "2026-10-26T00:00:00Z",
            ["complete_collections"] = new JsonArray("mobile_users","fans","admin_users"), ["mobile_users"] = new JsonArray(AdditionalMobile("fan")),
            ["fans"] = new JsonArray(new JsonObject { ["id"] = "extra-fan", ["phone"] = "+35799123456", ["first_name"] = "Mirror", ["last_name"] = "Evidence", ["birthday"] = "1990-01-01", ["favourite_player_id"] = "extra-player", ["notification_prefs"] = new JsonObject { ["match"] = true } }),
            ["admin_users"] = new JsonArray(AdditionalAdmin("extra-admin","extra-user"))
        };
        return (manifest, new(roster.Digest, staff.Digest, commit, roster, staff));
    }
    private static JsonObject AdditionalMobile(string role) => new() { ["id"] = "extra-mobile-"+role, ["phone"] = "+35799123456", ["role"] = role, ["name"] = "Own" };
    private static JsonObject AdditionalAdmin(string id, string username) => new()
    {
        ["id"] = id, ["username"] = username, ["name"] = "Admin", ["role"] = "manager", ["phone"] = "+35799123456",
        ["password_hash"] = "$2b$12$"+new string('A',53), ["organization_ids"] = new JsonArray(OrganizationIds.Club)
    };
}
