using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Api.Identity;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.Payabl;

/// <summary>
/// Approved sandbox prices are server configuration, never browser amounts.
/// No live recurring mandate, vault token, or automatic fan entitlement.
/// </summary>
public sealed class HubFanPayablOptions
{
    public bool Enabled { get; set; }
    public long ParentMonthlyCents { get; set; }
    public long PlayerMonthlyCents { get; set; }
    public long BothMonthlyCents { get; set; }
    public static bool Safe(HubFanPayablOptions settings)=>
        !settings.Enabled || (settings.ParentMonthlyCents is >=1 and <=1000000 &&
            settings.PlayerMonthlyCents is >=1 and <=1000000 &&
            settings.BothMonthlyCents is >=1 and <=1000000);
    public long Price(string audience)=>audience switch
    {
        "fan_parent"=>ParentMonthlyCents,
        "fan_player"=>PlayerMonthlyCents,
        "fan_both"=>BothMonthlyCents,
        _=>0
    };
}

public sealed record HubFanPayablCheckoutRequest(
    Guid ClientRequestId,string Audience,string Email,
    string FirstName,string LastName,bool AcceptedTerms);
public sealed record HubFanPayablCheckoutState(
    Guid Id,bool Created,string OrganizationId,string Audience,
    long AmountCents,string Currency,string Status,string? CheckoutUrl);
public sealed record HubFanPayablCheckoutResponse(
    Guid Id,string OrganizationId,string Audience,
    long AmountCents,string Currency,string Status,string? CheckoutUrl,bool Replay);

