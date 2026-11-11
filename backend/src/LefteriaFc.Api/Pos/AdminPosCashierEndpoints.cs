using System.Text;
using System.Text.Json;
using NpgsqlTypes;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Dedicated manager-only register discovery and shift controls.
/// No sale checkout, payment capture, refund execution or public POS access.
/// Requires explicitly approved SQL EXECUTE grants to the independent
/// web-admin role; absent grants, the operations fail closed.
/// </summary>
public static class AdminPosCashierEndpoints
{
    public sealed record OpenShiftRequest(Guid RequestId, long OpeningCashCents);
    public sealed record CloseShiftRequest(Guid RequestId, long CountedCashCents);
    public sealed record CheckoutItem(string ProductId, string VariantId, int Quantity);
    public sealed record PrepareSaleRequest(Guid RequestId, Guid ShiftId, List<CheckoutItem>? Items);
    public sealed record CancelSaleRequest(Guid RequestId);
    public sealed record CashTenderRequest(Guid RequestId, long TenderedCashCents);
    public sealed record ReturnLineRequest(int LineNumber, int Quantity);
    public sealed record ReturnSaleRequest(Guid RequestId, string ReasonCode,
        List<ReturnLineRequest>? Lines);
    public sealed record ReviewReturnRequest(Guid RequestId, bool Approve);
    public sealed record RequestRefundRequest(Guid RequestId, Guid PaymentEventId, long AmountCents);

