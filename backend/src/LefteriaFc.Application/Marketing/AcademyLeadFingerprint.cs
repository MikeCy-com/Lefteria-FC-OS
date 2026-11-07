using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LefteriaFc.Application.Marketing;

// A deliberately context-bound fingerprint for the future PUBLIC enquiry
// throttle. Only the HTTP server may supply RemoteIpAddress after its trusted
// reverse-proxy validation; never read caller-supplied X-Forwarded-For here.
public static class AcademyLeadFingerprint
{
    private const string Domain = "lefteria-academy-enquiry-throttle-v1\n";

    public static string Compute(IPAddress? trustedRemoteIp,ReadOnlySpan<byte> dedicatedKey)
    {
        if(trustedRemoteIp is null || dedicatedKey.Length!=32)
            throw new ArgumentException("A trusted remote address and dedicated 32-byte key are required.");

        var normalized=trustedRemoteIp.IsIPv4MappedToIPv6
            ? trustedRemoteIp.MapToIPv4() : trustedRemoteIp;
        // IPAddress.ToString() provides a canonical address representation;
        // the output never exposes the address or any part of its raw bytes.
        var message=Encoding.UTF8.GetBytes(Domain+normalized);
        return Convert.ToHexString(HMACSHA256.HashData(dedicatedKey,message))
            .ToLowerInvariant();
    }
}
