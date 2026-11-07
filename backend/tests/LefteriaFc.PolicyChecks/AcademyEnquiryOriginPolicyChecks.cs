using LefteriaFc.Application.Marketing;

namespace LefteriaFc.PolicyChecks;

internal static class AcademyEnquiryOriginPolicyChecks
{
    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("Academy enquiry permits only exact configured HTTPS origin",()=>{
            Require(AcademyEnquiryOriginPolicy.IsAllowed(
                "https://lefteriafc.cy","https://lefteriafc.cy"));
            Require(AcademyEnquiryOriginPolicy.IsAllowed(
                "https://srv1649602.hstgr.cloud:8443","https://srv1649602.hstgr.cloud:8443"));
            Require(!AcademyEnquiryOriginPolicy.IsAllowed(
                "https://lefteriafc.cy","https://srv1649602.hstgr.cloud:8443"));
        });
        yield return ("Academy enquiry rejects spoofed origins and invalid authority",()=>{
            const string site="https://lefteriafc.cy";
            foreach(var origin in new string?[]{
                null,"","null"," https://lefteriafc.cy",
                "https://evil.invalid","https://lefteriafc.cy.evil.invalid",
                "https://lefteriafc.cy@evil.invalid",
                "https://evil.invalid@lefteriafc.cy",
                "https://lefteriafc.cy/path",
                "https://lefteriafc.cy/?x=1",
                "https://lefteriafc.cy/#fragment",
                "https://lefteriafc.cy\r\nX-Forwarded-Host:evil.invalid",
                "http://lefteriafc.cy",
                "ftp://lefteriafc.cy"
            })Require(!AcademyEnquiryOriginPolicy.IsAllowed(origin,site));
        });
        yield return ("Academy enquiry allows explicitly configured loopback development origin only",()=>{
            Require(AcademyEnquiryOriginPolicy.IsAllowed("http://localhost:5173","http://localhost:5173"));
            Require(AcademyEnquiryOriginPolicy.IsAllowed("http://127.0.0.1:5000","http://127.0.0.1:5000"));
            Require(!AcademyEnquiryOriginPolicy.IsAllowed("http://example.com","http://example.com"));
            Require(!AcademyEnquiryOriginPolicy.IsAllowed("http://localhost:5173","http://localhost:5174"));
        });
        yield return ("Academy enquiry requires configured origin and bounded headers",()=>{
            Require(!AcademyEnquiryOriginPolicy.IsAllowed("https://lefteriafc.cy",null));
            Require(!AcademyEnquiryOriginPolicy.IsAllowed(null,"https://lefteriafc.cy"));
            Require(!AcademyEnquiryOriginPolicy.IsAllowed(new string('x',513),"https://lefteriafc.cy"));
        });
    }
    private static void Require(bool result)
    {
        if(!result)throw new InvalidOperationException("Academy enquiry origin policy failed.");
    }
}
