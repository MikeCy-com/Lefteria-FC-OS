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
        await AlphanumericRequestAsync();
    }

    private static async Task AlphanumericRequestAsync()
    {
        // https://www.twilio.com/docs/trust-hub/registrations/alphanumeric-sender-id
        var settings = new TwilioSmsOptions
        {
            AccountSid = "AC" + new string('a', 32), AuthToken = "test-only-token",
            FromNumber = " Fc 9 ",
            RoleFromNumbers = new(StringComparer.Ordinal)
            {
                ["coach"] = "Coach FC 7", ["parent"] = " ", ["player"] = "00357 (99) 222-222"
            }
        };
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var sender = new TwilioSmsSender(client, Options.Create(settings));
        var phone = PhoneNumber.Parse("+35799333333");
        Check.True(await sender.SendOtpAsync(phone, "123456", MobileRole.Coach, CancellationToken.None),
            "Configured alphanumeric role sender was refused.");
        Check.Equal("Coach FC 7", handler.Form["From"], "Alphanumeric role sender changed case or spacing.");
        Check.True(handler.RawForm.Contains("From=Coach+FC+7", StringComparison.Ordinal),
            "Alphanumeric sender was not form encoded.");
        foreach (var role in new MobileRole?[] { MobileRole.Parent, MobileRole.Management, null })
        {
            Check.True(await sender.SendOtpAsync(phone, "123456", role, CancellationToken.None),
                "Blank/missing role sender did not fall back to a valid alphanumeric ID.");
            Check.Equal(" Fc 9 ", handler.Form["From"], "Alphanumeric fallback was trimmed or normalized.");
            Check.True(handler.RawForm.Contains("From=+Fc+9+", StringComparison.Ordinal),
                "Alphanumeric leading/trailing spaces were not preserved in the form.");
        }
        Check.True(await sender.SendOtpAsync(phone, "123456", MobileRole.Player, CancellationToken.None),
            "Formatted numeric role sender was refused.");
        Check.Equal("+35799222222", handler.Form["From"], "Numeric role sender lost E.164 normalization.");
        Check.True(handler.RawForm.Contains("From=%2B35799222222", StringComparison.Ordinal),
            "Numeric sender's plus prefix was not form encoded.");
        foreach (var value in new[] { "A", "ABCDEFGHIJK", "A1234567890", "Fc club 123" })
        {
            settings.FromNumber = value;
            Check.True(await sender.SendOtpAsync(phone, "123456", null, CancellationToken.None),
                "Valid bounded ASCII alphanumeric ID was refused.");
            Check.Equal(value, handler.Form["From"], "Valid alphanumeric ID changed on delivery.");
        }
        settings.FromNumber = "00357 (99) 111-111";
        Check.True(await sender.SendOtpAsync(phone, "123456", null, CancellationToken.None),
            "Formatted numeric default sender was refused.");
        Check.Equal("+35799111111", handler.Form["From"], "Numeric default sender lost normalization.");
        settings.RoleFromNumbers["coach"] = "Club+FC";
        var calls = handler.Calls;
        Check.True(!await sender.SendOtpAsync(phone, "123456", MobileRole.Coach, CancellationToken.None),
            "An invalid nonblank role sender silently fell back to the default.");
        Check.Equal(calls, handler.Calls, "Invalid role sender attempted provider HTTP.");
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
            new TwilioSmsOptions { AccountSid = "AC" + new string('a', 32), AuthToken = "test-only", FromNumber = "invalid!" }
        })
        {
            var sender = new TwilioSmsSender(client, Options.Create(settings));
            Check.True(!await sender.SendOtpAsync(phone, "123456", MobileRole.Fan, CancellationToken.None),
                "Unconfigured or malformed Twilio settings were accepted.");
        }
        foreach (var value in new string?[]
        {
            null, "", " ", "1234567", "+1234567", "1234567890123456", "ABCDEFGHIJKL", " ABCDEFGHIJ ",
            "Club_FC", "Club-FC", "Club.FC", "Club&FC", "Club+FC", "Club\nFC", "Club\tFC", "\u00a0FC", "Λευτεριά", "Clüb", "ＦＣ"
        })
        {
            var settings = new TwilioSmsOptions
            {
                AccountSid = "AC" + new string('a', 32), AuthToken = "test-only", FromNumber = value!
            };
            var sender = new TwilioSmsSender(client, Options.Create(settings));
            Check.True(!await sender.SendOtpAsync(phone, "123456", null, CancellationToken.None),
                "Invalid sender text was accepted or normalized into an approved ID.");
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
        public string RawForm { get; private set; } = string.Empty;
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
            RawForm = content;
            Form = content.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]), StringComparer.Ordinal);
            // Never delegate to a network handler: all credentials/codes are fixture-only.
            return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
        }
    }
}
