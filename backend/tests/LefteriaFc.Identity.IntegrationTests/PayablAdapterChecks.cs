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

        // The extra Payabl fields are NOT included in the simplified
        // notification signature; verify their format and exact callback
        // order binding, then let PostgreSQL decide settlement state.
        var paymentId=Guid.Parse("9d3e279e-f8b7-4f76-b26b-d017c3f0f501");
        Check(PayablNotification.OrderId(paymentId).Length==30 &&
            PayablNotification.OrderId(paymentId).All(char.IsAsciiLetterOrDigit),
            "merchant order ID is provider-compliant");
        values["orderid"]=PayablNotification.OrderId(paymentId);
        values["amount"]="15.00";
        values["currency"]="EUR";
        values["payment_state"]="not_credited_yet";
        Check(PayablNotification.TryRead(values,paymentId,secret,baseTime,out var pending) &&
            pending is {PaymentState:"not_credited_yet",AmountCents:1500,Currency:"EUR"},
            "pending bank settlement has exact cents but does not mean paid");
        values["payment_state"]="credited";
        Check(PayablNotification.TryRead(values,paymentId,secret,baseTime,out var credited) &&
            credited is {PaymentState:"credited"}, "credited APM remains untrusted");
        values["payment_state"]="not_credited_yet";
        values["orderid"]=PayablNotification.OrderId(Guid.NewGuid());
        Check(!PayablNotification.TryRead(values,paymentId,secret,baseTime,out _),
            "cross-order callback refused despite otherwise valid signature");
        values["orderid"]=PayablNotification.OrderId(paymentId);
        foreach(var bad in new[]{"1e2","1.001","NaN","-1.00","1,00","1000001.00"})
        {
            values["amount"]=bad;
            Check(!PayablNotification.TryRead(values,paymentId,secret,baseTime,out _),
                "malformed callback amount must not change ledger");
        }
        values["amount"]="15.00";
        values["currency"]="USD";
        Check(PayablNotification.TryRead(values,paymentId,secret,baseTime,out var foreign) &&
            foreign is {Currency:"USD"}, "SQL must flag unexpected currency");
        values["currency"]="EUR";
        values["payment_state"]="pending;drop";
        Check(!PayablNotification.TryRead(values,paymentId,secret,baseTime,out _),
            "unrecognized settlement state format refused");
        values["payment_state"]="not_credited_yet";

        var paramsA = new Dictionary<string,string> {
            ["merchantid"]="unit_test",["amount"]="15.00",["currency"]="EUR",
            ["orderid"]="LFC-test"
        };
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(
            "15.00EURunit_testLFC-test" + secret))).ToLowerInvariant();
        Check(PayablSignature.Request(paramsA,secret)==expected, "ordered signature");

        var sent = 0;
        var handler = new FakeHandler(async request =>
        {
            sent++;
            Check(request.Method==HttpMethod.Post, "POST");
            Check(request.RequestUri?.AbsoluteUri=="https://sandbox.payabl.com/pay/payment/init", "sandbox endpoint");
            var payload = await request.Content!.ReadAsStringAsync();
            Check(payload.Contains("signature="), "signed request");
            Check(payload.Contains("payment_method=1"), "card-only hosted checkout");
            Check(request.Headers.Accept.Any(value=>value.MediaType=="application/x-www-form-urlencoded"), "provider form accept");
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
        var sandboxClient = new HttpClient(handler);
        sandboxClient.DefaultRequestHeaders.Accept.ParseAdd("application/x-www-form-urlencoded");
        var enabled = new PayablGateway(sandboxClient,Options.Create(new PayablOptions {
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
        try{
            await enabled.InitializeAsync(request with{OrderId=new string('A',31)},
                CancellationToken.None);
            throw new Exception("Oversized merchant order accepted");
        }catch(ArgumentException){}
        Check(sent==1,"merchant order length guard");

        // The SMS adapter cannot send without opt-in and cannot use an
        // arbitrary destination or an untrusted checkout URL.
        var smsCount=0;
        const string smsAccount="ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string smsService="MGbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string smsSecret="fake-twilio-auth-token-for-tests";
        var smsHandler=new FakeHandler(async outgoing=>{
            smsCount++;
            Check(outgoing.Method==HttpMethod.Post,"SMS method");
            Check(outgoing.RequestUri?.AbsoluteUri==
                "https://api.twilio.com/2010-04-01/Accounts/"+smsAccount+"/Messages.json",
                "Twilio API host");
            Check(outgoing.Headers.Authorization?.Scheme=="Basic","Twilio Basic authentication");
            var form=await outgoing.Content!.ReadAsStringAsync();
            Check(form.Contains("MessagingServiceSid="+smsService),"sender is the configured service");
            Check(form.Contains("To=%2B35799123456"),"SMS recipient is E164");
            Check(form.Contains("sandbox.payabl.com"),"SMS contains restricted Payabl link");
            return new HttpResponseMessage(HttpStatusCode.Created){
                Content=new StringContent("""{"sid":"SMcccccccccccccccccccccccccccccccc","status":"queued"}""")
            };
        });
        var smsDisabled=new TwilioPaymentSms(new HttpClient(smsHandler),
            Options.Create(new TwilioPaymentSmsOptions()));
        Check(!smsDisabled.Available,"Twilio disabled by default");
        try{
            await smsDisabled.SendAsync("+35799123456",
                "https://sandbox.payabl.com/pay/example","2026-11-01",1500,CancellationToken.None);
            throw new Exception("Disabled Twilio SMS sent");
        }catch(TwilioPaymentSmsUnavailableException){}
        Check(smsCount==0,"disabled SMS does not call network");
        var twilio=new TwilioPaymentSms(new HttpClient(smsHandler),
            Options.Create(new TwilioPaymentSmsOptions{
                Enabled=true,AccountSid=smsAccount,MessagingServiceSid=smsService,AuthToken=smsSecret
            }));
        Check(twilio.Available,"Twilio test-only opt-in");
        var smsSid=await twilio.SendAsync("+35799123456",
            "https://sandbox.payabl.com/pay/example","2026-11-01",1500,CancellationToken.None);
        Check(smsSid=="SMcccccccccccccccccccccccccccccccc"&&smsCount==1,"queued not delivered");
        foreach(var unsafeUrl in new[]{
            "https://untrusted.invalid/pay/example","http://sandbox.payabl.com/pay/example",
            "https://sandbox.payabl.com.evil.invalid/pay"
        }){
            try{
                await twilio.SendAsync("+35799123456",unsafeUrl,"2026-11-01",1500,CancellationToken.None);
                throw new Exception("Unsafe SMS link accepted");
            }catch(TwilioPaymentSmsUnavailableException){}
        }
        Check(smsCount==1,"unsafe SMS link blocked before network");


        // Source-matched A4 invoice PDF variants. The two render paths have
        // identical geometry but different verified payment status data.
        // No parent-supplied "paid" flag or card details enter the renderer.
        var invoiceId=Guid.Parse("aed1af4a-2df6-4d75-8e3f-6452d4412a18");
        string invoiceJson(string status,string method,string? paidAt,string reference) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                organization_id="lefteria_fc_academy",
                items=new[]{new{
                    id=invoiceId,organization_id="lefteria_fc_academy",
                    player_id="academy-child-1",player_name="Child Test",
                    group_name="U10",payer_name="Test Parent",
                    payer_email="parent@example.invalid",payer_phone="+35799123456",
                    fee_month="2026-11-01",due_date="2026-11-05",
                    issued_at="2026-11-01T12:00:00+00:00",
                    paid_at=paidAt,amount_cents=4500,currency="EUR",
                    status,payment_method=method,transaction_reference=reference,
                    payment_status=status=="paid"?"paid":"not_created"
                }}
            });
        foreach(var sample in new[]{
            invoiceJson("issued","unpaid",null,""),
            invoiceJson("paid","cash","2026-11-05T12:00:00+00:00",Guid.NewGuid().ToString("D")),
            invoiceJson("paid","card","2026-11-06T12:00:00+00:00","104837274")
        }){
            var invoice=AcademyInvoicePdf.Parse(sample);
            var bytes=AcademyInvoicePdf.Render(invoice);
            Check(bytes.Length>500 && bytes.Length<2_000_000 &&
                Encoding.ASCII.GetString(bytes,0,5)=="%PDF-","branded invoice PDF");
        }
        try{
            AcademyInvoicePdf.Parse(invoiceJson("paid","unpaid",null,""));
            throw new Exception("Unverified paid invoice accepted");
        }catch(InvalidOperationException){}
        var twoRows=System.Text.Json.JsonSerializer.Serialize(new{
            organization_id="lefteria_fc_academy",
            items=new[]{System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                invoiceJson("issued","unpaid",null,"")).GetProperty("items")[0],
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                invoiceJson("issued","unpaid",null,"")).GetProperty("items")[0]}
        });
        try{
            AcademyInvoicePdf.Parse(twoRows);
            throw new Exception("Duplicate fee in PDF selection");
        }catch(InvalidOperationException){}

        var rejected = new PayablGateway(
            new HttpClient(new FakeHandler(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://untrusted.invalid/steal") }
                }))),
            Options.Create(new PayablOptions {
                Enabled=true,Sandbox=true,MerchantId="unit_test",
                Secret=secret,PublicOrigin="https://os.example.test"
            }));
        try { await rejected.InitializeAsync(request,CancellationToken.None);
              throw new Exception("Redirected provider response accepted"); }
        catch(PayablUnavailableException) { }

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
