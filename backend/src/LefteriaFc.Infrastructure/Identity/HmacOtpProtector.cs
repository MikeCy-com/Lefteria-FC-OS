using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Infrastructure.Identity;

public sealed class HmacOtpProtector(IOptions<MobileAuthOptions> options) : IOtpProtector
{
    private readonly byte[] key = Convert.FromBase64String(options.Value.OtpKey);

    public string CreateSixDigitCode() => RandomNumberGenerator.GetInt32(1_000_000)
        .ToString("D6", CultureInfo.InvariantCulture);

    public byte[] ComputeDigest(Guid challengeId, PhoneNumber phone, string code) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"lefteria-mobile-otp-v1\n{challengeId:N}\n{phone.Digits}\n{code}"));
}
