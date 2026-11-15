using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Supplier-issued credit adjustments affect manual payable balances only
/// after review by another authorized Super Admin. They are not cash refunds,
/// Payabl activity, VAT credits, or posted general-ledger entries.
/// </summary>
public static class FinanceSupplierCreditEndpoints
{
    public sealed record SupplierCreditRequest(
        Guid ClientRequestId, Guid BillId, string? CreditReference,
        DateOnly? CreditDate, long AmountCents, string? Reason);

    public sealed record SupplierCreditDecision(bool? Approve, string? ReviewNote);

    private static bool Clean(string? value, int min, int max) =>
        value is not null && value.Length >= min && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);

    public static IEndpointRouteBuilder MapAdminSupplierCredits(
        this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/admin/finance/supplier-credits")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Private manual supplier credit notes");

        routes.MapPost("", async (
            string? organization_id, SupplierCreditRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            var today = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Nicosia")).DateTime);
            if (body.ClientRequestId == Guid.Empty || body.BillId == Guid.Empty
                || !Clean(body.CreditReference, 4, 80)
                || !Clean(body.Reason, 8, 180)
                || body.AmountCents is < 1 or > 1000000000
                || body.CreditDate is null || body.CreditDate.Value.Year < 2020
                || body.CreditDate.Value > today)
                throw new WebAdminException(422, "supplier_credit_invalid",
                    "Choose a valid supplier invoice and provide the real credit-note reference, date, amount and reason.");

            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.create_admin_supplier_credit_request(
                  @digest,@owner,@request,@bill,@reference,@date,@cents,@reason)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("request", body.ClientRequestId);
            cmd.Parameters.AddWithValue("bill", body.BillId);
            cmd.Parameters.AddWithValue("reference", body.CreditReference!);
            cmd.Parameters.AddWithValue("date", NpgsqlDbType.Date, body.CreditDate.Value);
            cmd.Parameters.AddWithValue("cents", body.AmountCents);
            cmd.Parameters.AddWithValue("reason", body.Reason!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("RequestAdminSupplierCredit")
          .Accepts<SupplierCreditRequest>("application/json")
          .ExcludeFromDescription();

        routes.MapPost("/{requestId:guid}/review", async (
            Guid requestId, string? organization_id, SupplierCreditDecision body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            var session = await auth.CurrentAsync(cookie, ct);
            if (session?.User.Role != "super_admin")
                throw new WebAdminException(403, "supplier_credit_review_denied",
                    "Only an independently authorized Super Admin may approve a supplier credit.");
            if (requestId == Guid.Empty || body.Approve is null
                || !Clean(body.ReviewNote, 8, 180))
                throw new WebAdminException(422, "supplier_credit_review_invalid",
                    "Provide an explicit decision and documented independent supplier-credit review.");

            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.review_admin_supplier_credit_request(
                  @digest,@owner,@request,@approve,@note)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("request", requestId);
            cmd.Parameters.AddWithValue("approve", body.Approve.Value);
            cmd.Parameters.AddWithValue("note", body.ReviewNote!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("ReviewAdminSupplierCredit")
          .Accepts<SupplierCreditDecision>("application/json")
          .ExcludeFromDescription();

        return endpoints;
    }
}
