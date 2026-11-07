using System.Net;
using LefteriaFc.Application.Marketing;

namespace LefteriaFc.PolicyChecks;

internal static class AcademyLeadFingerprintChecks
{
    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("Academy fingerprint is deterministic, bounded and never exposes IP",()=>{
            var key=Enumerable.Repeat((byte)0x51,32).ToArray();
            var ip=IPAddress.Parse("192.0.2.25");
            var one=AcademyLeadFingerprint.Compute(ip,key);
            var two=AcademyLeadFingerprint.Compute(ip,key);
            Require(one==two && one.Length==64);
            Require(one.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'));
            Require(!one.Contains("192.0.2",StringComparison.Ordinal));
            Require(one!=AcademyLeadFingerprint.Compute(IPAddress.Parse("192.0.2.26"),key));
            Require(one!=AcademyLeadFingerprint.Compute(ip,Enumerable.Repeat((byte)0x52,32).ToArray()));
        });
        yield return ("IPv4 mapped IPv6 is equivalent to canonical IPv4",()=>{
            var key=Enumerable.Repeat((byte)0x5a,32).ToArray();
            Require(AcademyLeadFingerprint.Compute(IPAddress.Parse("::ffff:192.0.2.30"),key)==
                AcademyLeadFingerprint.Compute(IPAddress.Parse("192.0.2.30"),key));
        });
        yield return ("Enquiry HMAC refuses missing address or incorrect key lengths",()=>{
            var key=new byte[32];
            Throws(()=>AcademyLeadFingerprint.Compute(null,key));
            Throws(()=>AcademyLeadFingerprint.Compute(IPAddress.Loopback,Array.Empty<byte>()));
            Throws(()=>AcademyLeadFingerprint.Compute(IPAddress.Loopback,new byte[16]));
        });
    }

    private static void Require(bool result)
    {
        if(!result)throw new InvalidOperationException("Academy enquiry HMAC fingerprint policy failed.");
    }
    private static void Throws(Action action)
    {
        try { action(); }catch(ArgumentException) { return; }
        throw new InvalidOperationException("Expected key/context refusal.");
    }
}
