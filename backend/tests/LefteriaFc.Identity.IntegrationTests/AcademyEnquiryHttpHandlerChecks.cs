using System.Net;
using System.Text;
using LefteriaFc.Api.Marketing;
using LefteriaFc.Application.Marketing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class AcademyEnquiryHttpHandlerChecks
{
    private const string AllowedOrigin="https://lefteriafc.cy";
    private static readonly byte[] Key=Enumerable.Repeat((byte)0x4d,32).ToArray();
    private const string ValidBody=
        """{"name":"Fixture Guardian","phone":"+35799000000","consent_gdpr":true,"consent_marketing":false}""";

    public static async Task RequestGuardsAsync()
    {
        var writer=new FakeWriter();
        var disabled=await Post(writer,ValidBody,enabled:false);
        Require(disabled==503 && writer.Calls==0,"Disabled enquiry unexpectedly wrote data.");

        var origin=await Post(writer,ValidBody,origin:"https://evil.invalid");
        Require(origin==403 && writer.Calls==0,"Cross-origin enquiry was not denied.");

        var missingOrigin=await Post(writer,ValidBody,origin:null);
        Require(missingOrigin==403 && writer.Calls==0,"Missing Origin was not denied.");

        var withoutIp=await Post(writer,ValidBody,withoutRemote:true);
        Require(withoutIp==503 && writer.Calls==0,"Missing trusted remote was not denied.");

        var malformedKey=await Post(writer,ValidBody,key:"not-base64");
        Require(malformedKey==503 && writer.Calls==0,"Invalid server HMAC key was not denied.");

        var unknownField=await Post(writer,
            """{"name":"Fixture Guardian","phone":"+35799000000","consent_gdpr":true,"player_id":"another-child"}""");
        Require(unknownField==400 && writer.Calls==0,"Unmapped identity selector was accepted.");

        var missingConsent=await Post(writer,
            """{"name":"Fixture Guardian","phone":"+35799000000","consent_gdpr":false}""");
        Require(missingConsent==400 && writer.Calls==0,"Missing GDPR consent was accepted.");

        var huge=await Post(writer,new string('x',8193));
        Require(huge==413 && writer.Calls==0,"Oversized enquiry was accepted.");

        var status=await Post(writer,ValidBody);
        Require(status==503 && writer.Calls==1,"Unapproved database writer returned success.");

        writer.Result=AcademyLeadStoreResult.Stored;
        status=await Post(writer,ValidBody);
        Require(status==202 && writer.Calls==2,"Committed fixture lead was not acknowledged.");
        Require(writer.Organization=="lefteria_fc_academy","Organization was not server bound.");
        Require(writer.Fingerprint==AcademyLeadFingerprint.Compute(
            IPAddress.Parse("192.0.2.40"),Key),"Fingerprint was not derived from the trusted remote address.");

        writer.Result=AcademyLeadStoreResult.RateLimited;
        status=await Post(writer,ValidBody);
        Require(status==429 && writer.Calls==3,"Database throttle outcome was not preserved.");
    }

    private static async Task<int> Post(
        FakeWriter writer,string payload,bool enabled=true,string? origin=AllowedOrigin,
        bool withoutRemote=false,string? key=null)
    {
        var ctx=new DefaultHttpContext();
        ctx.Request.Method="POST";
        ctx.Request.ContentType="application/json";
        ctx.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(payload));
        ctx.Request.ContentLength=Encoding.UTF8.GetByteCount(payload);
        ctx.Connection.RemoteIpAddress=withoutRemote?null:IPAddress.Parse("192.0.2.40");
        if(origin is not null)ctx.Request.Headers.Origin=origin;
        ctx.Request.Headers["X-Forwarded-For"]="203.0.113.41";
        var config=new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?>
            {
                ["AcademyEnquiry:Enabled"]=enabled?"true":"false",
                ["AcademyEnquiry:AllowedOrigin"]=AllowedOrigin,
                ["AcademyEnquiry:FingerprintKey"]=key??Convert.ToBase64String(Key)
            }).Build();
        var result=await AcademyEnquiryHttpHandler.HandleAsync(
            ctx,config,new AcademyLeadSubmissionService(writer),CancellationToken.None);
        if(result is not IStatusCodeHttpResult status || status.StatusCode is null)
            throw new InvalidOperationException("Enquiry response omitted explicit status.");
        return status.StatusCode.Value;
    }

    private static void Require(bool condition,string failure)
    {
        if(!condition)throw new InvalidOperationException(failure);
    }

    private sealed class FakeWriter : IAcademyLeadWriter
    {
        public AcademyLeadStoreResult Result {get;set;}=AcademyLeadStoreResult.Unavailable;
        public int Calls {get;private set;}
        public string? Organization {get;private set;}
        public string? Fingerprint {get;private set;}
        public Task<AcademyLeadStoreResult> WriteAsync(
            ValidatedAcademyLead lead,string organization,string trustedFingerprint,CancellationToken ct)
        {
            Calls++;
            Organization=organization;
            Fingerprint=trustedFingerprint;
            return Task.FromResult(Result);
        }
    }
}
