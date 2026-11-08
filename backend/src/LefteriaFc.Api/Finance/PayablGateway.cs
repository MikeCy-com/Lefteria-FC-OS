using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Only hosted checkout; all card entry stays on payabl. Full PAN/CVC are
/// never accepted, saved, logged, or proxied by Lefteria FC OS.
/// </summary>
public sealed class PayablGatewaySettings
{
    public bool Enabled { get; }
    public bool Sandbox { get; }
    public string? MerchantId { get; }
    public Uri? PublicOrigin { get; }
    public Uri? GatewayOrigin { get; }
    private readonly string? secret;

    public PayablGatewaySettings(IConfiguration config)
    {
        Enabled=string.Equals(config["Payabl:Enabled"],"true",
            StringComparison.OrdinalIgnoreCase);
        Sandbox=!string.Equals(config["Payabl:Environment"],"production",
            StringComparison.OrdinalIgnoreCase);
        if(!Enabled)return;
        if(config["Payabl:Environment"] is not ("sandbox" or "production"))
            throw new InvalidOperationException("Enabled payabl. requires an explicit sandbox/production environment.");
        var merchant=config["Payabl:MerchantId"];
        var key=config["Payabl:MerchantSecret"];
        var origin=config["Payabl:PublicOrigin"];
        if(merchant is null || merchant.Length is <1 or >40 ||
           !Regex.IsMatch(merchant,@"\A[A-Za-z0-9_-]+\z",RegexOptions.CultureInvariant)||
           key is null || key.Length<16 || key.Length>256 ||
           !Uri.TryCreate(origin,UriKind.Absolute,out var website) ||
           website.Scheme!="https" || website.UserInfo.Length>0 ||
           website.AbsolutePath!="/" || website.Query.Length>0 ||
           website.Fragment.Length>0)
            throw new InvalidOperationException("Enabled payabl. requires merchant credentials and an exact HTTPS origin.");
        MerchantId=merchant;
        secret=key;
        PublicOrigin=website;
        GatewayOrigin=new Uri(Sandbox?
            "https://sandbox.payabl.com/":"https://payabl.com/");
    }

    public string SignRequest(IReadOnlyDictionary<string,string> fields)
    {
        if(!Enabled||string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("payabl. gateway is disabled.");
        // payabl.'s documented legacy request signing is SHA-1(values sorted
        // by parameter name + merchant secret), with unencoded field values.
        // Do not add HASH/signature to the signed input.
        var data=string.Concat(fields.Where(kv=>kv.Key is not ("signature" or "HASH"))
            .OrderBy(kv=>kv.Key,StringComparer.Ordinal).Select(kv=>kv.Value))+secret;
        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(data)));
    }

    public bool VerifyNotification(string? transactionId,string? type,string? errorCode,
        string? timestamp,string? security)
    {
        if(!Enabled||string.IsNullOrEmpty(secret)||transactionId is null||
           !Regex.IsMatch(transactionId,@"\A[0-9]{1,50}\z")||
           type is null||!Regex.IsMatch(type,@"\A[a-z_]{1,30}\z")||
           errorCode is null||!Regex.IsMatch(errorCode,@"\A-?[0-9]{1,8}\z")||
           timestamp is null||!long.TryParse(timestamp,NumberStyles.None,
               CultureInfo.InvariantCulture,out var unix)||
           security is null||security.Length!=64||
           !Regex.IsMatch(security,@"\A[0-9a-fA-F]{64}\z"))
            return false;
        // Accept delayed retries, never an event far in the future.
        var when=DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(unix,
            DateTimeOffset.MinValue.ToUnixTimeSeconds(),
            DateTimeOffset.MaxValue.ToUnixTimeSeconds()));
        if(when>DateTimeOffset.UtcNow.AddMinutes(5)||
           when<DateTimeOffset.UtcNow.AddDays(-30))return false;
        // Simplified payabl. notification security:
        // SHA-256(transactionid + type + errorcode + timestamp + secret).
        var material=Encoding.UTF8.GetBytes(transactionId+type+errorCode+timestamp+secret);
        var expected=SHA256.HashData(material);
        var supplied=Convert.FromHexString(security);
        return CryptographicOperations.FixedTimeEquals(expected,supplied);
    }

    public bool IsTrustedCheckoutUrl(string? raw,out Uri result)
    {
        result=null!;
        if(!Enabled||raw is null||raw.Length>2048||
           !Uri.TryCreate(raw,UriKind.Absolute,out var uri)||
           uri.Scheme!="https"||uri.UserInfo.Length>0||
           uri.Fragment.Length>0||uri.Port!=443)return false;
        var host=uri.Host;
        var allowed=Sandbox?
            host is "sandbox.payabl.com" or "pay4.sandbox.payabl.com":
            host is "payabl.com" or "pay4.payabl.com";
        if(!allowed||!uri.AbsolutePath.StartsWith("/hpp/",StringComparison.Ordinal))
            return false;
        result=uri;
        return true;
    }
}

