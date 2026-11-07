using System.Net.Mail;
using System.Text.RegularExpressions;

namespace LefteriaFc.Application.Marketing;

// Pure input policy only. No endpoint, persistence, identity or SMS provider
// is enabled until the separate audited PostgreSQL lead workflow is verified.
public sealed record AcademyLeadInput(
    string? Name,string? Phone,string? Email,int? PlayerAge,
    string? InterestedIn,string? Message,string? PartnerSlug,
    bool ConsentGdpr,bool ConsentMarketing);

public sealed record ValidatedAcademyLead(
    string Name,string? Phone,string? Email,int? PlayerAge,
    string? InterestedIn,string? Message,string? PartnerSlug,
    string Source,bool ConsentGdpr,bool ConsentMarketing);

public static partial class AcademyLeadPolicy
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex PartnerSlugPattern();

    // No user-controlled status, organization, admin identity or campaign ID.
    // Reject on bad input before touching any durable record or external provider.
    public static bool TryValidate(
        AcademyLeadInput input,out ValidatedAcademyLead? lead,out string? error)
    {
        lead=null;
        error=null;
        var name=input.Name?.Trim() ?? "";
        var phone=Optional(input.Phone);
        var email=Optional(input.Email);
        var interest=Optional(input.InterestedIn);
        var message=Optional(input.Message);
        var partner=Optional(input.PartnerSlug);

        if(name.Length is < 1 or > 120 || name.Any(char.IsControl))
            return Reject("invalid_name",out error);
        if(phone is null && email is null)
            return Reject("contact_required",out error);
        if(phone is not null && (phone.Length is < 7 or > 32 ||
            phone.Any(c=>!char.IsAsciiDigit(c) && c is not ('+' or '-' or ' ' or '(' or ')')) ||
            phone.Count(char.IsAsciiDigit)<7 || phone.Count(c=>c=='+')>1 ||
            (phone.Contains('+') && !phone.StartsWith('+'))))
            return Reject("invalid_phone",out error);
        if(email is not null && (email.Length>254 ||
            email.Any(char.IsControl) || email.Any(char.IsWhiteSpace) ||
            !MailAddress.TryCreate(email,out var parsed) ||
            !string.Equals(parsed.Address,email,StringComparison.Ordinal)))
            return Reject("invalid_email",out error);
        if(input.PlayerAge is < 4 or > 99)
            return Reject("invalid_player_age",out error);
        if(interest is not null && (interest.Length>120 || interest.Any(char.IsControl)))
            return Reject("invalid_interest",out error);
        if(message is not null && (message.Length>2000 ||
            message.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t'))))
            return Reject("invalid_message",out error);
        if(partner is not null && !PartnerSlugPattern().IsMatch(partner))
            return Reject("invalid_partner",out error);
        if(!input.ConsentGdpr)
            return Reject("consent_required",out error);

        lead=new(name,phone,email,input.PlayerAge,interest,message,partner,
            partner is null?"website":"partner_referral",true,input.ConsentMarketing);
        return true;
    }

    private static string? Optional(string? input)
    {
        var value=input?.Trim();
        return string.IsNullOrEmpty(value)?null:value;
    }

    private static bool Reject(string reason,out string? error)
    {
        error=reason;
        return false;
    }
}
