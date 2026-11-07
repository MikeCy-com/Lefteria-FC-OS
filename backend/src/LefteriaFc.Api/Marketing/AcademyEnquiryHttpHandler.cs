using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Marketing;

namespace LefteriaFc.Api.Marketing;

/// <summary>
/// Unmapped request handler: deliberately NOT registered in Program.cs.
/// An independently reviewed route activation must verify proxy trust,
/// execute grants, data retention, updated OpenAPI and complete HTTP tests.
/// </summary>
public static class AcademyEnquiryHttpHandler
{
    private const int MaxJsonBytes = 8192;
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<IResult> HandleAsync(
        HttpContext context, IConfiguration configuration,
        AcademyLeadSubmissionService service, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";

        if (!string.Equals(configuration["AcademyEnquiry:Enabled"], "true", StringComparison.Ordinal))
            return Unavailable();

        if (context.Request.Headers.Origin.Count != 1 ||
            !AcademyEnquiryOriginPolicy.IsAllowed(
                context.Request.Headers.Origin[0],
                configuration["AcademyEnquiry:AllowedOrigin"]))
            return Error(403, "origin_denied");

        if (!AcademyEnquiryActivationGate.TryCreateFingerprint(
                configuration["AcademyEnquiry:Enabled"],
                configuration["AcademyEnquiry:FingerprintKey"],
                context.Connection.RemoteIpAddress, out var fingerprint) || fingerprint is null)
            return Unavailable();

        if (!context.Request.HasJsonContentType())
            return Error(415, "json_required");

        if (context.Request.ContentLength is > MaxJsonBytes)
            return Error(413, "payload_too_large");

        // A bounded read protects even chunked requests with no Content-Length.
        var buffer = new byte[MaxJsonBytes + 1];
        var used = 0;
        while (used < buffer.Length)
        {
            var count = await context.Request.Body.ReadAsync(
                buffer.AsMemory(used, buffer.Length - used), ct);
            if (count == 0) break;
            used += count;
        }
        if (used > MaxJsonBytes)
            return Error(413, "payload_too_large");

        AcademyLeadInput? input;
        try
        {
            input = JsonSerializer.Deserialize<AcademyLeadInput>(
                buffer.AsSpan(0, used), StrictJson);
        }
        catch (JsonException)
        {
            return Error(400, "invalid_json");
        }
        if (input is null) return Error(400, "invalid_json");

        AcademyLeadSubmissionResult outcome;
        try
        {
            outcome = await service.SubmitAsync(
                input, "lefteria_fc_academy", fingerprint, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }

        return outcome.Status switch
        {
            AcademyLeadSubmissionStatus.Accepted =>
                Results.Json(new AcademyEnquiryResponse("received"), statusCode: 202),
            AcademyLeadSubmissionStatus.Invalid =>
                Error(400, outcome.ErrorCode ?? "invalid_lead"),
            AcademyLeadSubmissionStatus.Denied => Error(403, "organization_denied"),
            AcademyLeadSubmissionStatus.RateLimited => Error(429, "rate_limited"),
            _ => Unavailable()
        };
    }

    private static IResult Unavailable() => Error(503, "enquiry_unavailable");
    private static IResult Error(int status, string code) =>
        Results.Json(new AcademyEnquiryResponse(code), statusCode: status);
}

public sealed record AcademyEnquiryResponse(string Code);