public sealed record PayablHostedCheckout(string StartUrl,string TransactionId);

public sealed class PayablHostedClient(PayablGatewaySettings settings,
    IHttpClientFactory http)
{
    public async Task<PayablHostedCheckout> CreateAsync(
        string orderId,int amountCents,string firstName,string lastName,
        string email,CancellationToken token)
    {
        if(!settings.Enabled||settings.GatewayOrigin is null||
           settings.PublicOrigin is null||settings.MerchantId is null)
            throw new InvalidOperationException("payabl. checkout is not configured.");
        if(!Regex.IsMatch(orderId,@"\Alfc_[0-9a-f]{32}\z")||
           amountCents is <1 or >10000000||
           firstName.Length is <1 or >50||lastName.Length is <1 or >50||
           email.Length is <3 or >50||!email.Contains('@'))
            throw new ArgumentException("Invalid hosted checkout request.");
        var fields=new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            ["merchantid"]=settings.MerchantId,
            ["orderid"]=orderId,
            ["amount"]=(amountCents/100m).ToString("0.00",CultureInfo.InvariantCulture),
            ["currency"]="EUR",
            ["language"]="el",
            ["firstname"]=firstName,
            ["lastname"]=lastName,
            ["email"]=email,
            ["url_return"]=new Uri(settings.PublicOrigin,"pay/return").AbsoluteUri,
            ["notification_url"]=new Uri(settings.PublicOrigin,"api/payabl/notifications").AbsoluteUri
        };
        fields["signature"]=settings.SignRequest(fields);
        using var request=new HttpRequestMessage(HttpMethod.Post,
            new Uri(settings.GatewayOrigin,"pay/payment/init"));
        request.Content=new FormUrlEncodedContent(fields);
        request.Headers.TryAddWithoutValidation("Accept","application/x-www-form-urlencoded, */*");
        using var response=await http.CreateClient("payabl-gateway").SendAsync(
            request,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode!=HttpStatusCode.OK)
            throw new InvalidOperationException("payabl. session request failed.");
        var bytes=await response.Content.ReadAsByteArrayAsync(token);
        if(bytes.Length>8192)throw new InvalidOperationException("Unexpected payabl. response size.");
        var payload=ParseForm(Encoding.UTF8.GetString(bytes));
        if(!payload.TryGetValue("errorcode",out var code)||code!="0"||
           !payload.TryGetValue("transactionid",out var transaction)||
           !Regex.IsMatch(transaction,@"\A[0-9]{1,50}\z")||
           !payload.TryGetValue("start_url",out var url)||
           !settings.IsTrustedCheckoutUrl(url,out var redirect))
            throw new InvalidOperationException("Unverified payabl. hosted session.");
        return new(redirect.AbsoluteUri,transaction);
    }

    public static Dictionary<string,string> ParseForm(string body)
    {
        if(body.Length>8192)throw new FormatException("Oversized gateway form.");
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var part in body.Split('&'))
        {
            if(part.Length==0)continue;
            var pos=part.IndexOf('=');
            if(pos<1)throw new FormatException("Malformed gateway form.");
            var key=WebUtility.UrlDecode(part[..pos]);
            var value=WebUtility.UrlDecode(part[(pos+1)..]);
            if(key.Length is <1 or >80||value.Length>4096||
               !Regex.IsMatch(key,@"\A[A-Za-z0-9_]+\z")||
               !result.TryAdd(key,value))
                throw new FormatException("Duplicated or unsafe gateway field.");
        }
        return result;
    }
}
