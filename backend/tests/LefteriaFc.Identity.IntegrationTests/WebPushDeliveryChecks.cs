using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LefteriaFc.Api.Messaging;
using Microsoft.Extensions.Configuration;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class WebPushDeliveryChecks
{
    public static async Task RunAsync()
    {
        var disabled=new WebPushDeliverySettings(new ConfigurationBuilder().Build());
        Require(!disabled.Enabled && disabled.PublicKey is null,"Unconfigured push is not disabled.");
        foreach(var host in new[]{
            "https://localhost/fcm/send/token",
            "http://fcm.googleapis.com/fcm/send/token",
            "https://fcm.googleapis.com.attacker.invalid/fcm/send/token",
            "https://127.0.0.1/fcm/send/token",
            "https://user:pass@fcm.googleapis.com/fcm/send/token",
            "https://fcm.googleapis.com:444/fcm/send/token",
            "https://fcm.googleapis.com/fcm/send/token#fragment"})
            Require(!WebPushEndpointPolicy.TryEndpoint(host,out _),
                "Unsafe Web Push endpoint allowed.");
        Require(WebPushEndpointPolicy.TryEndpoint(
            "https://fcm.googleapis.com/fcm/send/sample",out var endpoint),
            "Real push provider endpoint was rejected.");
        Require(WebPushEndpointPolicy.TryEndpoint(
            "https://updates.push.services.mozilla.com/wpush/v2/token",out _),
            "Firefox Web Push provider was rejected.");
        Require(WebPushEndpointPolicy.TryEndpoint(
            "https://web.push.apple.com/Q/t",out _),
            "Safari Web Push provider was rejected.");
        using var generated=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ec=generated.ExportParameters(true);
        var publicKey=new byte[]{4}.Concat(ec.Q.X!).Concat(ec.Q.Y!).ToArray();
        var settings=new WebPushDeliverySettings(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>{
                ["WebPush:Enabled"]="true",
                ["WebPush:VapidPublicKey"]=WebPushEndpointPolicy.Base64Url(publicKey),
                ["WebPush:VapidPrivateKey"]=WebPushEndpointPolicy.Base64Url(ec.D!),
                ["WebPush:Subject"]="mailto:security@example.invalid"
            }).Build());
        Require(settings.Enabled&&settings.PublicKey is not null,
            "Configured push VAPID keys were rejected.");

        var jwt=settings.CreateToken(endpoint);
        var parts=jwt.Split('.');
        Require(parts.Length==3,"VAPID token is not JWS compact serialization.");
        using var doc=JsonDocument.Parse(Decode(parts[1]));
        Require(doc.RootElement.GetProperty("aud").GetString()=="https://fcm.googleapis.com"&&
            doc.RootElement.GetProperty("sub").GetString()=="mailto:security@example.invalid"&&
            doc.RootElement.GetProperty("exp").GetInt64()>
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()&&
            doc.RootElement.GetProperty("exp").GetInt64()<=
                DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(),
            "Invalid audience, contact or TTL in VAPID JWT.");
        using var verifier=ECDsa.Create(new ECParameters{
            Curve=ECCurve.NamedCurves.nistP256,Q=ec.Q
        });
        Require(verifier.VerifyData(
            Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),
            HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            "VAPID ES256 signature did not validate.");

        // A fake local HttpMessageHandler proves that the worker sends no
        // data to push gateways, sends required headers and classifies replies.
        var fake=new ReplyHandler(HttpStatusCode.Created);
        var sender=new WebPushDeliverySender(settings,new LocalFactory(fake));
        var (accepted,code)=await sender.SendAsync(endpoint.AbsoluteUri,
            CancellationToken.None);
        Require(accepted=="accepted"&&code==201&&fake.Requests==1&&
            fake.LastBodyBytes==0&&fake.LastVapidValid&&fake.LastTtl=="3600",
            "Payload-free push was not accepted with an authenticated VAPID request.");
        fake.Status=HttpStatusCode.Gone;
        var (gone,oldCode)=await sender.SendAsync(endpoint.AbsoluteUri,
            CancellationToken.None);
        Require(gone=="gone"&&oldCode==410,"Expired endpoint not retired.");
        fake.Status=HttpStatusCode.TooManyRequests;
        var (retry,retryCode)=await sender.SendAsync(endpoint.AbsoluteUri,
            CancellationToken.None);
        Require(retry=="retry"&&retryCode==429,"Throttled provider not retried.");
        var (rejected,_) = await sender.SendAsync(
            "https://push.attacker.invalid/send",CancellationToken.None);
        Require(rejected=="rejected"&&fake.Requests==3,
            "Untrusted endpoint reached outbound HTTP.");
    }

    private static byte[] Decode(string raw) =>
        Convert.FromBase64String(raw.Replace('-','+').Replace('_','/')
            .PadRight((raw.Length+3)/4*4,'='));
    private static void Require(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    private sealed class LocalFactory(HttpMessageHandler handler):IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Require(name=="web-push","Unexpected HTTP client selected.");
            return new HttpClient(handler,disposeHandler:false);
        }
    }
    private sealed class ReplyHandler(HttpStatusCode status):HttpMessageHandler
    {
        public HttpStatusCode Status {get;set;}=status;
        public int Requests {get;private set;}
        public int LastBodyBytes {get;private set;}
        public bool LastVapidValid {get;private set;}
        public string? LastTtl {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken token)
        {
            Requests++;
            LastBodyBytes=(await request.Content!.ReadAsByteArrayAsync(token)).Length;
            LastVapidValid=request.Headers.TryGetValues("Authorization",out var values)&&
                values.Single().StartsWith("vapid t=",StringComparison.Ordinal)&&
                values.Single().Contains(", k=",StringComparison.Ordinal)&&
                request.RequestUri!.Host=="fcm.googleapis.com"&&
                request.Method==HttpMethod.Post;
            LastTtl=request.Headers.TryGetValues("TTL",out var ttl)?
                ttl.Single():null;
            return new HttpResponseMessage(Status);
        }
    }
}
