using System.Globalization;
using System.Text;
using System.Text.Json;
using LefteriaFc.Api.Identity;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.Payabl;

public sealed record AcademyTrainingFeeIssueCommand(
    Guid ClientRequestId, string? PlayerId, string? FeeMonth,
    long AmountCents, string? PayerEmail, string? PayerFirstName, string? PayerLastName);

public sealed record ParentTrainingFeePreferenceCommand(
    int PreferredDay, bool SaveCardRequested, bool AutomaticCollectionRequested);

public sealed record TrainingFeeSmsCommand(Guid ClientRequestId);

/// <summary>
/// Training-fee issuance is a deliberate manager action for ONE child and ONE
/// selected YYYY-MM. There is no annual schedule, yearly batch generation,
/// or implicit twelve-month renewal.
/// </summary>
public static class AcademyFeeEndpoints
{
    public static IEndpointRouteBuilder MapAcademyFees(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/finance/payabl/training-fees")
            .WithTags("Academy training fees (sandbox)")
            .RequireAuthorization(WebAdminServices.Policy);

        admin.MapGet("/", async (string? organization_id, WebAdminService service,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminCookie(organization_id,service,context,false,ct);
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT finance.read_admin_training_fees(@digest,@owner)::text",db);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner",organization_id!);
            var json = await cmd.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode:503)
                : Results.Content(json,"application/json",Encoding.UTF8,200);
        }).WithName("ReadAdminAcademyTrainingFees");

        admin.MapPost("/", async (string? organization_id,
            AcademyTrainingFeeIssueCommand body, WebAdminService service,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminCookie(organization_id,service,context,true,ct);
            if (body.ClientRequestId==Guid.Empty ||
                body.PlayerId is null || body.PlayerId.Length is < 1 or > 128 ||
                body.FeeMonth is null || body.FeeMonth.Length!=7 ||
                !DateOnly.TryParseExact(body.FeeMonth+"-01","yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var month) ||
                month.Year is < 2020 or > 2100 ||
                body.AmountCents is < 1 or > 100000000 ||
                !Ascii(body.PayerEmail,3,50) || !body.PayerEmail!.Contains('@') ||
                !Ascii(body.PayerFirstName,1,50) ||
                !Ascii(body.PayerLastName,1,50))
                throw new WebAdminException(422,"invalid_training_fee",
                    "Issue one explicit YYYY-MM training fee for a verified academy child.");
            await using var db = await connections.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT finance.issue_admin_training_fee(
                  @digest,@owner,@id,@player,@month,@cents,@email,@first,@last)::text
                """,db);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("owner",organization_id!);
            cmd.Parameters.AddWithValue("id",body.ClientRequestId);
            cmd.Parameters.AddWithValue("player",body.PlayerId);
            cmd.Parameters.AddWithValue("month",month);
            cmd.Parameters.AddWithValue("cents",body.AmountCents);
            cmd.Parameters.AddWithValue("email",body.PayerEmail!);
            cmd.Parameters.AddWithValue("first",body.PayerFirstName!);
            cmd.Parameters.AddWithValue("last",body.PayerLastName!);
            try
            {
                var json = await cmd.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when (e.SqlState is "23505" or "42501" or "22023")
            {
                throw new WebAdminException(e.SqlState=="23505" ? 409 : e.SqlState=="42501" ? 403 : 422,
                    "training_fee_conflict",
                    "The month is already issued differently, or the verified child/guardian link is unavailable.");
            }
        }).WithName("IssueAdminAcademyTrainingFee")
        .Accepts<AcademyTrainingFeeIssueCommand>("application/json");

        admin.MapPost("/{feeId:guid}/sms", async (
            Guid feeId, string? organization_id, TrainingFeeSmsCommand body,
            WebAdminService service, WebAdminConnections connections,
            IWebAdminSecrets secrets, PayablGateway gateway, TwilioPaymentSms sms,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await AdminCookie(organization_id,service,context,true,ct);
            if (body.ClientRequestId==Guid.Empty)
                throw new WebAdminException(422,"invalid_sms_request","A unique SMS attempt ID is required.");
            if (!gateway.Available || !sms.Available)
                throw new WebAdminException(503,"training_fee_sms_disabled",
                    "Payabl sandbox and approved Twilio SMS sending must both be configured.");
            var digest=secrets.TokenDigest(cookie);
            string? phone;
            string? link;
            long cents;
            string month;
            Guid reservation;
            bool created;
            await using (var db=await connections.OpenAsync(ct))
            await using (var query=new NpgsqlCommand("""
                SELECT finance.reserve_admin_training_fee_sms(
                  @digest,@owner,@fee,@request)::text
                """,db))
            {
                query.Parameters.AddWithValue("digest",digest);
                query.Parameters.AddWithValue("owner",organization_id!);
                query.Parameters.AddWithValue("fee",feeId);
                query.Parameters.AddWithValue("request",body.ClientRequestId);
                string? raw;
                try { raw=await query.ExecuteScalarAsync(ct) as string; }
                catch(PostgresException e) when(e.SqlState is "42501" or "22023")
                { throw new WebAdminException(403,"sms_not_permitted","Only a ready fee for the verified guardian can be sent."); }
                if(raw is null)throw new WebAdminException(503,"sms_ledger_unavailable","SMS ledger unavailable.");
                using var item=JsonDocument.Parse(raw);
                reservation=item.RootElement.GetProperty("id").GetGuid();
                created=item.RootElement.GetProperty("new").GetBoolean();
                if(!created) return Results.Ok(new { sms_attempt_id=reservation,
                    state=item.RootElement.GetProperty("state").GetString(), replay=true });
                phone=item.RootElement.GetProperty("phone").GetString();
                link=item.RootElement.GetProperty("checkout_url").GetString();
                cents=item.RootElement.GetProperty("amount_cents").GetInt64();
                month=item.RootElement.GetProperty("fee_month").GetString() ?? "";
            }
            // A unique reservation is consumed BEFORE network I/O. An
            // uncertain Twilio response is never automatically resent.
            var accepted=false;
            string? sid=null;
            try
            {
                var result=await sms.SendAsync(phone!,link!,month,cents,ct);
                sid=result;
                accepted=true;
            }
            catch(Exception error) when(error is TwilioPaymentSmsUnavailableException or
                HttpRequestException or TaskCanceledException or JsonException)
            {
                // Do not return a false claim of SMS delivery or retry blindly.
            }
            await using(var db=await connections.OpenAsync(ct))
            await using(var mark=new NpgsqlCommand("""
                SELECT finance.finish_admin_training_fee_sms(
                  @digest,@owner,@id,@accepted,@sid)
                """,db))
            {
                mark.Parameters.AddWithValue("digest",digest);
                mark.Parameters.AddWithValue("owner",organization_id!);
                mark.Parameters.AddWithValue("id",reservation);
                mark.Parameters.AddWithValue("accepted",accepted);
                mark.Parameters.AddWithValue("sid",sid is null ? DBNull.Value : sid);
                if(await mark.ExecuteScalarAsync(ct) is not true)
                    throw new WebAdminException(503,"sms_state_uncertain","SMS attempt requires reconciliation.");
            }
            return Results.Ok(new { sms_attempt_id=reservation,
                state=accepted ? "accepted" : "unknown", replay=false,
                delivery_confirmed=false });
        }).WithName("SendAdminAcademyTrainingFeeSms")
        .Accepts<TrainingFeeSmsCommand>("application/json");

        var parent = endpoints.MapGroup("/api/mobile/finance")
            .WithTags("Parent training fees").RequireAuthorization(IdentityServices.Policy);
        parent.MapGet("/training-fees", async (HttpContext context,
            LefteriaDbContext database, CancellationToken ct) =>
        {
            var session=MobilePrincipal.Read(context.User);
            if(session.Role!=MobileRole.Parent)return Results.Forbid();
            var db=database.Database.GetDbConnection();
            await database.Database.OpenConnectionAsync(ct);
            try
            {
                await using var cmd=new NpgsqlCommand("""
                    SELECT finance.read_parent_training_fees(
                      @identity,@phone,@role,@expires)::text
                    """,(NpgsqlConnection)db);
                cmd.Parameters.AddWithValue("identity",session.IdentityId);
                cmd.Parameters.AddWithValue("phone",session.NormalizedPhone);
                cmd.Parameters.AddWithValue("role","parent");
                cmd.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
                var json=await cmd.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState=="42501"){return Results.Forbid();}
            finally {await database.Database.CloseConnectionAsync();}
        }).WithName("ReadParentTrainingFees");

        parent.MapPut("/preferences", async (
            ParentTrainingFeePreferenceCommand body,
            HttpContext context,LefteriaDbContext database,CancellationToken ct) =>
        {
            var session=MobilePrincipal.Read(context.User);
            if(session.Role!=MobileRole.Parent)return Results.Forbid();
            if(body.PreferredDay is < 1 or > 31 ||
                (body.AutomaticCollectionRequested && !body.SaveCardRequested))
                return Results.BadRequest();
            var db=database.Database.GetDbConnection();
            await database.Database.OpenConnectionAsync(ct);
            try
            {
                await using var cmd=new NpgsqlCommand("""
                    SELECT finance.set_parent_fee_preferences(
                      @identity,@phone,@role,@expires,@day,@save,@auto)::text
                    """,(NpgsqlConnection)db);
                cmd.Parameters.AddWithValue("identity",session.IdentityId);
                cmd.Parameters.AddWithValue("phone",session.NormalizedPhone);
                cmd.Parameters.AddWithValue("role","parent");
                cmd.Parameters.AddWithValue("expires",session.ExpiresAt.ToUniversalTime());
                cmd.Parameters.AddWithValue("day",body.PreferredDay);
                cmd.Parameters.AddWithValue("save",body.SaveCardRequested);
                cmd.Parameters.AddWithValue("auto",body.AutomaticCollectionRequested);
                var json=await cmd.ExecuteScalarAsync(ct) as string;
                return json is null ? Results.Problem(statusCode:503)
                    : Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException e) when(e.SqlState=="42501"){return Results.Forbid();}
            finally {await database.Database.CloseConnectionAsync();}
        }).WithName("SaveParentTrainingFeePreferences")
        .Accepts<ParentTrainingFeePreferenceCommand>("application/json");
        return endpoints;
    }

    private static bool Ascii(string? value,int min,int max) =>
        value is not null && value.Length>=min && value.Length<=max &&
        value.All(c=>c is >= ' ' and <= '~');

    private static async Task<string> AdminCookie(string? owner,
        WebAdminService service,HttpContext context,bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="no-store";
        if(owner!="lefteria_fc_academy" || context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization","Choose Lefteria FC Academy.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await service.CurrentAsync(cookie,ct);
        if(session is null)throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(!AdminPermissions.Has(session.User.Role,write ? "finance:write" : "finance:read") ||
           !session.User.Organizations.Any(org=>org.Id==owner))
            throw new WebAdminException(403,"not_authorized","Finance permission denied.");
        return cookie!;
    }
}
