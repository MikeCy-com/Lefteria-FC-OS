using System.Net;
using System.Text;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class TwilioAdapterChecks
{
    public static async Task ConfiguredRequestAsync()
    {
        var settings = new TwilioSmsOptions
        {
            AccountSid = "AC" + new string('a', 32), AuthToken = "test-only-token",
            FromNumber = "+35799111111",
            RoleFromNumbers = new(StringComparer.Ordinal)
            {
                ["coach"] = "+35799222222", ["parent"] = " "
            }
        };
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var sender = new TwilioSmsSender(client, Options.Create(settings));
        var phone = PhoneNumber.Parse("+35799333333");
        Check.True(await sender.SendOtpAsync(phone, "123456", MobileRole.Coach, CancellationToken.None),
            "Configured successful fake Twilio delivery was refused.");
        Check.Equal(HttpMethod.Post, handler.Method, "Twilio adapter used an unexpected method.");
        Check.Equal($"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json", handler.Uri,
            "Twilio adapter used an unexpected destination.");
        Check.Equal("Basic", handler.AuthorizationScheme, "Twilio adapter omitted Basic authentication.");
        Check.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.AccountSid + ":" + settings.AuthToken)),
            handler.AuthorizationValue, "Twilio adapter encoded credentials incorrectly.");
        Check.Equal("application/x-www-form-urlencoded", handler.ContentType, "Twilio form content type differs.");
        Check.Equal(phone.WireValue, handler.Form["To"], "Twilio destination phone differs.");
        Check.Equal("+35799222222", handler.Form["From"], "Role-specific Twilio sender was ignored.");
        Check.True(handler.Form["Body"].Contains("123456", StringComparison.Ordinal) &&
            handler.Form["Body"].Contains("5", StringComparison.Ordinal), "Twilio OTP body lost code or lifetime.");

        foreach (var role in new MobileRole?[] { MobileRole.Parent, MobileRole.Management, null })
        {
            Check.True(await sender.SendOtpAsync(phone, "123456", role, CancellationToken.None),
                "Default fake Twilio sender fallback was refused.");
            Check.Equal(settings.FromNumber, handler.Form["From"], "Blank/missing role sender did not use the default number.");
        }
        handler.Status = HttpStatusCode.TooManyRequests;
        Check.True(!await sender.SendOtpAsync(phone, "123456", MobileRole.Fan, CancellationToken.None),
            "A failed provider HTTP response was reported as delivered.");
    }

    public static async Task UnconfiguredDoesNotCallHttpAsync()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var phone = PhoneNumber.Parse("+35799333333");
        foreach (var settings in new[]
        {
            new TwilioSmsOptions(),
            new TwilioSmsOptions { AccountSid = "invalid", AuthToken = "test-only", FromNumber = "+35799111111" },
            new TwilioSmsOptions { AccountSid = "AC" + new string('a', 32), AuthToken = "", FromNumber = "+35799111111" },
            new TwilioSmsOptions { AccountSid = "AC" + new string('a', 32), AuthToken = "test-only", FromNumber = "invalid" }
        })
        {
            var sender = new TwilioSmsSender(client, Options.Create(settings));
            Check.True(!await sender.SendOtpAsync(phone, "123456", MobileRole.Fan, CancellationToken.None),
                "Unconfigured or malformed Twilio settings were accepted.");
        }
        Check.Equal(0, handler.Calls, "Unconfigured Twilio adapter attempted an HTTP request.");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationValue { get; private set; }
        public string? ContentType { get; private set; }
        public Dictionary<string, string> Form { get; private set; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri?.AbsoluteUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationValue = request.Headers.Authorization?.Parameter;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            Form = content.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]), StringComparer.Ordinal);
            // Never delegate to a network handler: all credentials/codes are fixture-only.
            return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
        }
    }
}
