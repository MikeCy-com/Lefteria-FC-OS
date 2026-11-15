using System.Text.RegularExpressions;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Independently authorized, manual evidence-only supplier payment review.
/// No external payment execution, bank verification or GL posting.
/// </summary>
public static class FinanceSupplierPaymentEndpoints
{
    public sealed record SupplierPaymentRequest(
        Guid ClientRequestId, Guid BillId, long AmountCents,
        DateOnly? PaymentDate, string? PaymentMethod, string? EvidenceReference);

    public sealed record SupplierPaymentReview(
        bool Approve, string? ReviewNote);

    private static bool Clean(string? value, int min, int max) =>
        value is not null && value.Length is >= 1
        && value.Length >= min && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);

    public static IEndpointRouteBuilder MapAdminSupplierPayments(
        this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/admin/finance/supplier-payments")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Supplier payment evidence and four-eyes review");

        routes.MapGet("", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, false, ct);
            var selected = FinanceSupplierBillEndpoints.Month(month);
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT finance.read_admin_supplier_payment_register(" +
                "@digest,@owner,@month)::text", db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("month", NpgsqlDbType.Date, selected);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("ReadAdminSupplierPaymentEvidence")
          .ExcludeFromDescription();

        routes.MapPost("", async (
            string? organization_id, SupplierPaymentRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            var localToday = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Nicosia")).DateTime);
            if (body.ClientRequestId == Guid.Empty || body.BillId == Guid.Empty
                || body.AmountCents is < 1 or > 1000000000
                || body.PaymentDate is null
                || body.PaymentDate.Value.Year is < 2020 or > 2100
                || body.PaymentDate.Value > localToday
                || body.PaymentMethod is not
                    ("bank_transfer" or "cash" or "external_card")
                || !Clean(body.EvidenceReference, 6, 140))
                throw new WebAdminException(422, "supplier_payment_evidence_invalid",
                    "Select a verified invoice and provide a past or current payment date, amount and evidence reference.");

            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.create_admin_supplier_payment_request(
                  @digest,@owner,@request,@bill,@cents,@date,@method,@evidence)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("request", body.ClientRequestId);
            cmd.Parameters.AddWithValue("bill", body.BillId);
            cmd.Parameters.AddWithValue("cents", body.AmountCents);
            cmd.Parameters.AddWithValue("date", NpgsqlDbType.Date, body.PaymentDate.Value);
            cmd.Parameters.AddWithValue("method", body.PaymentMethod!);
            cmd.Parameters.AddWithValue("evidence", body.EvidenceReference!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("CreateAdminSupplierPaymentEvidence")
          .Accepts<SupplierPaymentRequest>("application/json")
          .ExcludeFromDescription();

        routes.MapPost("/{requestId:guid}/review", async (
            Guid requestId, string? organization_id, SupplierPaymentReview body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await FinanceSupplierBillEndpoints.Verify(
                organization_id, auth, context, true, ct);
            var session = await auth.CurrentAsync(cookie, ct);
            if (session?.User.Role != "super_admin")
                throw new WebAdminException(403, "supplier_payment_review_denied",
                    "Only a different, authorized Super Admin may review payment evidence.");
            if (requestId == Guid.Empty || !Clean(body.ReviewNote, 8, 180))
                throw new WebAdminException(422, "supplier_payment_review_invalid",
                    "Provide a documented independent review decision.");

            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.review_admin_supplier_payment_request(
                  @digest,@owner,@request,@approve,@note)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("request", requestId);
            cmd.Parameters.AddWithValue("approve", body.Approve);
            cmd.Parameters.AddWithValue("note", body.ReviewNote!);
            return await FinanceSupplierBillEndpoints.Run(cmd, ct);
        }).WithName("ReviewAdminSupplierPaymentEvidence")
          .Accepts<SupplierPaymentReview>("application/json")
          .ExcludeFromDescription();

        return endpoints;
    }
}