/// <summary>
/// Mobile fan Payabl initial card checkout only. The return URL and the
/// signed webhook NEVER activate an entitlement without provider settlement
/// reconciliation by a separately reviewed finance service.
/// </summary>
public static class HubFanPayablCheckoutEndpoints
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };

    public static IEndpointRouteBuilder MapHubFanPayablCheckout(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/mobile/hub/payabl/checkout",
            (string? organization_id,HubFanPayablCheckoutRequest body,
             HttpContext context,IHostEnvironment environment,
             IOptions<HubFanPayablOptions> fanOptions,IOptions<PayablOptions> payabl,
             IWebAdminSecrets secrets,PayablGateway gateway,
             LefteriaDbContext database,CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="private, no-store";
                return MobileIdentityEndpoints.Execute(
                    ()=>CreateAsync(organization_id,body,context,environment,
                        fanOptions.Value,payabl.Value,secrets,gateway,database,token),
                    context);
            })
            .RequireAuthorization(IdentityServices.Policy)
            .WithTags("Mobile HUB Payabl sandbox")
            .WithName("CreateMobileFanHubPayablSandboxCheckout")
            .WithDescription("Begin a sandbox card checkout for a verified fan. A payment return and gateway callback cannot grant subscription access.")
            .Produces<HubFanPayablCheckoutResponse>()
            .WithMobileErrors();

        endpoints.MapPost("/api/payabl/hub/webhooks/{id:guid}/{nonce}",
            async (Guid id,string nonce,HttpContext context,
                IHostEnvironment environment,IOptions<HubFanPayablOptions> fanOptions,
                IOptions<PayablOptions> payabl,LefteriaDbContext database,
                CancellationToken token)=>
            {
                context.Response.Headers.CacheControl="no-store";
                if(!environment.IsStaging() || !fanOptions.Value.Enabled ||
                   !payabl.Value.Enabled || !PayablOptions.Safe(payabl.Value) ||
                   !HubFanPayablOptions.Safe(fanOptions.Value))
                    return Results.NotFound();
                if(nonce.Length!=43 || nonce.Any(c=>
                    !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
                   context.Request.ContentLength is >4096 ||
                   context.Request.ContentType is null ||
                   !context.Request.ContentType.StartsWith(
                       "application/x-www-form-urlencoded",
                       StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest();

                await using var buffer=new MemoryStream();
                var chunk=new byte[1024];
                int read;
                while((read=await context.Request.Body.ReadAsync(chunk,token))!=0)
                {
                    if(buffer.Length+read>4096)return Results.BadRequest();
                    await buffer.WriteAsync(chunk.AsMemory(0,read),token);
                }
                Dictionary<string,string> fields;
                try { fields=PayablGateway.ParseFields(
                    Encoding.ASCII.GetString(buffer.ToArray())); }
                catch(PayablUnavailableException) {return Results.BadRequest();}
                if(!PayablNotification.TryRead(fields,id,payabl.Value.Secret,
                    DateTimeOffset.UtcNow,out var notice) || notice is null)
                    return Results.BadRequest();

                await database.Database.OpenConnectionAsync(token);
                try
                {
                    await using var command=new NpgsqlCommand("""
                        SELECT hub.record_fan_payabl_checkout_notice(
                          @id,@nonce,@transaction,@kind,@code,@stamp,
                          @order,@amount,@currency,@state)
                        """,(NpgsqlConnection)database.Database.GetDbConnection());
                    command.Parameters.AddWithValue("id",id);
                    command.Parameters.AddWithValue("nonce",
                        PayablSignature.CallbackDigest(nonce));
                    command.Parameters.AddWithValue("transaction",notice.TransactionId);
                    command.Parameters.AddWithValue("kind",notice.Kind);
                    command.Parameters.AddWithValue("code",notice.ErrorCode);
                    command.Parameters.AddWithValue("stamp",notice.Timestamp);
                    command.Parameters.AddWithValue("order",notice.OrderId);
                    command.Parameters.Add("amount",NpgsqlTypes.NpgsqlDbType.Bigint).Value=
                        notice.AmountCents is long cents?cents:DBNull.Value;
                    command.Parameters.Add("currency",NpgsqlTypes.NpgsqlDbType.Text).Value=
                        notice.Currency is { } currency?currency:DBNull.Value;
                    command.Parameters.AddWithValue("state",notice.PaymentState);
                    if(await command.ExecuteScalarAsync(token) is not true)
                        return Results.StatusCode(503);
                    // This is a reconciliation record, NOT an active subscription.
                    return Results.Ok();
                }
                catch(PostgresException ex) when(ex.SqlState is "42501" or "22023")
                {return Results.BadRequest();}
                finally {await database.Database.CloseConnectionAsync();}
            })
            .WithTags("Payabl signed sandbox notifications")
            .WithName("RecordHubFanPayablSandboxNotification")
            .Accepts<string>("application/x-www-form-urlencoded")
            .Produces(200).Produces(400).Produces(404).Produces(503);

        return endpoints;
    }

    private static async Task<HubFanPayablCheckoutResponse> CreateAsync(
        string? owner,HubFanPayablCheckoutRequest body,HttpContext context,
        IHostEnvironment environment,HubFanPayablOptions fanOptions,
        PayablOptions payabl,IWebAdminSecrets secrets,PayablGateway gateway,
        LefteriaDbContext database,CancellationToken token)
    {
        var actor=MobilePrincipal.Read(context.User);
        if(actor.Role!=MobileRole.Fan)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Verified fan role required.");
        if(context.Request.Query["organization_id"].Count!=1 ||
            owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
            body is null || body.ClientRequestId==Guid.Empty ||
            body.Audience is not ("fan_parent" or "fan_player" or "fan_both") ||
            !body.AcceptedTerms || !ValidPayer(body))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid sandbox fan checkout request.");
        if(!environment.IsStaging() || !fanOptions.Enabled ||
            !HubFanPayablOptions.Safe(fanOptions) ||
            !payabl.Enabled || !PayablOptions.Safe(payabl) ||
            !gateway.Available)
            throw Unavailable();

        var amount=fanOptions.Price(body.Audience);
        var id=Guid.NewGuid();
        var nonce=secrets.NewToken();
        var nonceDigest=PayablSignature.CallbackDigest(nonce);
        var current=await ExecuteAsync<HubFanPayablCheckoutState>(database,"""
            SELECT hub.begin_mobile_fan_payabl_checkout(
              @identity,@phone,@role,@expires,@owner,@audience,@id,
              @client,@nonce,@amount)::text
            """,actor,owner,token,command=>
            {
                command.Parameters.AddWithValue("audience",body.Audience);
                command.Parameters.AddWithValue("id",id);
                command.Parameters.AddWithValue("client",body.ClientRequestId);
                command.Parameters.AddWithValue("nonce",nonceDigest);
                command.Parameters.AddWithValue("amount",amount);
            });
        if(current is null || current.Id==Guid.Empty ||
           current.OrganizationId!=owner || current.Audience!=body.Audience ||
           current.AmountCents!=amount || current.Currency!="EUR" ||
           current.Status is not ("initiating" or "ready" or "review_required" or "failed"))
            throw Unavailable();
        if(!current.Created)
            return new(current.Id,owner,current.Audience,amount,"EUR",
                current.Status,current.CheckoutUrl,true);

        var origin=payabl.PublicOrigin.TrimEnd('/');
        PayablCheckoutResult hosted;
        try
        {
            hosted=await gateway.InitializeAsync(new PayablCheckoutRequest(
                PayablNotification.OrderId(id),amount,"EUR",body.Email,
                body.FirstName,body.LastName,
                context.Connection.RemoteIpAddress?.MapToIPv4().ToString()
                    ?? "127.0.0.1",
                origin+"/api/payabl/hub/webhooks/"+id.ToString("D")+"/"+nonce,
                origin+"/payments/return"),token);
        }
        catch(Exception ex) when(ex is PayablUnavailableException or
            HttpRequestException or TaskCanceledException)
        {
            // Intentionally leave 'initiating'. A replay cannot create a
            // second checkout: the uncertain gateway request needs review.
            throw Unavailable();
        }
        var ready=await ExecuteAsync<bool>(database,"""
            SELECT hub.ready_mobile_fan_payabl_checkout(
              @identity,@phone,@role,@expires,@owner,@id,@url,@transaction)
            """,actor,owner,token,command=>
            {
                command.Parameters.AddWithValue("id",id);
                command.Parameters.AddWithValue("url",hosted.CheckoutUrl);
                command.Parameters.AddWithValue("transaction",hosted.TransactionId);
            });
        if(!ready)throw Unavailable();
        return new(id,owner,body.Audience,amount,"EUR","ready",hosted.CheckoutUrl,false);
    }

    private static async Task<T?> ExecuteAsync<T>(
        LefteriaDbContext database,string sql,MobileSession actor,
        string owner,CancellationToken token,Action<NpgsqlCommand> additional)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand(sql,
                    (NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","fan");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",owner);
                additional(command);
                var raw=await command.ExecuteScalarAsync(token);
                if(typeof(T)==typeof(bool))
                    return raw is bool value?(T)(object)value:default;
                return raw is string text?JsonSerializer.Deserialize<T>(text,Json):default;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            throw ex.SqlState switch
            {
                "42501"=>new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Fan identity unavailable."),
                "23505" or "40001"=>new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,
                    "An existing checkout needs reconciliation; do not start a duplicate."),
                "22023"=>new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Invalid checkout details."),
                _=>Unavailable()
            };
        }
    }

    private static bool ValidPayer(HubFanPayablCheckoutRequest body)
    {
        static bool Safe(string? value,int min,int max)=>
            value is not null && value.Length>=min && value.Length<=max &&
            value.All(c=>c is >= ' ' and <= '~') &&
            value.Trim()==value;
        return Safe(body.Email,3,50)&&body.Email.Contains('@') &&
            Safe(body.FirstName,1,50)&&Safe(body.LastName,1,50);
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Sandbox fan checkout unavailable or needs manual reconciliation.");
}
