using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LefteriaFc.Api.Football;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Finance;

/// <summary>
/// Accountant/Super Admin manual supplier-bill register. Recording a supplier
/// invoice never marks it paid, starts Payabl, or posts a general-ledger entry.
/// Site-only managers have a separate redacted aggregate reader.
/// </summary>
public static class FinanceSupplierBillEndpoints
{
    public sealed record SupplierBillCommand(
        Guid ClientRequestId, string? SupplierName, string? InvoiceReference,
        string? Category, string? Description, DateOnly? BillDate,
        long GrossAmountCents, Guid? LocationId);

    private static readonly string[] Categories =
    [
        "facilities","equipment","travel","matchday",
        "staff_services","administration","other"
    ];

    internal static DateOnly Month(string? input)
    {
        if (input is null ||
            !Regex.IsMatch(input, @"^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$") ||
            !DateOnly.TryParseExact(input + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new WebAdminException(422, "supplier_bill_month_invalid",
                "Select an accounting month from 2020 through 2100.");
        return value;
    }

    private static bool Clean(string? input, int min, int max) =>
        input is not null && input.Length >= min && input.Length <= max
        && input == input.Trim() && !input.Any(char.IsControl);

    internal static async Task<string> Verify(
        string? owner, WebAdminService auth, HttpContext context,
        bool write, CancellationToken ct)
    {
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            context.Request.Query["organization_id"].Count != 1)
            throw new WebAdminException(422, "supplier_bill_organization_invalid",
                "Choose exactly one organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await auth.CurrentAsync(cookie, ct);
        if (session is null)
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (session.User.Role is not ("super_admin" or "accountant") ||
            !AdminPermissions.Has(session.User.Role, write ? "finance:write" : "finance:read") ||
            !session.User.Organizations.Any(o => o.Id == owner))
            throw new WebAdminException(403, "supplier_bill_permission_denied",
                "Your account is not assigned to this organization's accounting.");
        return cookie!;
    }

    internal static async Task<IResult> Run(
        NpgsqlCommand command, CancellationToken ct)
    {
        try
        {
            var json = await command.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode:503)
                : Results.Content(json, "application/json", Encoding.UTF8, 200);
        }
        catch (PostgresException e) when (e.SqlState == "42501")
        {
            throw new WebAdminException(403, "supplier_bill_permission_denied",
                "Account, organization or Academy location permission was revoked.");
        }
        catch (PostgresException e) when (e.SqlState == "23505")
        {
            throw new WebAdminException(409, "supplier_bill_duplicate",
                "The supplier invoice reference or request ID is already recorded differently.");
        }
        catch (PostgresException e) when (e.SqlState is "23503" or "23514")
        {
            throw new WebAdminException(409, "supplier_bill_conflict",
                "The selected Academy location or supplier bill has changed.");
        }
        catch (PostgresException e) when (e.SqlState is "22023" or "22007" or "22008" or "22001")
        {
            throw new WebAdminException(422, "supplier_bill_invalid",
                "Check the invoice date, amount, reference and Academy location.");
        }
        catch (PostgresException e) when (e.SqlState is "42883" or "3F000")
        {
            throw new WebAdminException(503, "supplier_bill_unavailable",
                "The isolated supplier-bill register has not been enabled.");
        }
    }

    public static IEndpointRouteBuilder MapAdminSupplierBills(
        this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/admin/finance/supplier-bills")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Private supplier bill register");

        routes.MapGet("", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Verify(organization_id, auth, context, false, ct);
            var selected = Month(month);
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT finance.read_admin_supplier_bills(@digest,@owner,@month)::text", db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("month", NpgsqlDbType.Date, selected);
            return await Run(cmd, ct);
        }).WithName("ReadAdminSupplierBills").ExcludeFromDescription();

        routes.MapPost("", async (
            string? organization_id, SupplierBillCommand body,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Verify(organization_id, auth, context, true, ct);
            if (body.ClientRequestId == Guid.Empty ||
                !Clean(body.SupplierName, 2, 140) ||
                !Clean(body.InvoiceReference, 1, 80) ||
                !Clean(body.Description, 4, 300) ||
                !Categories.Contains(body.Category, StringComparer.Ordinal) ||
                body.BillDate is null ||
                body.BillDate.Value.Year is < 2020 or > 2100 ||
                body.GrossAmountCents is < 1 or > 1000000000 ||
                (organization_id == "lefteria_fc_academy" &&
                    (body.LocationId is null || body.LocationId == Guid.Empty)) ||
                (organization_id == "lefteria_fc" && body.LocationId is not null))
                throw new WebAdminException(422, "supplier_bill_invalid",
                    "Enter one supplier invoice with a valid amount and exact Academy location.");

            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.create_admin_supplier_bill(
                  @digest,@owner,@request,@supplier,@reference,@category,
                  @description,@bill_date,@cents,@location)::text
                """, db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("request", body.ClientRequestId);
            cmd.Parameters.AddWithValue("supplier", body.SupplierName!);
            cmd.Parameters.AddWithValue("reference", body.InvoiceReference!);
            cmd.Parameters.AddWithValue("category", body.Category!);
            cmd.Parameters.AddWithValue("description", body.Description!);
            cmd.Parameters.AddWithValue("bill_date", NpgsqlDbType.Date, body.BillDate.Value);
            cmd.Parameters.AddWithValue("cents", body.GrossAmountCents);
            cmd.Parameters.AddWithValue("location", NpgsqlDbType.Uuid,
                (object?)body.LocationId ?? DBNull.Value);
            return await Run(cmd, ct);
        }).WithName("RecordAdminSupplierBill")
          .Accepts<SupplierBillCommand>("application/json")
          .ExcludeFromDescription();

        var sites = endpoints.MapGroup("/api/admin/football/territories/finance/supplier-bills")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Academy manager supplier obligation totals");
        sites.MapGet("", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id, context, false, ct);
            if (organization_id != "lefteria_fc_academy")
                throw new WebAdminException(422, "supplier_bill_scope_invalid",
                    "Only Academy sites have this aggregate.");
            var selected = Month(month);
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT finance.read_admin_supplier_site_totals(@digest,@owner,@month)::text",
                db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("month", NpgsqlDbType.Date, selected);
            return await Run(cmd, ct);
        }).WithName("ReadAcademySupplierBillLocationTotals")
          .ExcludeFromDescription();
        // The legacy /supplier-bills endpoint remains a gross-obligation
        // report. The new site-only endpoint adds manually reviewed credits
        // and payment evidence without exposing supplier identities.
        var payables = endpoints.MapGroup(
                "/api/admin/football/territories/finance/supplier-payables")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Academy manager site-only supplier payable positions");
        payables.MapGet("", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id, context, false, ct);
            if (organization_id != "lefteria_fc_academy" ||
                context.Request.Query["organization_id"].Count != 1)
                throw new WebAdminException(422, "supplier_site_payables_scope_invalid",
                    "Select exactly the Academy organization for site-level payables.");
            var selected = Month(month);
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT finance.read_admin_supplier_site_payables(" +
                "@digest,@owner,@month)::text", db);
            cmd.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner", organization_id!);
            cmd.Parameters.AddWithValue("month", NpgsqlDbType.Date, selected);
            return await Run(cmd, ct);
        }).WithName("ReadAcademySupplierLocationPayables")
          .ExcludeFromDescription();
        return endpoints;
    }
}
