using LefteriaFc.Api.Marketing;
using SkiaSharp;
using System.Text;
using LefteriaFc.Api.Release;
using Microsoft.AspNetCore.Http;

namespace LefteriaFc.Identity.IntegrationTests;

internal static class AcademyMvpBoundaryChecks
{
    public static void Run()
    {
        VerifyRegistrationPdf();
        Check.Equal(true,AcademyMvpBoundary.ValidMode(null),
            "Unset optional Academy MVP flag should preserve normal development.");
        Check.Equal(true,AcademyMvpBoundary.ValidMode("true"),
            "Exactly true must enable pilot protection.");
        Check.Equal(true,AcademyMvpBoundary.ValidMode("false"),
            "Exactly false must preserve the full development environment.");
        foreach (var invalid in new[]{"TRUE","False","1","yes","", "pilot"})
            Check.Equal(false,AcademyMvpBoundary.ValidMode(invalid),
                "Unreviewed optional-feature setting was silently accepted.");
        Check.Equal(true,AcademyMvpBoundary.Disabled("true"),
            "Exact opt-in was not recognized.");
        Check.Equal(false,AcademyMvpBoundary.Disabled("false"),
            "Disabled pilot accidentally blocked normal development.");

        foreach (var path in new[]
        {
            "/api/admin/pos","/api/admin/pos/sites",
            "/api/admin/pos/cashier/registers",
            "/api/admin/finance/payabl/payment-requests",
            "/api/admin/finance/payabl/payment-requests/any",
            "/api/payabl/webhooks/00000000-0000-0000-0000-000000000001/demo",
            "/api/mobile/hub/fan-subscription",
            "/api/mobile/hub/fan-subscription/plans",
            "/api/mobile/hub/fan-subscription/checkout",
            "/api/mobile/hub/fan-learning/next",
            "/api/admin/finance/payabl/training-fees/00000000-0000-0000-0000-000000000001/sms"
        })
            Check.Equal(true,AcademyMvpBoundary.IsOptionalEndpoint(new PathString(path)),
                "Optional unverified paid/checkout endpoint stayed enabled: "+path);

        foreach (var path in new[]
        {
            "/api/health/live","/api/health/ready","/api/mobile/family",
            "/api/mobile/finance/training-fees",
            "/api/admin/finance/payabl/status",
            "/api/admin/finance/payabl/training-fees",
            "/api/admin/finance/payabl/training-fees/sample",
            "/api/admin/finance/payabl/invoices",
            "/api/admin/football/audiences",
            "/api/admin/football/groups/with-profile",
            "/api/admin/academy/attendance",
            "/api/mobile/hub/player-learning/next",
            "/api/admin/possessions","/api/mobile/hub/fan-subscriptions",
            "/api/payable","/api/admin/finance/payabl/payment-requests-other"
        })
            Check.Equal(false,AcademyMvpBoundary.IsOptionalEndpoint(new PathString(path)),
                "Core Academy workflow or nonmatching endpoint was blocked: "+path);
    }

    // Render synthetic Greek text and a synthetic signature, never real child data.
    private static void VerifyRegistrationPdf()
    {
        using var bitmap=new SKBitmap(320,80);
        using(var canvas=new SKCanvas(bitmap))
        using(var paint=new SKPaint{Color=SKColors.Black,StrokeWidth=3,IsAntialias=true})
        {
            canvas.Clear(SKColors.White);
            for(var i=0;i<20;i++)
                canvas.DrawLine(12+i*10,20+i%3*12,22+i*10,45-i%4*5,paint);
        }
        using var image=SKImage.FromBitmap(bitmap);
        using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        var form=new AcademyRegistrationRequest {
            PlayerFirstName="Δοκιμή",PlayerLastName="Αθλητής",PlayerDob="2015-03-04",
            PlayerGender="male",PlayerAddress="Δοκιμαστική οδός",PlayerCity="Λεμεσός",
            ParentName="Δοκιμή Κηδεμόνα",ParentRelationship="Κηδεμόνας",
            ParentPhone="35799000000",ParentEmail="fixture@example.invalid",
            EmergencyName="Δοκιμή Επαφής",EmergencyPhone="35799000001",
            KoaHealthCardStatus="yes",PreferredNumber="19",PreferredPrintName="FIXTURE",
            HasAllergies=true,AllergiesDetails=new string('α',1500),
            ConsentParticipation=true,ConsentMedicalAuth=true,ConsentGdpr=true,
            ConsentMedia="no",ConsentLiability=true,ConsentFinancial=true,
            PaymentMethod="cash",SignatureDate="2026-11-28",
            SignatureData="data:image/png;base64,"+Convert.ToBase64String(encoded.ToArray())
        };
        var id=Guid.NewGuid();
        var pdf=AcademyRegistrationPdf.Render(form,id,"fixture-policy",2,100,
            new DateTimeOffset(2026,11,28,12,0,0,TimeSpan.Zero));
        Check.Equal(true,pdf.Length is >500 and <2_000_000 &&
            Encoding.ASCII.GetString(pdf,0,5)=="%PDF-",
            "Registration renderer did not create a bounded PDF.");
        form.SignatureData="data:image/png;base64,invalid";
        try
        {
            AcademyRegistrationPdf.Render(form,id,"fixture-policy",2,100,DateTimeOffset.UtcNow);
            throw new InvalidOperationException("Malformed signature was accepted.");
        }
        catch(InvalidDataException) { }
    }

}
