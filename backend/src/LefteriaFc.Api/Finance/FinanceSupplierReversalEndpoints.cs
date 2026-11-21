using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Undo the accounting impact of an incorrectly approved *manual* supplier
/// payment or credit. Never executes refunds, bank transfers or GL postings.
/// </summary>
public static class FinanceSupplierReversalEndpoints
{
    public sealed record SupplierApprovalReversalRequest(
        Guid ClientRequestId, string? TargetKind, Guid TargetId, string? Reason);
    public sealed record SupplierApprovalReversalReview(bool? Approve, string? ReviewNote);

    private static bool Clean(string? value, int min, int max) =>
        value is not null && value.Length >= min && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);

    public static IEndpointRouteBuilder MapAdminSupplierReversals(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/finance/supplier-reversals")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Manual supplier approval correction and independent review");

        group.MapPost("", async (
            string? organization_id, SupplierApprovalReversalRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            if (body.ClientRequestId == Guid.Empty || body.TargetId == Guid.Empty
                || body.TargetKind is not ("payment" or "credit")
                || !Clean(body.Reason, 12, 180))
                throw new WebAdminException(422, "supplier_reversal_invalid",
                    "Select an approved manual supplier record and give a specific correction reason.");
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.create_admin_supplier_approval_reversal(
                  @digest,@owner,@client,@kind,@target,@reason)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("client", body.ClientRequestId);
            cmd.Parameters.AddWithValue("kind", body.TargetKind);
            cmd.Parameters.AddWithValue("target", body.TargetId);
            cmd.Parameters.AddWithValue("reason", body.Reason!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("RequestAdminSupplierApprovalReversal")
          .Accepts<SupplierApprovalReversalRequest>("application/json")
          .ExcludeFromDescription();

        group.MapPost("/{reversalId:guid}/review", async (
            Guid reversalId, string? organization_id, SupplierApprovalReversalReview body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            var session = await auth.CurrentAsync(cookie, ct);
            if (session?.User.Role != "super_admin")
                throw new WebAdminException(403, "supplier_reversal_review_denied",
                    "Only an independently authorized Super Admin can review corrections.");
            if (reversalId == Guid.Empty || body.Approve is null
                || !Clean(body.ReviewNote, 12, 180))
                throw new WebAdminException(422, "supplier_reversal_review_invalid",
                    "Document the second review and explicitly approve or reject it.");
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.review_admin_supplier_approval_reversal(
                  @digest,@owner,@reversal,@approve,@note)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("reversal", reversalId);
            cmd.Parameters.AddWithValue("approve", body.Approve.Value);
            cmd.Parameters.AddWithValue("note", body.ReviewNote!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("ReviewAdminSupplierApprovalReversal")
          .Accepts<SupplierApprovalReversalReview>("application/json")
          .ExcludeFromDescription();

        return endpoints;
    }
}
