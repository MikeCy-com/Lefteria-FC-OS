using LefteriaFc.Application.Trials;

namespace LefteriaFc.PolicyChecks;

internal static class FirstTeamTrialsSettingsChecks
{
    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("First-team trial defaults are source-grounded and closed",()=>{
            var initial=FirstTeamTrialsSettingsPolicy.Default;
            Require(!initial.Open);
            Require(initial.Headline=="Ετοιμος να Ξεκινησεις το Ταξιδι σου;");
            Require(initial.Subtitle.Contains("LEFTERIA FC",StringComparison.Ordinal));
            Require(initial.ButtonText=="Δηλωσε Συμμετοχη για Δοκιμαστικα");
            Require(initial.ClosedMessage=="Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές. Παρακαλούμε δοκιμάστε ξανά σύντομα.");
            Require(FirstTeamTrialsSettingsPolicy.TryValidate(initial,out var error)&&error is null);
        });
        yield return ("First-team trial settings reject blank and unsafe content",()=>{
            var initial=FirstTeamTrialsSettingsPolicy.Default;
            Reject(null,"settings_required");
            Reject(initial with {Headline="  "},"invalid_headline");
            Reject(initial with {Headline=" Admin"},"invalid_headline");
            Reject(initial with {Headline=new string('x',181)},"invalid_headline");
            Reject(initial with {Subtitle=new string('x',501)},"invalid_subtitle");
            Reject(initial with {ButtonText=new string('x',101)},"invalid_button");
            Reject(initial with {ClosedMessage=new string('x',501)},"invalid_closed_message");
            Reject(initial with {ClosedMessage="Unsafe\0content"},"invalid_closed_message");
        });
        yield return ("A settings value never directly grants trial submission authority",()=>{
            var candidate=FirstTeamTrialsSettingsPolicy.Default with {Open=true};
            Require(FirstTeamTrialsSettingsPolicy.TryValidate(candidate,out _));
            // The settings record is presentation-only. It has no persistence
            // interface, consent waiver or form submission capability.
            Require(typeof(FirstTeamTrialsSettings).GetProperties().Length==5);
        });
    }

    private static void Reject(FirstTeamTrialsSettings? settings,string reason)
    {
        Require(!FirstTeamTrialsSettingsPolicy.TryValidate(settings,out var error));
        Require(error==reason);
    }
    private static void Require(bool value)
    {
        if(!value)throw new InvalidOperationException("First-team trial settings policy failed.");
    }
}
