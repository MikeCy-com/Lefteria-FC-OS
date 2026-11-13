using System.Security.Cryptography;
using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Api.Payabl;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileFanHubStartBody(
    string? OrganizationId,string? Track,
    string? Email,string? FirstName,string? LastName);
public sealed record MobileFanHubStatus(
    string OrganizationId,string Status,bool AudienceParent,
    bool AudiencePlayer,DateTimeOffset? ExpiresAt,string? CheckoutUrl);
public sealed record MobileFanHubCheckout(
    Guid Id,string Status,bool Created,Guid? PaymentId,long? AmountCents,
    string? CheckoutUrl);

/// <summary>
/// Sandbox-only fan subscription checkout. Never infer settlement from browser
/// redirects; only existing signed Payabl callback and finance ledger can
/// activate subscription. Client cannot choose price, expiry or provider.
/// </summary>
public sealed class MobileFanPayablStore(
    LefteriaFc.Infrastructure.Persistence.LefteriaDbContext database,
    PayablGateway gateway,IOptions<PayablOptions> options)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower
    };
    public async Task<MobileFanHubStatus> StatusAsync(
        MobileSession actor,string organization,CancellationToken ct)
    {
        Check(actor,organization);
        try
        {
            var result=await ExecuteAsync<MobileFanHubStatus>("status",actor,
                organization,null,null,null,null,null,null,null,ct);
            if(result is null || result.OrganizationId!=organization ||
               result.Status is not ("none" or "pending" or "active" or "past_due") ||
               (result.Status=="active" &&
                 (result.ExpiresAt is null || result.ExpiresAt<=DateTimeOffset.UtcNow)) ||
               (result.CheckoutUrl is not null && !ValidHostedUrl(result.CheckoutUrl)))
                throw Unavailable();
            return result;
        }
        catch(PostgresException ex) when(ex.SqlState=="42501")
        {
            throw Forbidden();
        }
        catch(PostgresException){throw Unavailable();}
    }

    public async Task<MobileFanHubCheckout> StartAsync(
        MobileSession actor,MobileFanHubStartBody body,string sourceIp,CancellationToken ct)
    {
        if(body is null || body.OrganizationId is not
             ("lefteria_fc" or "lefteria_fc_academy") ||
           body.Track is not ("parent" or "player" or "both") ||
           !SafePayer(body.Email,3,50) || !body.Email!.Contains('@') ||
           !SafePayer(body.FirstName,1,50) || !SafePayer(body.LastName,1,50))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid fan subscription checkout.");
        Check(actor,body.OrganizationId);
        if(!gateway.Available || !PayablOptions.Safe(options.Value))
            throw Unavailable();
        var paymentId=Guid.NewGuid();
        var nonce=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        MobileFanHubCheckout? reserved;
        try
        {
            reserved=await ExecuteAsync<MobileFanHubCheckout>("reserve",actor,
                body.OrganizationId,body.Track,paymentId,
                PayablSignature.CallbackDigest(nonce),
                body.Email,body.FirstName,body.LastName,null,ct);
        }
        catch(PostgresException ex) when(ex.SqlState=="42501"){throw Forbidden();}
        catch(PostgresException ex) when(ex.SqlState=="22023")
        {
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Fan checkout validation failed.");
        }
        catch(PostgresException){throw Unavailable();}
        if(reserved is null || reserved.Id==Guid.Empty ||
           reserved.Status is not ("pending" or "active" or "past_due") ||
           (reserved.Created && (reserved.PaymentId!=paymentId ||
             reserved.AmountCents is null or <100 or >1000000)))
            throw Unavailable();
        if(!reserved.Created)
        {
            var status=await StatusAsync(actor,body.OrganizationId,ct);
            return new MobileFanHubCheckout(reserved.Id,status.Status,false,
                null,null,status.CheckoutUrl);
        }

        // Never retry a gateway initialization after an uncertain response.
        // The reserved checkout is reconciled by operations; no extra charge.
        PayablCheckoutResult hosted;
        var origin=options.Value.PublicOrigin.TrimEnd('/');
        try
        {
            hosted=await gateway.InitializeAsync(new PayablCheckoutRequest(
                PayablNotification.OrderId(paymentId),
                reserved.AmountCents!.Value,"EUR",body.Email!,body.FirstName!,body.LastName!,
                sourceIp,origin+"/api/payabl/webhooks/"+paymentId.ToString("D")+"/"+nonce,
                origin+"/payments/return"),ct);
        }
        catch(Exception ex) when(ex is PayablUnavailableException or HttpRequestException
            or TaskCanceledException or ArgumentException)
        {
            throw Unavailable();
        }
        if(!ValidHostedUrl(hosted.CheckoutUrl))throw Unavailable();
        try
        {
            var confirmed=await ExecuteAsync<bool>("ready",actor,
                body.OrganizationId,null,paymentId,null,null,null,null,
                new ReadyCheckout(hosted.CheckoutUrl,hosted.TransactionId),ct);
            if(!confirmed)throw Unavailable();
            return reserved with { CheckoutUrl=hosted.CheckoutUrl };
        }
        catch(PostgresException){throw Unavailable();}
    }

    private sealed record ReadyCheckout(string Url,string Transaction);
    private async Task<T?> ExecuteAsync<T>(string operation,MobileSession actor,
        string owner,string? track,Guid? payment,string? digest,
        string? email,string? first,string? last,ReadyCheckout? ready,CancellationToken ct)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            var statement=operation switch {
                "status"=>"SELECT hub.read_mobile_fan_subscription(@identity,@phone,@role,@expires,@owner)::text",
                "reserve"=>"SELECT hub.reserve_mobile_fan_payabl(@identity,@phone,@role,@expires,@owner,@track,@payment,@digest,@email,@first,@last)::text",
                "ready"=>"SELECT hub.ready_mobile_fan_payabl(@identity,@phone,@role,@expires,@owner,@payment,@url,@transaction)::text",
                _=>throw new InvalidOperationException("Unknown fan billing action.")
            };
            await using var cmd=new NpgsqlCommand(statement,
                (NpgsqlConnection)database.Database.GetDbConnection());
            cmd.Parameters.AddWithValue("identity",actor.IdentityId);
            cmd.Parameters.AddWithValue("phone",actor.NormalizedPhone);
            cmd.Parameters.AddWithValue("role","fan");
            cmd.Parameters.AddWithValue("expires",actor.ExpiresAt);
            cmd.Parameters.AddWithValue("owner",owner);
            if(payment is Guid paymentId)cmd.Parameters.AddWithValue("payment",paymentId);
            if(track is not null)cmd.Parameters.AddWithValue("track",track);
            if(digest is not null)cmd.Parameters.AddWithValue("digest",digest);
            if(email is not null)cmd.Parameters.AddWithValue("email",email);
            if(first is not null)cmd.Parameters.AddWithValue("first",first);
            if(last is not null)cmd.Parameters.AddWithValue("last",last);
            if(ready is not null)
            {
                cmd.Parameters.AddWithValue("url",ready.Url);
                cmd.Parameters.AddWithValue("transaction",ready.Transaction);
            }
            var value=await cmd.ExecuteScalarAsync(ct);
            if(value is null || value is DBNull)throw Unavailable();
            if(operation=="ready")
                return (T)(object)(value is string text && bool.Parse(text));
            if(value is not string raw)throw Unavailable();
            return JsonSerializer.Deserialize<T>(raw,Json);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }

    private static bool ValidHostedUrl(string url)=>
        Uri.TryCreate(url,UriKind.Absolute,out var uri) &&
        uri.Scheme=="https" && uri.UserInfo.Length==0 &&
        uri.Host is ("sandbox.payabl.com" or "pay4.sandbox.payabl.com") &&
        uri.Fragment.Length==0 && uri.AbsoluteUri.Length<=2048;

    private static bool SafePayer(string? text,int min,int max)=>
        text is not null && text.Length>=min && text.Length<=max &&
        text.All(c=>c>=' '&&c<='~');

    private static void Check(MobileSession actor,string owner)
    {
        if(actor.Role!=MobileRole.Fan ||
           owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw Forbidden();
    }
    private static MobileAuthException Forbidden()=>new(
        MobileAuthFaultKind.Forbidden,MobileAuthErrorCode.RoleUnavailable,
        "Verified fan account required.");
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Payabl sandbox subscription is unavailable.");
}
