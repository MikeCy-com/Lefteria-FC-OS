using LefteriaFc.Application.Marketing;

namespace LefteriaFc.PolicyChecks;

internal static class AcademyLeadSubmissionChecks
{
    private const string Academy="lefteria_fc_academy";
    private static readonly string Fingerprint=new('a',64);
    private static readonly AcademyLeadInput Input=new(
        "Verified Guardian","+35799000000",null,9,null,null,null,true,false);

    public static IEnumerable<(string Name,Action Check)> Checks()
    {
        yield return ("Academy submission denies wrong organization before invoking writer",()=>{
            var writer=new FakeWriter();
            var outcome=Submit(writer,Input,"lefteria_fc",Fingerprint);
            Require(outcome.Status==AcademyLeadSubmissionStatus.Denied && writer.Calls==0);
        });
        yield return ("Academy submission requires server generated HMAC fingerprint",()=>{
            var writer=new FakeWriter();
            foreach(var key in new string?[]{null,"","attacker",new string('A',64),new string('z',64)})
                Require(Submit(writer,Input,Academy,key).Status==AcademyLeadSubmissionStatus.Unavailable);
            Require(writer.Calls==0);
        });
        yield return ("Academy submission rejects missing GDPR without storage",()=>{
            var writer=new FakeWriter();
            var outcome=Submit(writer,Input with {ConsentGdpr=false},Academy,Fingerprint);
            Require(outcome.Status==AcademyLeadSubmissionStatus.Invalid &&
                outcome.ErrorCode=="consent_required" && writer.Calls==0);
        });
        yield return ("Academy submission only confirms after durable writer returns Stored",()=>{
            var writer=new FakeWriter {Result=AcademyLeadStoreResult.Stored};
            var outcome=Submit(writer,Input,Academy,Fingerprint);
            Require(outcome.Status==AcademyLeadSubmissionStatus.Accepted &&
                writer.Calls==1 && writer.Organization==Academy &&
                writer.Fingerprint==Fingerprint && writer.Lead?.Name=="Verified Guardian");
        });
        yield return ("Academy submission preserves throttle and unavailable failures",()=>{
            foreach(var (repository,expected) in new[]{
                (AcademyLeadStoreResult.RateLimited,AcademyLeadSubmissionStatus.RateLimited),
                (AcademyLeadStoreResult.Unavailable,AcademyLeadSubmissionStatus.Unavailable)
            }){
                var writer=new FakeWriter {Result=repository};
                Require(Submit(writer,Input,Academy,Fingerprint).Status==expected && writer.Calls==1);
            }
        });
    }

    private static AcademyLeadSubmissionResult Submit(
        FakeWriter writer,AcademyLeadInput input,string organization,string? fingerprint)=>
        new AcademyLeadSubmissionService(writer).SubmitAsync(
            input,organization,fingerprint,CancellationToken.None).GetAwaiter().GetResult();

    private static void Require(bool result)
    {
        if(!result)throw new InvalidOperationException("Academy lead submission service contract failed.");
    }

    private sealed class FakeWriter : IAcademyLeadWriter
    {
        public int Calls {get;private set;}
        public string? Organization {get;private set;}
        public string? Fingerprint {get;private set;}
        public ValidatedAcademyLead? Lead {get;private set;}
        public AcademyLeadStoreResult Result {get;set;}=AcademyLeadStoreResult.Unavailable;

        public Task<AcademyLeadStoreResult> WriteAsync(
            ValidatedAcademyLead lead,string organization,string trustedFingerprint,CancellationToken ct)
        {
            Calls++;
            Lead=lead;
            Organization=organization;
            Fingerprint=trustedFingerprint;
            return Task.FromResult(Result);
        }
    }
}
