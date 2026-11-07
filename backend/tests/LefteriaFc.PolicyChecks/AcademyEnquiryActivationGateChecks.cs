using System.Net;
using LefteriaFc.Application.Marketing;

namespace LefteriaFc.PolicyChecks;

internal static class AcademyEnquiryActivationGateChecks
{
    public static IEnumerable<(string Name, Action Check)> Checks()
    {
        yield return ("Academy enquiry remains closed without exact opt-in", () =>
        {
            var key = Convert.ToBase64String(Enumerable.Repeat((byte)0x5e, 32).ToArray());
            foreach (var flag in new string?[] { null, "", "false", "TRUE", "True", "1", "yes", " true " })
                Require(!AcademyEnquiryActivationGate.TryCreateFingerprint(flag, key,
                    IPAddress.Parse("192.0.2.4"), out var fingerprint) && fingerprint is null);
        });
        yield return ("Academy enquiry rejects malformed and non-dedicated keys", () =>
        {
            foreach (var key in new string?[] { null, "", "invalid!", Convert.ToBase64String(new byte[16]),
                         Convert.ToBase64String(new byte[31]), Convert.ToBase64String(new byte[33]) })
                Require(!AcademyEnquiryActivationGate.TryCreateFingerprint("true", key,
                    IPAddress.Loopback, out var fingerprint) && fingerprint is null);
        });
        yield return ("Academy enquiry refuses missing trusted remote address", () =>
        {
            var key = Convert.ToBase64String(Enumerable.Repeat((byte)0x5e, 32).ToArray());
            Require(!AcademyEnquiryActivationGate.TryCreateFingerprint("true", key, null,
                out var fingerprint) && fingerprint is null);
        });
        yield return ("Academy enquiry requires deliberate opt-in plus valid remote and key", () =>
        {
            var key = Convert.ToBase64String(Enumerable.Repeat((byte)0x5e, 32).ToArray());
            Require(AcademyEnquiryActivationGate.TryCreateFingerprint("true", key,
                IPAddress.Parse("192.0.2.4"), out var fingerprint));
            Require(fingerprint is not null && fingerprint.Length == 64 &&
                    fingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
            Require(fingerprint == AcademyLeadFingerprint.Compute(
                IPAddress.Parse("192.0.2.4"), Enumerable.Repeat((byte)0x5e, 32).ToArray()));
        });
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Academy enquiry activation security contract failed.");
    }
}
