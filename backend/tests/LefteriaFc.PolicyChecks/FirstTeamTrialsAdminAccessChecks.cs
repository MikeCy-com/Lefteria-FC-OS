using LefteriaFc.Application.Trials;
using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.PolicyChecks;

internal static class FirstTeamTrialsAdminAccessChecks
{
    private static WebAdminIdentityDto User(string role,params string[] orgs)=>
        new(Guid.NewGuid(),"fixture","Fixture",role,Array.Empty<string>(),
            orgs.Select(id=>new WebAdminOrganizationDto(id,id)).ToArray());

    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("First-team trials admin view requires exact FC assignment and source read role",()=>{
            Require(FirstTeamTrialsAdminAccess.CanRead(User("manager","lefteria_fc"),"lefteria_fc"));
            Require(FirstTeamTrialsAdminAccess.CanRead(User("editor","lefteria_fc"),"lefteria_fc"));
            Require(FirstTeamTrialsAdminAccess.CanRead(User("super_admin","lefteria_fc"),"lefteria_fc"));
        });
        yield return ("Academy or unassigned staff cannot read FC trial admin view",()=>{
            foreach(var role in new[]{"super_admin","manager","editor","marketing","coach"})
            {
                Require(!FirstTeamTrialsAdminAccess.CanRead(User(role,"lefteria_fc_academy"),"lefteria_fc"));
                Require(!FirstTeamTrialsAdminAccess.CanRead(User(role,"lefteria_fc"),"lefteria_fc_academy"));
                Require(!FirstTeamTrialsAdminAccess.CanRead(User(role),"lefteria_fc"));
            }
        });
        yield return ("First-team read rejects unsupported roles and anonymous sessions",()=>{
            Require(!FirstTeamTrialsAdminAccess.CanRead(null,"lefteria_fc"));
            Require(!FirstTeamTrialsAdminAccess.CanRead(User("unknown","lefteria_fc"),"lefteria_fc"));
            Require(!FirstTeamTrialsAdminAccess.CanRead(User("coach","lefteria_fc"),"lefteria_fc"));
            Require(!FirstTeamTrialsAdminAccess.CanRead(User("marketing","lefteria_fc"),"lefteria_fc"));
            Require(!FirstTeamTrialsAdminAccess.CanRead(User("manager","lefteria_fc"),null));
        });
        yield return ("No first-team trial settings editor is authorized by read permissions",()=>{
            Require(!FirstTeamTrialsAdminAccess.CanEdit(User("manager","lefteria_fc"),"lefteria_fc"));
            Require(!FirstTeamTrialsAdminAccess.CanEdit(User("super_admin","lefteria_fc"),"lefteria_fc"));
        });
    }
    private static void Require(bool ok)
    {
        if(!ok)throw new InvalidOperationException("First-team trials administrative authorization failed.");
    }
}
