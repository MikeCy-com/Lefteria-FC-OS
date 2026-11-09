using System.Text.Json;
using LefteriaFc.Api.Identity;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Payabl;

public sealed record RecordAcademyCashPaymentCommand(Guid ClientRequestId, string? Notes);

/// <summary>
/// A verified backend-generated PDF for the original branded Academy invoice
/// design. Neither browser nor parent can choose amounts, payment method,
/// invoice status, guardian identity, child name, or print a paid receipt.
/// </summary>
public static class AcademyInvoiceEndpoints
{
    private static string[] ParseIds(string? value)
    {
        if(string.IsNullOrWhiteSpace(value) || value.Length>800)
            throw new InvalidOperationException("Invalid invoice ID selection.");
        var list=value.Split(',',StringSplitOptions.None);
        if(list.Length is < 1 or > 20 || list.Any(x=>!Guid.TryParse(x,out var id)||id==Guid.Empty) ||
           list.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Length)
            throw new InvalidOperationException("Invalid invoice selection.");
        return list;
    }

    public static IEndpointRouteBuilder MapAcademyInvoices(this IEndpointRouteBuilder endpoints)
    {
        var admin=endpoints.MapGroup("/api/admin/finance/payabl")
            .WithTags("Academy invoice PDFs").RequireAuthorization(WebAdminServices.Policy);
        admin.MapPost("/training-fees/{feeId:guid}/cash", async (
            Guid feeId, string? organization_id, RecordAcademyCashPaymentCommand body,
            WebAdminService service, WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await AdminCookie(organization_id,service,context,true,ct);
            if(body.ClientRequestId==Guid.Empty || body.Notes is null ||
                body.Notes.Length>280 || body.Notes.Any(char.IsControl))
                return Results.BadRequest();
            await using var db=await connections.OpenAsync(ct);
            await using var query=new NpgsqlCommand("""
              SELECT finance.record_admin_training_fee_cash(
                @digest,@owner,@fee,@receipt,@notes)::text
              """,db);
            query.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            query.Parameters.AddWithValue("owner",organization_id!);
            query.Parameters.AddWithValue("fee",feeId);
            query.Parameters.AddWithValue("receipt",body.ClientRequestId);
            query.Parameters.AddWithValue("notes",body.Notes);
            try
            {
                var json=await query.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503) :
                    Results.Content(json,"application/json",System.Text.Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState is "42501" or "23505" or "22023")
            {
                return e.SqlState=="23505" ? Results.Conflict(new {
                    detail="Cash cannot be recorded after a card checkout was created or a receipt already exists."
                }) : Results.BadRequest();
            }
        }).WithName("RecordAdminAcademyTrainingFeeCash")
        .Accepts<RecordAcademyCashPaymentCommand>("application/json");

        admin.MapGet("/invoices/{invoiceId:guid}.pdf", async(
            Guid invoiceId,string? organization_id,WebAdminService service,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            PublicContentService publicContent,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await AdminCookie(organization_id,service,context,false,ct);
            return await AdminPdf([invoiceId],organization_id!,cookie,connections,secrets,publicContent,context,ct);
        }).WithName("DownloadAdminAcademyInvoicePdf");

        admin.MapGet("/invoices/multi.pdf",async(
            string? ids,string? organization_id,WebAdminService service,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            PublicContentService publicContent,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await AdminCookie(organization_id,service,context,false,ct);
            Guid[] selected;
            try{selected=ParseIds(ids).Select(Guid.Parse).ToArray();}
            catch(InvalidOperationException){return Results.BadRequest();}
            return await AdminPdf(selected,organization_id!,cookie,connections,secrets,publicContent,context,ct);
        }).WithName("DownloadAdminAcademyMultiInvoicePdf");

        var parent=endpoints.MapGroup("/api/mobile/finance/invoices")
            .WithTags("Parent invoice PDFs").RequireAuthorization(IdentityServices.Policy);

        parent.MapGet("/{invoiceId:guid}.pdf",async(Guid invoiceId,HttpContext context,
            LefteriaDbContext db,PublicContentService publicContent,CancellationToken ct) =>
            await ParentPdf([invoiceId],context,db,publicContent,ct))
            .WithName("DownloadParentAcademyInvoicePdf");

        parent.MapGet("/multi.pdf",async(string? ids,HttpContext context,
            LefteriaDbContext db,PublicContentService publicContent,CancellationToken ct) =>
        {
            Guid[] selected;
            try{selected=ParseIds(ids).Select(Guid.Parse).ToArray();}
            catch(InvalidOperationException){return Results.BadRequest();}
            return await ParentPdf(selected,context,db,publicContent,ct);
        }).WithName("DownloadParentAcademyMultiInvoicePdf");
        return endpoints;
    }

