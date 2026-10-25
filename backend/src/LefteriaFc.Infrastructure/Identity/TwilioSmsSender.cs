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
            string.IsNullOrWhiteSpace(settings.AuthToken) || !PhoneNumber.TryParse(from, out var sender))
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
            ["From"] = sender.WireValue,
            ["Body"] = $"Λευτεριά FC: Ο κωδικός σύνδεσής σας είναι {code}. Ισχύει για 5 λεπτά."
        });
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
