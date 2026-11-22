using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Draft-only VAT classifications for a specific existing product variant.
/// No endpoint here may approve, post, issue a receipt, refund or enable Payabl.
/// </summary>
public static class AdminPosVatSettingsEndpoints
{
    public sealed record VatProposalRequest(Guid RequestId, string? ProductId,
        string? VariantId, string? TreatmentCode, DateOnly? EffectiveFrom,
        string? AuthorityReference, string? Justification);
    public sealed record VatReviewRequest(bool? Approve, string? ReviewNote);

    private static readonly string[] Codes =
        ["standard_19","reduced_9","reduced_5","reduced_3","zero_0","exempt"];

    private static bool Slug(string? value, int max) =>
        value is {Length: > 0} && value.Length <= max &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static bool Note(string? value) =>
        value is {Length: >= 10 and <= 500} && value==value.Trim() &&
        !value.Any(char.IsControl);

    public static IEndpointRouteBuilder MapAdminPosVatSettings(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/finance/pos-vat")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Private POS VAT classification proposals");

        group.MapGet("", async (string? organization_id, WebAdminService admin,
            WebAdminConnections db, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie=await Verify(admin,context,organization_id,ct);
            await using var connection=await db.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_vat_proposals(@digest,@owner)::text",
                connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            return await ReadJson(command,ct);
        }).WithName("ReadAdminPosVatProposals").ExcludeFromDescription();

        group.MapPost("/proposals", async (string? organization_id,
            VatProposalRequest body, WebAdminService admin,
            WebAdminConnections db,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Verify(admin,context,organization_id,ct);
            if(body.RequestId==Guid.Empty || !Slug(body.ProductId,80) ||
               !Slug(body.VariantId,60) || !Codes.Contains(body.TreatmentCode) ||
               body.EffectiveFrom is null ||
               body.EffectiveFrom.Value.Year is < 2020 or > 2100 ||
               !Note(body.AuthorityReference)||!Note(body.Justification))
                throw new WebAdminException(422,"pos_vat_proposal_invalid",
                    "Supply an existing product variant, tax category, effective date and documented basis.");
            await using var connection=await db.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT pos_cashier.propose_admin_variant_vat(
                  @digest,@owner,@id,@product,@variant,@code,@effective,
                  @reference,@reason)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",body.RequestId);
            command.Parameters.AddWithValue("product",body.ProductId!);
            command.Parameters.AddWithValue("variant",body.VariantId!);
            command.Parameters.AddWithValue("code",body.TreatmentCode!);
            command.Parameters.Add("effective",NpgsqlDbType.Date).Value=body.EffectiveFrom.Value;
            command.Parameters.AddWithValue("reference",body.AuthorityReference!);
            command.Parameters.AddWithValue("reason",body.Justification!);
            return await ReadJson(command,ct);
        }).WithName("ProposeAdminPosVariantVat")
          .Accepts<VatProposalRequest>("application/json").ExcludeFromDescription();

        group.MapGet("/reviews", async (string? organization_id,
            WebAdminService admin,WebAdminConnections db,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Verify(admin,context,organization_id,ct);
            await using var connection=await db.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT pos_cashier.read_admin_vat_reviews(@digest,@owner)::text",
                connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            return await ReadJson(command,ct);
        }).WithName("ReadAdminPosVatReviews").ExcludeFromDescription();

        group.MapPost("/proposals/{proposalId:guid}/review",async (
            Guid proposalId,string? organization_id,VatReviewRequest body,
            WebAdminService admin,WebAdminConnections db,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Verify(admin,context,organization_id,ct,review:true);
            if(proposalId==Guid.Empty||body.Approve is null||!Note(body.ReviewNote))
                throw new WebAdminException(422,"pos_vat_review_invalid",
                    "Provide a valid review decision and a documented explanation.");
            await using var connection=await db.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT pos_cashier.review_admin_variant_vat(
                  @digest,@owner,@proposal,@approve,@note)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("proposal",proposalId);
            command.Parameters.AddWithValue("approve",body.Approve.Value);
            command.Parameters.AddWithValue("note",body.ReviewNote!);
            return await ReadJson(command,ct);
        }).WithName("ReviewAdminPosVatProposal")
          .Accepts<VatReviewRequest>("application/json").ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<string> Verify(WebAdminService admin,
        HttpContext context,string? owner,CancellationToken ct,bool review=false)
    {
        if(owner is null || !PublicContentService.IsOrganization(owner))
            throw new WebAdminException(400,"invalid_organization","Select an organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await admin.CurrentAsync(cookie,ct);
        if(session is null)throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(review && session.User.Role!="super_admin")
            throw new WebAdminException(403,"pos_vat_review_denied","Only a distinct Super Admin may review VAT proposals.");
        if(session.User.Role is not ("super_admin" or "accountant") ||
           !AdminPermissions.Has(session.User.Role,"finance:write") ||
           !session.User.Organizations.Any(o=>o.Id==owner))
            throw new WebAdminException(403,"pos_vat_denied","VAT proposals are restricted to authorized finance staff.");
        return cookie!;
    }

    private static async Task<IResult> ReadJson(NpgsqlCommand command,CancellationToken ct)
    {
        try
        {
            var raw=await command.ExecuteScalarAsync(ct) as string;
            if(raw is null)throw new InvalidOperationException("VAT proposal response missing.");
            using var doc=JsonDocument.Parse(raw);
            return Results.Json(doc.RootElement.Clone());
        }
        catch(PostgresException ex) when(ex.SqlState=="42501")
        {throw new WebAdminException(403,"pos_vat_denied","Finance VAT access denied.");}
        catch(PostgresException ex) when(ex.SqlState is "22023" or "23503")
        {throw new WebAdminException(422,"pos_vat_invalid","Invalid VAT classification or product variant.");}
        catch(PostgresException ex) when(ex.SqlState=="P0001" &&
            ex.MessageText is "pos_vat_proposal_replay_conflict" or "pos_vat_review_conflict")
        {throw new WebAdminException(409,"pos_vat_request_conflict","This request was already recorded with different data.");}
        catch(PostgresException ex) when(ex.SqlState=="P0001")
        {throw new WebAdminException(403,"pos_vat_session_expired","The authorized session was revoked.");}
        catch(PostgresException ex) when(ex.SqlState=="42883")
        {throw new WebAdminException(503,"pos_vat_not_ready","VAT proposal access has not been configured.");}
    }
}
