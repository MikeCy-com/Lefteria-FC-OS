using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Football;

/// <summary>
/// Financial location attribution is an immutable VERIFIED snapshot, not a
/// player-current-group guess. No invoice or payment ledger mutation.
/// </summary>
public static class AdminAcademyLocationFinanceEndpoints
{
    public sealed record FinanceAllocation(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("issue_id")] Guid IssueId,
        [property: JsonPropertyName("location_id")] Guid LocationId,
        [property: JsonPropertyName("evidence")] string? Evidence);

    private static DateOnly ValidateMonth(string? month)
    {
        if (month is null || !Regex.IsMatch(month, @"^(20[2-9][0-9]|2100)-(0[1-9]|1[0-2])$")
            || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new WebAdminException(422, "academy_finance_month_invalid",
                "Select a month from 2020 to 2100.");
        return date;
    }

    public static IEndpointRouteBuilder MapAdminAcademyLocationFinance(
        this IEndpointRouteBuilder endpoints)
    {
        var routes=endpoints.MapGroup("/api/admin/football/territories/finance")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Academy territory finance");

        routes.MapGet("", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie=await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id,context,false,ct);
            var selected=ValidateMonth(month);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT finance.read_admin_academy_location_finance(@digest,@owner,@month)::text",
                connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("month",NpgsqlDbType.Date,selected);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("ReadAdminAcademyLocationFinance")
          .ExcludeFromDescription();

        routes.MapGet("/unallocated", async (
            string? organization_id, string? month,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie=await AdminFootballTerritoryEndpoints.Verify(
                auth, organization_id,context,false,ct);
            var selected=ValidateMonth(month);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT finance.read_admin_academy_unallocated_issues(@digest,@owner,@month)::text",
                connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("month",NpgsqlDbType.Date,selected);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("ReadAdminAcademyUnallocatedIssues")
          .ExcludeFromDescription();

        routes.MapPost("/allocate", async (
            string? organization_id, FinanceAllocation input,
            WebAdminService auth, WebAdminConnections connections,
            IWebAdminSecrets secrets, HttpContext context, CancellationToken ct) =>
        {
            var cookie=await AdminFootballTerritoryEndpoints.Verify(
                auth,organization_id,context,true,ct);
            if (input.Kind is not ("training_fee" or "general_charge") ||
                input.IssueId==Guid.Empty || input.LocationId==Guid.Empty ||
                input.Evidence is null || input.Evidence.Length is < 5 or > 160 ||
                input.Evidence!=input.Evidence.Trim() ||
                input.Evidence.Any(char.IsControl))
                throw new WebAdminException(422,"academy_finance_evidence_invalid",
                    "Provide an issue, an exact site and an evidence reference.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT finance.assign_admin_academy_issue_site(" +
                "@digest,@owner,@kind,@issue,@location,@evidence)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("kind",input.Kind);
            command.Parameters.AddWithValue("issue",input.IssueId);
            command.Parameters.AddWithValue("location",input.LocationId);
            command.Parameters.AddWithValue("evidence",input.Evidence);
            return await AdminFootballTerritoryEndpoints.Execute(command,ct);
        }).WithName("AllocateAdminAcademyIssueToVerifiedSite")
          .Accepts<FinanceAllocation>("application/json")
          .ExcludeFromDescription();

        return endpoints;
    }
}