    private static async Task<IResult> AdminPdf(Guid[] ids,string organization,
        string cookie,WebAdminConnections connections,IWebAdminSecrets secrets,
        PublicContentService publicContent,HttpContext context,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        await using var db=await connections.OpenAsync(ct);
        await using var query=new NpgsqlCommand(
            "SELECT finance.read_admin_invoice_pdf(@digest,@owner,@ids)::text",db);
        query.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
        query.Parameters.AddWithValue("owner",organization);
        query.Parameters.AddWithValue("ids",ids);
        try
        {
            var raw=await query.ExecuteScalarAsync(ct) as string;
            if(raw is null)return Results.Problem(statusCode:503);
            return await Document(raw,publicContent,ct);
        }
        catch(PostgresException error) when(error.SqlState is "42501" or "22023")
        {return Results.NotFound();}
        catch(JsonException){return Results.Problem(statusCode:503);}
    }

    private static async Task<IResult> ParentPdf(Guid[] ids,HttpContext context,
        LefteriaDbContext database,PublicContentService publicContent,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        var session=MobilePrincipal.Read(context.User);
        if(session.Role!=MobileRole.Parent)return Results.Forbid();
        var connection=database.Database.GetDbConnection();
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var query=new NpgsqlCommand("""
                SELECT finance.read_parent_invoice_pdf(
                   @identity,@phone,@role,@expires,@ids)::text
                """,(NpgsqlConnection)connection);
            query.Parameters.AddWithValue("identity",session.IdentityId);
            query.Parameters.AddWithValue("phone",session.NormalizedPhone);
            query.Parameters.AddWithValue("role","parent");
            query.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
            query.Parameters.AddWithValue("ids",ids);
            var raw=await query.ExecuteScalarAsync(ct) as string;
            if(raw is null)return Results.Problem(statusCode:503);
            return await Document(raw,publicContent,ct);
        }
        catch(PostgresException error) when(error.SqlState is "42501" or "22023")
        {return Results.NotFound();}
        catch(JsonException){return Results.Problem(statusCode:503);}
        finally{await database.Database.CloseConnectionAsync();}
    }

    private static async Task<IResult> Document(string raw,PublicContentService publicContent,
        CancellationToken ct)
    {
        AcademyInvoiceBatch document;
        try{document=AcademyInvoicePdf.Parse(raw);}
        catch(Exception e) when(e is JsonException or InvalidOperationException or NullReferenceException)
        {return Results.Problem(statusCode:503);}
        PublicClub? club=null;
        try
        {
            var site=await publicContent.GetSiteAsync("lefteria_fc_academy",ct);
            club=site?.Club ?? (await publicContent.GetSiteAsync("lefteria_fc",ct))?.Club;
        }
        catch(Exception e) when(!ct.IsCancellationRequested &&
            (e is System.Data.Common.DbException or TimeoutException))
        {
            // A temporarily missing public club profile is not permission
            // to display a forged receipt; retain source design defaults.
        }
        // Use packaged source crest when available; never fetch URL supplied by
        // a customer or from another organization (no PDF renderer SSRF).
        var logo=Path.Combine(AppContext.BaseDirectory,"club-logo.png");
        byte[] pdf;
        try{pdf=AcademyInvoicePdf.Render(document,logo,club);}
        catch(Exception){return Results.Problem(statusCode:503);}
        var fileName=document.Items.Count==1
            ? "invoice-"+document.Items[0].Id.ToString("N")[..8]+".pdf"
            : "academy-invoices-"+document.Items.Count+".pdf";
        return Results.File(pdf,"application/pdf",fileName,
            enableRangeProcessing:false);
    }

    private static async Task<string> AdminCookie(string? owner,
        WebAdminService service,HttpContext context,bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        if(owner!="lefteria_fc_academy" || context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization","Choose exactly one Academy.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await service.CurrentAsync(cookie,ct);
        if(session is null)throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(!AdminPermissions.Has(session.User.Role,write?"finance:write":"finance:read") ||
           !session.User.Organizations.Any(org=>org.Id==owner))
            throw new WebAdminException(403,"not_authorized","Finance access denied.");
        return cookie!;
    }
}
