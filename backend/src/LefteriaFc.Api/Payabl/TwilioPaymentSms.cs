using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.Payabl;

/// <summary>
/// Explicit opt-in provider for existing, OTP-verified guardian phones only.
/// No payment links are ever sent without an admin action and an outbox
/// reservation. Acceptance by Twilio is NOT proof of handset delivery.
/// </summary>
public sealed class TwilioPaymentSmsOptions
{
    public bool Enabled { get; set; }
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string MessagingServiceSid { get; set; } = "";

    public static bool Safe(TwilioPaymentSmsOptions options) =>
        !options.Enabled || (
        Regex.IsMatch(options.AccountSid,@"\AAC[0-9a-fA-F]{32}\z",
            RegexOptions.CultureInvariant) &&
        options.AuthToken.Length is >= 20 and <= 128 &&
        options.AuthToken.All(c => c is >= '!' and <= '~') &&
        Regex.IsMatch(options.MessagingServiceSid,@"\AMG[0-9a-fA-F]{32}\z",
            RegexOptions.CultureInvariant));
}

public sealed class TwilioPaymentSmsUnavailableException :
    Exception
{
    public TwilioPaymentSmsUnavailableException():base("SMS sending unavailable."){}
}

public sealed class TwilioPaymentSms(
    HttpClient client,IOptions<TwilioPaymentSmsOptions> configured)
{
    public bool Available => configured.Value.Enabled &&
        TwilioPaymentSmsOptions.Safe(configured.Value);

    public async Task<string> SendAsync(
        string phone,string checkoutUrl,string month,long cents,CancellationToken ct)
    {
        if (!Available ||
            !Regex.IsMatch(phone,@"\A\+[1-9][0-9]{7,14}\z",
                RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(month,@"\A20[0-9]{2}-[0-9]{2}-01\z",
                RegexOptions.CultureInvariant) ||
            cents is < 1 or > 100000000 ||
            !Uri.TryCreate(checkoutUrl,UriKind.Absolute,out var target) ||
            target.Scheme!=Uri.UriSchemeHttps || target.UserInfo.Length!=0 ||
            target.Host is not ("sandbox.payabl.com" or "pay4.sandbox.payabl.com") ||
            target.Port!=443)
            throw new TwilioPaymentSmsUnavailableException();

        var settings=configured.Value;
        var body="Lefteria FC Academy: Training fee "+month[..7]+" ("+
            (cents/100m).ToString("F2",CultureInfo.InvariantCulture)+
            " EUR). Secure card checkout: "+checkoutUrl+
            " If already paid, ignore this message.";
        if(body.Length>2500)
            throw new TwilioPaymentSmsUnavailableException();

        using var request=new HttpRequestMessage(HttpMethod.Post,
            "https://api.twilio.com/2010-04-01/Accounts/"+settings.AccountSid+"/Messages.json");
        // Configure per request, not a global header that could be forwarded.
        request.Headers.Authorization=new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(
                settings.AccountSid+":"+settings.AuthToken)));
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["To"]=phone,["MessagingServiceSid"]=settings.MessagingServiceSid,
            ["Body"]=body
        });
        using var response=await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode!=HttpStatusCode.Created)
            throw new TwilioPaymentSmsUnavailableException();
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var document=await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth=6 },ct);
        if(!document.RootElement.TryGetProperty("sid",out var value) ||
            value.ValueKind!=JsonValueKind.String ||
            value.GetString() is not string sid ||
            !Regex.IsMatch(sid,@"\ASM[0-9a-fA-F]{32}\z",
                RegexOptions.CultureInvariant))
            throw new TwilioPaymentSmsUnavailableException();
        return sid;
    }
}
