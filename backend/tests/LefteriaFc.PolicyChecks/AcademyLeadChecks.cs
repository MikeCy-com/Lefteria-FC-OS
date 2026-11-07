using LefteriaFc.Application.Marketing;

namespace LefteriaFc.PolicyChecks;

internal static class AcademyLeadChecks
{
    private static readonly AcademyLeadInput Valid=new(
        "  Parent Example  "," +357 99 000000 "," parent@example.test ",9,
        "Ακαδημία U10-U12","Ενδιαφέρον για δοκιμαστικό","ts-international-soccer-academy",
        true,false);

    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("Academy lead policy preserves trimmed source fields and opt-in",()=>{
            Require(AcademyLeadPolicy.TryValidate(Valid,out var lead,out var error));
            Require(error is null);
            if(lead is null)throw new InvalidOperationException("Validation returned no lead.");
            Require(lead.Name=="Parent Example");
            Require(lead.Phone=="+357 99 000000");
            Require(lead.Email=="parent@example.test");
            Require(lead.Source=="partner_referral");
            Require(lead.PartnerSlug=="ts-international-soccer-academy");
            Require(lead.ConsentGdpr && !lead.ConsentMarketing);
        });
        yield return ("Academy lead without referral is website-sourced",()=>{
            Require(AcademyLeadPolicy.TryValidate(
                Valid with {PartnerSlug=null,ConsentMarketing=true},
                out var lead,out _));
            Require(lead is { Source:"website",PartnerSlug:null,ConsentMarketing:true });
        });
        yield return ("Academy lead requires name, contact and GDPR consent",()=>{
            Invalid(Valid with {Name="  "},"invalid_name");
            Invalid(Valid with {Name=new string('a',121)},"invalid_name");
            Invalid(Valid with {Phone=null,Email=null},"contact_required");
            Invalid(Valid with {Phone=" ",Email=" "},"contact_required");
            Invalid(Valid with {ConsentGdpr=false},"consent_required");
        });
        yield return ("Academy lead denies malformed contact and unsafe ages",()=>{
            Invalid(Valid with {Phone="99999"},"invalid_phone");
            Invalid(Valid with {Phone="++357 99000000"},"invalid_phone");
            Invalid(Valid with {Email="bad @example.test"},"invalid_email");
            Invalid(Valid with {PlayerAge=3},"invalid_player_age");
            Invalid(Valid with {PlayerAge=100},"invalid_player_age");
        });
        yield return ("Academy lead rejects overlong and hostile metadata",()=>{
            Invalid(Valid with {InterestedIn=new string('x',121)},"invalid_interest");
            Invalid(Valid with {Message=new string('x',2001)},"invalid_message");
            Invalid(Valid with {PartnerSlug="../other"},"invalid_partner");
            Invalid(Valid with {PartnerSlug="A"},"invalid_partner");
            Invalid(Valid with {PartnerSlug=new string('x',65)},"invalid_partner");
        });
        yield return ("Marketing opt-in remains separate from required GDPR consent",()=>{
            Invalid(Valid with {ConsentGdpr=false,ConsentMarketing=true},"consent_required");
        });
    }

    private static void Invalid(AcademyLeadInput input,string expected)
    {
        Require(!AcademyLeadPolicy.TryValidate(input,out var lead,out var error));
        Require(lead is null && error==expected);
    }

    private static void Require(bool value)
    {
        if(!value)throw new InvalidOperationException("Academy lead policy contract failed.");
    }
}
