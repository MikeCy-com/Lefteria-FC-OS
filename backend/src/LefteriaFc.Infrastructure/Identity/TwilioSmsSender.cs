using System.Net.Http.Headers;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Infrastructure.Identity;

public sealed class TwilioSmsSender(HttpClient client, IOptions<TwilioSmsOptions> options) : ISmsSender
{
    public async Task<bool> SendOtpAsync(PhoneNumber phone, string code, MobileRole? detectedRole,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var from = detectedRole is { } role && settings.RoleFromNumbers.TryGetValue(role.ToWireValue(), out var roleNumber)
            && !string.IsNullOrWhiteSpace(roleNumber) ? roleNumber : settings.FromNumber;
        if (settings.AccountSid.Length != 34 || !settings.AccountSid.StartsWith("AC", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(settings.AuthToken) || !TrySender(from, out var sender))
        {
            return false;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid)}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.AccountSid + ":" + settings.AuthToken)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phone.WireValue,
            ["From"] = sender,
            ["Body"] = $"Λευτεριά FC: Ο κωδικός σύνδεσής σας είναι {code}. Ισχύει για 5 λεπτά."
        });
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static bool TrySender(string? value, out string sender)
    {
        sender = string.Empty;
        if (PhoneNumber.TryParse(value, out var phone))
        {
            sender = phone.WireValue;
            return true;
        }
        // Twilio's core alphanumeric format is bounded ASCII letters, digits
        // and spaces, with at least one letter. Keep its exact configured text.
        if (value is not { Length: > 0 and <= 11 }) return false;
        var hasLetter = false;
        foreach (var character in value)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') hasLetter = true;
            else if (character is not (>= '0' and <= '9' or ' ')) return false;
        }
        if (!hasLetter) return false;
        sender = value;
        return true;
    }
}
