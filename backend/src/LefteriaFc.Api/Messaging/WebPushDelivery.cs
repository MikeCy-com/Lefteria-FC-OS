using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LefteriaFc.Api.Messaging;

/// <summary>
/// This sender transmits a *payload-free* RFC 8292 VAPID Web Push request.
/// The service worker displays only a generic announcement prompt. There is
/// deliberately no title, member identity or announcement content to encrypt
/// or expose to a push gateway. HTTP 2xx means service acceptance, not device
/// display or confirmed readership.
/// </summary>
public sealed class WebPushDeliverySettings
{
    public bool Enabled { get; }
    public string? PublicKey { get; }
    private readonly byte[]? _privateKey;
    private readonly string? _subject;

    public WebPushDeliverySettings(IConfiguration config)
    {
        // Fail closed. Explicitly enable only after configuring real VAPID
        // credentials and reviewing the deployment's HTTPS/browser support.
        Enabled=string.Equals(config["WebPush:Enabled"],"true",
            StringComparison.OrdinalIgnoreCase);
        if(!Enabled)return;
        var subject=config["WebPush:Subject"];
        if(string.IsNullOrWhiteSpace(subject) || subject.Length>256 ||
           (!subject.StartsWith("mailto:",StringComparison.Ordinal) &&
            !(Uri.TryCreate(subject,UriKind.Absolute,out var subjectUri) &&
              subjectUri.Scheme==Uri.UriSchemeHttps)))
            throw new InvalidOperationException("Web Push subject must be a configured HTTPS URL or mailto URI.");

        var pub=config["WebPush:VapidPublicKey"];
        var priv=config["WebPush:VapidPrivateKey"];
        if(!WebPushEndpointPolicy.DecodeKey(pub,out var publicBytes)||
           publicBytes.Length!=65||publicBytes[0]!=4||
           !WebPushEndpointPolicy.DecodeKey(priv,out var privateBytes)||
           privateBytes.Length!=32)
            throw new InvalidOperationException("Web Push requires a valid P-256 public/private VAPID key pair.");
        using var signingKey=CreateKey(publicBytes,privateBytes);
        var material=signingKey.ExportParameters(false);
        if(!CryptographicOperations.FixedTimeEquals(material.Q.X!,publicBytes.AsSpan(1,32))||
           !CryptographicOperations.FixedTimeEquals(material.Q.Y!,publicBytes.AsSpan(33,32)))
            throw new InvalidOperationException("Web Push configured VAPID key pair does not match.");
        PublicKey=WebPushEndpointPolicy.Base64Url(publicBytes);
        _privateKey=privateBytes;
        _subject=subject;
    }

    public string CreateToken(Uri endpoint)
    {
        if(!Enabled || PublicKey is null || _privateKey is null || _subject is null)
            throw new InvalidOperationException("Web Push is disabled.");
        if(!WebPushEndpointPolicy.DecodeKey(PublicKey,out var publicBytes))
            throw new InvalidOperationException("Invalid configured VAPID key.");
        using var signingKey=CreateKey(publicBytes,_privateKey);
        var header=WebPushEndpointPolicy.Base64Url(
            Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"typ\":\"JWT\"}"));
        var payload=WebPushEndpointPolicy.Base64Url(
            JsonSerializer.SerializeToUtf8Bytes(new {
                aud=endpoint.GetLeftPart(UriPartial.Authority),
                exp=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                sub=_subject
            }));
        var input=Encoding.ASCII.GetBytes(header+"."+payload);
        var signature=signingKey.SignData(input,HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return header+"."+payload+"."+WebPushEndpointPolicy.Base64Url(signature);
    }

    private static ECDsa CreateKey(byte[] publicBytes,byte[] privateBytes)
    {
        var key=ECDsa.Create();
        key.ImportParameters(new ECParameters{
            Curve=ECCurve.NamedCurves.nistP256,
            Q=new ECPoint{X=publicBytes.AsSpan(1,32).ToArray(),
                Y=publicBytes.AsSpan(33,32).ToArray()},
            D=privateBytes
        });
        return key;
    }
}

public static class WebPushEndpointPolicy
{
    private static readonly Regex Token=new(
        @"\A[A-Za-z0-9_-]+={0,2}\z",RegexOptions.CultureInvariant);

    public static bool TryEndpoint(string? raw,out Uri endpoint)
    {
        endpoint=null!;
        if(raw is null || raw.Length is <24 or >2048 ||
           raw.Any(x=>x>127 || char.IsControl(x))||
           !Uri.TryCreate(raw,UriKind.Absolute,out var uri)||
           uri.Scheme!=Uri.UriSchemeHttps || uri.Port!=443 ||
           uri.UserInfo.Length>0 || uri.Fragment.Length>0 ||
           uri.AbsolutePath.Length<2 || uri.IsLoopback ||
           !TrustedPushHost(uri.Host))
            return false;
        endpoint=uri;
        return true;
    }

    private static bool TrustedPushHost(string host)=>
        host.Equals("fcm.googleapis.com",StringComparison.OrdinalIgnoreCase)||
        host.Equals("updates.push.services.mozilla.com",StringComparison.OrdinalIgnoreCase)||
        host.Equals("web.push.apple.com",StringComparison.OrdinalIgnoreCase)||
        (host.EndsWith(".push.apple.com",StringComparison.OrdinalIgnoreCase)&&
         host.Length>".push.apple.com".Length)||
        (host.EndsWith(".notify.windows.com",StringComparison.OrdinalIgnoreCase)&&
         host.Length>".notify.windows.com".Length);

    public static bool DecodeKey(string? raw,out byte[] key)
    {
        key=[];
        if(raw is null || raw.Length is <16 or >128 || !Token.IsMatch(raw))
            return false;
        try{
            var normalized=raw.TrimEnd('=').Replace('-','+').Replace('_','/');
            normalized=normalized.PadRight((normalized.Length+3)/4*4,'=');
            key=Convert.FromBase64String(normalized);
            return true;
        }
        catch(FormatException){return false;}
    }

    public static string Base64Url(ReadOnlySpan<byte> bytes)=>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}

public sealed class WebPushDeliverySender(
    WebPushDeliverySettings settings,IHttpClientFactory factory)
{
    public async Task<(string Outcome,int? Status)> SendAsync(
        string rawEndpoint,CancellationToken cancellationToken)
    {
        if(!settings.Enabled || !WebPushEndpointPolicy.TryEndpoint(rawEndpoint,out var endpoint))
            return ("rejected",null);
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);
        request.Content=new ByteArrayContent([]);
        request.Headers.TryAddWithoutValidation("Authorization",
            "vapid t="+settings.CreateToken(endpoint)+", k="+settings.PublicKey);
        request.Headers.TryAddWithoutValidation("TTL","3600");
        request.Headers.TryAddWithoutValidation("Urgency","normal");
        request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{
            NoStore=true
        };
        try
        {
            using var response=await factory.CreateClient("web-push").SendAsync(
                request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
            var status=(int)response.StatusCode;
            if(status is >=200 and <300)return ("accepted",status);
            if(status is 404 or 410)return ("gone",status);
            if(status==429 || status>=500)return ("retry",status);
            return ("rejected",status);
        }
        catch(HttpRequestException){return ("retry",null);}
        catch(TaskCanceledException) when(!cancellationToken.IsCancellationRequested)
        {return ("retry",null);}
    }
}
