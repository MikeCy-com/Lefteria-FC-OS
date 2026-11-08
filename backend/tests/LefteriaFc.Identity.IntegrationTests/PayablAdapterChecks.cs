using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Api.Payabl;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class PayablAdapterChecks
{
    public static async Task RunAsync()
    {
        var baseTime = DateTimeOffset.FromUnixTimeSeconds(1794260000);
        var values = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["transactionid"]="118656640",["type"]="capture",["errorcode"]="0",
            ["timestamp"]=baseTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };
        const string secret = "sandbox-test-secret-not-real";
        var signature = SHA256.HashData(Encoding.UTF8.GetBytes(
            values["transactionid"] + values["type"] + values["errorcode"] + values["timestamp"] + secret));
        values["security"] = Convert.ToHexString(signature).ToLowerInvariant();
        Check(PayablSignature.Notification(values,secret,baseTime), "valid callback");
        Check(!PayablSignature.Notification(values,secret,baseTime.AddMinutes(16)), "expired callback");
        Check(!PayablSignature.Notification(values,secret+"other",baseTime), "invalid secret");
        values["errorcode"]="1";
        Check(!PayablSignature.Notification(values,secret,baseTime), "tampered error code");
        values["errorcode"]="0";
        values["type"]="refund";
        Check(!PayablSignature.Notification(values,secret,baseTime), "tampered type");
        values["type"]="capture";
        values["security"]="short";
        Check(!PayablSignature.Notification(values,secret,baseTime), "short signature");
        values["security"]=Convert.ToHexString(signature).ToLowerInvariant();

        var paramsA = new Dictionary<string,string> {
            ["merchantid"]="unit_test",["amount"]="15.00",["currency"]="EUR",
            ["orderid"]="LFC-test"
        };
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(
            "15.00EURLFC-testunit_test" + secret))).ToLowerInvariant();
        Check(PayablSignature.Request(paramsA,secret)==expected, "ordered signature");

        var sent = 0;
        var handler = new FakeHandler(async request =>
        {
            sent++;
            Check(request.Method==HttpMethod.Post, "POST");
            Check(request.RequestUri?.AbsoluteUri=="https://sandbox.payabl.com/pay/payment/init", "sandbox endpoint");
            var payload = await request.Content!.ReadAsStringAsync();
            Check(payload.Contains("signature="), "signed request");
            Check(!payload.Contains("ccn=") && !payload.Contains("cvv="), "no card data");
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("errorcode=0&start_url=https%3A%2F%2Fsandbox.payabl.com%2Fpay%2Fcheckout%2F123&transactionid=104837274")
            };
        });
        var inactive = new PayablGateway(new HttpClient(handler),Options.Create(new PayablOptions()));
        Check(!inactive.Available, "provider disabled by default");
        var request = new PayablCheckoutRequest("LFC-test",1500,"EUR","test@example.com",
            "Test","Customer","127.0.0.1",
            "https://os.example.test/api/payabl/webhooks/abc/test",
            "https://os.example.test/admin/finance");
        try { await inactive.InitializeAsync(request,CancellationToken.None); throw new Exception("Disabled provider sent"); }
        catch(PayablUnavailableException) { }
        Check(sent==0,"disabled gateway makes no network calls");
        var enabled = new PayablGateway(new HttpClient(handler),Options.Create(new PayablOptions {
            Enabled=true,Sandbox=true,MerchantId="unit_test",
            Secret=secret,PublicOrigin="https://os.example.test"
        }));
        Check(enabled.Available,"sandbox ready");
        var checkout = await enabled.InitializeAsync(request,CancellationToken.None);
        Check(checkout.TransactionId=="104837274" &&
              checkout.CheckoutUrl=="https://sandbox.payabl.com/pay/checkout/123", "verified HPP");
        Check(sent==1,"one request only");
        try { await enabled.InitializeAsync(request with { NotificationUrl="https://untrusted.invalid/notify" },
                  CancellationToken.None); throw new Exception("Unsafe origin accepted"); }
        catch (ArgumentException) { }
        Check(sent==1,"unsafe callback origin blocks network");
    }
    private static void Check(bool condition,string name)
    {
        if(!condition)throw new Exception("Payabl sandbox check failed: "+name);
    }
    private sealed class FakeHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> action)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken cancellationToken)=>action(request);
    }
}
