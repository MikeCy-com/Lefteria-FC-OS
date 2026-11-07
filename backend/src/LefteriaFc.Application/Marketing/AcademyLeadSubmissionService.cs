namespace LefteriaFc.Application.Marketing;

public enum AcademyLeadStoreResult { Stored, RateLimited, Unavailable }
public enum AcademyLeadSubmissionStatus { Accepted, Invalid, Denied, RateLimited, Unavailable }

public sealed record AcademyLeadSubmissionResult(
    AcademyLeadSubmissionStatus Status,string? ErrorCode = null);

// The persistence implementation must enforce durable per-fingerprint throttling,
// isolated Academy scope, consent provenance and a committed insert before
// returning Stored. No HTTP route or database grant is activated here.
public interface IAcademyLeadWriter
{
    Task<AcademyLeadStoreResult> WriteAsync(
        ValidatedAcademyLead lead,string organization,string trustedFingerprint,CancellationToken ct);
}

public sealed class AcademyLeadSubmissionService(IAcademyLeadWriter writer)
{
    private const string AcademyOrganization = "lefteria_fc_academy";

    public async Task<AcademyLeadSubmissionResult> SubmitAsync(
        AcademyLeadInput input,string organization,string? trustedFingerprint,CancellationToken ct)
    {
        // Only the fixed Academy route can submit here; caller-selected FC or
        // organization aliases can never widen this service's write scope.
        if(organization!=AcademyOrganization)
            return new(AcademyLeadSubmissionStatus.Denied,"organization_denied");

        // A future HTTP adapter must supply an HMAC digest of trusted request
        // context, never a browser-supplied header, IP string or cookie.
        if(trustedFingerprint is null || trustedFingerprint.Length!=64 ||
           trustedFingerprint.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return new(AcademyLeadSubmissionStatus.Unavailable,"throttle_unavailable");

        if(!AcademyLeadPolicy.TryValidate(input,out var validated,out var error) ||
           validated is null)
            return new(AcademyLeadSubmissionStatus.Invalid,error ?? "invalid_lead");

        var stored=await writer.WriteAsync(validated,AcademyOrganization,trustedFingerprint,ct);
        return stored switch
        {
            AcademyLeadStoreResult.Stored =>
                new(AcademyLeadSubmissionStatus.Accepted),
            AcademyLeadStoreResult.RateLimited =>
                new(AcademyLeadSubmissionStatus.RateLimited,"rate_limited"),
            _ => new(AcademyLeadSubmissionStatus.Unavailable,"lead_unavailable")
        };
    }
}