    public static IEndpointRouteBuilder MapAdminPosCashier(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/pos/cashier")
            .WithTags("POS cashier administration")
            .RequireAuthorization(WebAdminServices.Policy);

        group.MapGet("/registers", async (string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_cashier_registers(@digest,@owner)::text", connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            return await Query(command, ct);
        }).WithName("ReadAdminPosCashierRegisters");

        group.MapPost("/registers/{registerId}/shifts", async (string registerId,
            string? organization_id, OpenShiftRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (!ValidRegister(registerId) || body.RequestId == Guid.Empty ||
                body.OpeningCashCents is < 0 or > 100000000)
                throw new WebAdminException(400, "pos_shift_invalid",
                    "Invalid register or opening cash.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.open_admin_cashier_shift(@digest,@owner,@register,@opening,@request)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("register", registerId);
            command.Parameters.AddWithValue("opening", body.OpeningCashCents);
            command.Parameters.AddWithValue("request", body.RequestId);
            return await Query(command, ct);
        }).WithName("OpenAdminPosCashierShift");

        group.MapPost("/shifts/{shiftId:guid}/close", async (Guid shiftId,
            string? organization_id, CloseShiftRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (shiftId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.CountedCashCents is < 0 or > 100000000)
                throw new WebAdminException(400, "pos_shift_invalid",
                    "Invalid shift or counted cash.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.close_admin_cashier_shift(@digest,@owner,@shift,@counted,@request)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("shift", shiftId);
            command.Parameters.AddWithValue("counted", body.CountedCashCents);
            command.Parameters.AddWithValue("request", body.RequestId);
            return await Query(command, ct);
        }).WithName("CloseAdminPosCashierShift");
        // Reservation-only. Runtime function grant and approved tax policy
        // are deliberately absent, so this route fails closed by default.
        group.MapPost("/registers/{registerId}/sales/prepare", async (string registerId,
            string? organization_id, PrepareSaleRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (!ValidRegister(registerId) || body.ShiftId == Guid.Empty ||
                body.RequestId == Guid.Empty ||
                body.Items is null || body.Items.Count is < 1 or > 30 ||
                body.Items.Any(item =>
                    !ValidRegister(item.ProductId) ||
                    item.VariantId is null || item.VariantId.Length is < 1 or > 60 ||
                    !item.VariantId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') ||
                    item.Quantity is < 1 or > 99) ||
                body.Items.Select(item => (item.ProductId, item.VariantId))
                    .Distinct().Count() != body.Items.Count)
                throw new WebAdminException(400, "pos_sale_items_invalid",
                    "Select 1 to 30 unique products and quantities between 1 and 99.");
            var cart = JsonSerializer.Serialize(body.Items.Select(item => new
            {
                product_id = item.ProductId,
                variant_id = item.VariantId,
                quantity = item.Quantity
            }));
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.prepare_cashier_checkout(@digest,@owner,@register,@shift,@request,@items)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("register", registerId);
            command.Parameters.AddWithValue("shift", body.ShiftId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("items", NpgsqlDbType.Jsonb, cart);
            return await Query(command, ct);
        }).WithName("PrepareAdminPosReservedSale");

        group.MapPost("/sales/{saleId:guid}/cancel", async (
            Guid saleId, string? organization_id, CancelSaleRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (saleId == Guid.Empty || body.RequestId == Guid.Empty)
                throw new WebAdminException(400, "pos_sale_cancel_invalid",
                    "Select a valid sale and cancellation request.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.cancel_unpaid_cashier_sale(@digest,@owner,@sale,@request)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("sale", saleId);
            command.Parameters.AddWithValue("request", body.RequestId);
            return await Query(command, ct);
        }).WithName("CancelAdminPosUnpaidSale");

        // Verified physical cash only. Not a Payabl/card verification
        // endpoint; the underlying fiscal policies and SQL permission are
        // disabled by default in every new OS environment.
        group.MapPost("/sales/{saleId:guid}/tender-cash", async (
            Guid saleId, string? organization_id, CashTenderRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (saleId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.TenderedCashCents is < 1 or > 100000000)
                throw new WebAdminException(400, "pos_cash_tender_invalid",
                    "Select a valid sale, request and counted cash amount.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.settle_exact_cash_sale(@digest,@owner,@sale,@request,@cash)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("sale", saleId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("cash", body.TenderedCashCents);
            return await Query(command, ct);
        }).WithName("TenderVerifiedAdminPosCashSale");

        group.MapGet("/shifts/{shiftId:guid}/reconciliation", async (
            Guid shiftId, string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (shiftId == Guid.Empty)
                throw new WebAdminException(400, "pos_shift_report_invalid",
                    "Select a valid cashier shift.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_cashier_shift_reconciliation(@digest,@owner,@shift)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("shift", shiftId);
            return await Query(command, ct);
        }).WithName("ReadAdminPosCashierReconciliation");

        group.MapGet("/receipts/{receiptId:guid}", async (
            Guid receiptId, string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (receiptId == Guid.Empty)
                throw new WebAdminException(400, "pos_receipt_invalid",
                    "Choose one valid receipt.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_assigned_cashier_receipt(@digest,@owner,@receipt)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("receipt", receiptId);
            return await Query(command, ct);
        }).WithName("ReadAdminPosAssignedCashierReceipt");

        group.MapPost("/sales/{saleId:guid}/returns", async (
            Guid saleId, string? organization_id, ReturnSaleRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (saleId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.ReasonCode is not ("defective" or "wrong_item" or
                    "customer_return" or "other") ||
                body.Lines is null || body.Lines.Count is < 1 or > 30 ||
                body.Lines.Any(x => x.LineNumber is < 1 or > 30 ||
                    x.Quantity is < 1 or > 99) ||
                body.Lines.Select(x => x.LineNumber).Distinct().Count() != body.Lines.Count)
                throw new WebAdminException(400, "pos_return_request_invalid",
                    "Select an original sale, reason and valid return quantities.");
            var lines = JsonSerializer.Serialize(body.Lines.Select(x => new
            {
                line_number = x.LineNumber,
                quantity = x.Quantity
            }));
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.request_assigned_cashier_return(@digest,@owner,@sale,@request,@reason,@lines)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("sale", saleId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("reason", body.ReasonCode);
            command.Parameters.AddWithValue("lines", NpgsqlDbType.Jsonb, lines);
            return await Query(command, ct);
        }).WithName("RequestAdminPosSaleReturn");

        group.MapPost("/returns/{returnId:guid}/review", async (
            Guid returnId, string? organization_id, ReviewReturnRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (returnId == Guid.Empty || body.RequestId == Guid.Empty)
                throw new WebAdminException(400, "pos_return_review_invalid",
                    "Select one valid return and review request.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.review_cashier_return(@digest,@owner,@return,@request,@approve)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("return", returnId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("approve", body.Approve);
            return await Query(command, ct);
        }).WithName("ReviewAdminPosSaleReturn");

        // Pending instruction only: no cash disbursement or Payabl API call.
        group.MapPost("/returns/{returnId:guid}/refund-requests", async (
            Guid returnId, string? organization_id, RequestRefundRequest body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            if (returnId == Guid.Empty || body.RequestId == Guid.Empty ||
                body.PaymentEventId == Guid.Empty ||
                body.AmountCents is < 1 or > 100000000)
                throw new WebAdminException(400, "pos_refund_request_invalid",
                    "Select an approved return and verified original payment.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.request_approved_return_refund(@digest,@owner,@return,@payment,@request,@amount)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("return", returnId);
            command.Parameters.AddWithValue("payment", body.PaymentEventId);
            command.Parameters.AddWithValue("request", body.RequestId);
            command.Parameters.AddWithValue("amount", body.AmountCents);
            return await Query(command, ct);
        }).WithName("RequestAdminPosApprovedReturnRefund");

        // Manager finance checkpoint, not a general ledger posting.
        // Explicit finance:read permission is required in addition to
        // cashier/shop access already checked by Authority.
        group.MapGet("/finance-handoff", async (
            string? organization_id,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var (cookie, owner) = await Authority(auth, organization_id, context, ct);
            var session = await auth.CurrentAsync(cookie, ct);
            if (session is null ||
                !AdminPermissions.Has(session.User.Role, "finance:read"))
                throw new WebAdminException(403, "pos_finance_handoff_access_denied",
                    "Financial reconciliation requires a manager finance grant.");
            await using var connection = await connections.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_finance_handoff(@digest,@owner)::text",
                connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            return await Query(command, ct);
        }).WithName("ReadAdminPosFinanceHandoff");

        return endpoints;
    }

    private static bool ValidRegister(string? value) =>
        value is not null && value.Length is >= 1 and <= 80 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static async Task<(string Cookie, string Owner)> Authority(
        WebAdminService auth, string? organization, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (context.Request.Query["organization_id"].Count != 1 ||
            organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "pos_owner_invalid",
                "Select one valid organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role is not ("manager" or "super_admin") ||
            !AdminPermissions.Has(session.User.Role, "shop:write") ||
            !session.User.Organizations.Any(o => o.Id == organization))
            throw new WebAdminException(403, "pos_cashier_access_denied",
                "Cashier management requires an authorized organization and role.");
        return (cookie!, organization);
    }

    private static async Task<IResult> Query(NpgsqlCommand command, CancellationToken ct)
    {
        try
        {
            var result = await command.ExecuteScalarAsync(ct) as string;
            return result is null ? Results.Problem(statusCode: 503)
                : Results.Content(result, "application/json", Encoding.UTF8, 200);
        }
        catch (PostgresException ex) when (ex.SqlState is
            "42501" or "22023" or "23505" or "23514" or "55000" or "54000" or "40001")
        {
            var status = ex.SqlState switch
            {
                "42501" => 403,
                "22023" => 422,
                "23505" => 409,
                "23514" => 409,
                "54000" => 413,
                "40001" => 409,
                _ => 409
            };
            throw new WebAdminException(status, "pos_cashier_operation_unavailable",
                "The cashier operation could not be completed. Refresh register status.");
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "3F000")
        {
            throw new WebAdminException(503, "pos_cashier_not_configured",
                "Cashier operations are not enabled in this environment.");
        }
    }
}