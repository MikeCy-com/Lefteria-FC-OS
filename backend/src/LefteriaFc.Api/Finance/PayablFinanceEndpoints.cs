using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.Finance;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;

namespace LefteriaFc.Api.Finance;

public static class PayablFinanceEndpoints
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth=5
    };

    public static IEndpointRouteBuilder MapPayablFinance(this IEndpointRouteBuilder app)
    {
        var admin=app.MapGroup("/api/admin/finance/payabl")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Finance administration");

        var list=admin.MapGet("/payment-links",async (
            string? organization_id,WebAdminService service,
            IPayablFinanceStore store,HttpContext context,CancellationToken ct)=>
        {
            var (cookie,owner)=await Authorized(service,organization_id,context,ct);
            return Results.Ok(await store.ListAsync(cookie,owner,ct));
        }).WithName("ListAdminPayablPaymentLinks")
          .Produces<AdminPayablPaymentsDto>();
        Errors(list);

        var create=admin.MapPost("/payment-links",async (
            string? organization_id,WebAdminService service,
            IPayablFinanceStore store,HttpContext context,CancellationToken ct)=>
        {
            var (cookie,owner)=await Authorized(service,organization_id,context,ct);
            var body=await Body<AdminPayablPaymentCreateBody>(context,ct);
            return Results.Ok(await store.CreateAsync(cookie,owner,body,ct));
        }).WithName("CreateAdminPayablPaymentLink")
          .Accepts<AdminPayablPaymentCreateBody>("application/json")
          .Produces<AdminPayablPaymentDto>();
        Errors(create);

        var checkout=admin.MapPost("/payment-links/{id}/checkout",async(
            string id,string? organization_id,WebAdminService service,
            IPayablFinanceStore store,PayablHostedClient gateway,
            PayablGatewaySettings settings,HttpContext context,CancellationToken ct)=>
        {
            var (cookie,owner)=await Authorized(service,organization_id,context,ct);
            if(!settings.Enabled)
                throw new WebAdminException(503,"payabl_disabled",
                    "payabl. is not configured for this deployment.");
            var lease=Guid.NewGuid();
            var claimed=await store.ClaimCheckoutAsync(cookie,owner,id,lease,ct);
            PayablHostedCheckout hosted;
            try
            {
                hosted=await gateway.CreateAsync(
                    claimed.OrderId,claimed.AmountCents,
                    claimed.PayerFirstName,claimed.PayerLastName,
                    claimed.PayerEmail,ct);
            }
            catch(Exception error) when(error is not OperationCanceledException)
            {
                // The provider might have created an unused checkout session
                // before an upstream timeout. This never charges a card.
                // Do not expose raw provider response, PII, or secrets.
                await store.CompleteCheckoutAsync(cookie,owner,id,lease,
                    null,null,false,ct);
                throw new WebAdminException(503,"payabl_unavailable",
                    "Hosted checkout could not be verified.");
            }
            return Results.Ok(await store.CompleteCheckoutAsync(cookie,owner,
                id,lease,hosted.StartUrl,hosted.TransactionId,true,ct));
        }).WithName("ActivateAdminPayablPaymentLink")
          .Accepts<AdminPayablCheckoutEmptyBody>("application/json")
          .Produces<PayablCheckoutResultDto>();
        Errors(checkout);

        var publicLink=app.MapGet("/api/public/payabl/links/{token}",async (
            string token,IPayablFinanceStore store,
            PayablGatewaySettings settings,HttpContext context,
            CancellationToken ct)=>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.XContentTypeOptions="nosniff";
            if(!settings.Enabled)
                return Results.Ok(new PublicPayablLinkDto(false,"unavailable"));
            var link=await store.PublicLinkAsync(token,ct);
            // Defensive rejection of even a DB-stored non-provider URL.
            if(link.CheckoutUrl is not null&&
               !settings.IsTrustedCheckoutUrl(link.CheckoutUrl,out _))
                return Results.Ok(link with { CheckoutUrl=null, Status="unavailable" });
            return Results.Ok(link);
        }).WithName("GetPublicPayablPaymentLink")
          .WithTags("Public payment links")
          .Produces<PublicPayablLinkDto>();
        publicLink.Produces<WebAdminError>(422)
            .Produces<WebAdminError>(503);

        var notification=app.MapPost("/api/payabl/notifications",async (
            HttpContext context,IPayablFinanceStore store,
            PayablGatewaySettings settings,CancellationToken ct)=>
        {
            context.Response.Headers.CacheControl="no-store";
            if(!settings.Enabled)
                return Results.StatusCode(503);
            if(!context.Request.HasFormContentType ||
               context.Request.ContentLength is >8192)
                return Results.StatusCode(415);
            string body;
            try
            {
                await using var ms=new MemoryStream();
                var buffer=new byte[1024];
                int count;
                while((count=await context.Request.Body.ReadAsync(buffer,ct))!=0)
                {
                    if(ms.Length+count>8192)return Results.StatusCode(413);
                    await ms.WriteAsync(buffer.AsMemory(0,count),ct);
                }
                body=Encoding.UTF8.GetString(ms.ToArray());
            }
            catch(DecoderFallbackException){return Results.BadRequest();}
            Dictionary<string,string> payload;
            try{payload=PayablHostedClient.ParseForm(body);}
            catch(FormatException){return Results.BadRequest();}
            if(!payload.TryGetValue("transactionid",out var transaction)||
               !payload.TryGetValue("type",out var type)||
               !payload.TryGetValue("errorcode",out var errorCode)||
               !payload.TryGetValue("timestamp",out var timestamp)||
               !payload.TryGetValue("security",out var security)||
               !payload.TryGetValue("orderid",out var orderId)||
               !settings.VerifyNotification(transaction,type,errorCode,timestamp,security)||
               orderId is not {Length:36}||
               !orderId.StartsWith("lfc_",StringComparison.Ordinal)||
               !orderId.AsSpan(4).ToString().All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return Results.StatusCode(403);
            if(!long.TryParse(timestamp,NumberStyles.None,
                    CultureInfo.InvariantCulture,out var seconds))
                return Results.StatusCode(403);
            var eventHash=Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(orderId+"|"+transaction+"|"+type+"|"+
                    errorCode+"|"+timestamp+"|"+security)));
            var recorded=await store.RecordNotificationAsync(orderId,
                transaction,type,errorCode,DateTimeOffset.FromUnixTimeSeconds(seconds),
                eventHash,ct);
            return recorded.Accepted?Results.Text("OK","text/plain"):
                Results.StatusCode(503);
        }).WithName("ReceivePayablPaymentNotification")
          .WithTags("Payment provider callbacks")
          .Accepts<string>("application/x-www-form-urlencoded")
          .Produces(200).Produces(400).Produces(403)
          .Produces(413).Produces(415).Produces(503);
        return app;
    }

    private static async Task<(string Cookie,string Owner)> Authorized(
        WebAdminService service,string? owner,HttpContext context,CancellationToken ct)
    {
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization",
                "Choose an authorized organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await service.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(!AdminPermissions.Has(session.User.Role,"finance:write")||
           !session.User.Organizations.Any(x=>x.Id==owner))
            throw new WebAdminException(403,"not_authorized","Finance role required.");
        return (cookie!,owner);
    }

    private static async Task<T> Body<T>(HttpContext context,CancellationToken ct)
    {
        try
        {
            using var document=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=5},ct);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in document.RootElement.EnumerateObject())
                if(!keys.Add(item.Name))throw new JsonException();
            return document.Deserialize<T>(Json)??throw new JsonException();
        }
        catch(JsonException)
        {
            throw new WebAdminException(400,"invalid_json","Provide one valid payment object.");
        }
    }
    private static void Errors(RouteHandlerBuilder endpoint)
    {
        foreach(var status in new[]{400,401,403,409,413,415,422,503})
            endpoint.Produces<WebAdminError>(status);
    }
}
public sealed record AdminPayablCheckoutEmptyBody();
