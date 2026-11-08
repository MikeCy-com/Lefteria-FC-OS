using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Finance;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Finance;

public sealed class PostgresPayablFinanceStore(
    WebAdminConnections adminConnections,IWebAdminSecrets secrets,
    LefteriaDbContext database) : IPayablFinanceStore
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        NumberHandling=JsonNumberHandling.Strict,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminPayablPaymentDto> CreateAsync(string cookie,string owner,
        AdminPayablPaymentCreateBody body,CancellationToken token)
    {
        Verify(cookie,owner);
        if(body.Purpose is not ("academy_fee" or "subscription_invoice" or
             "shop_order" or "event_ticket" or "donation" or "other")||
           body.Title is not {Length:>=2 and <=140}||
           body.Title!=body.Title.Trim()||body.Title.Any(char.IsControl)||
           body.AmountCents is null or <1 or >10000000||
           !ValidName(body.FirstName)||!ValidName(body.LastName)||
           !ValidEmail(body.Email))throw Invalid();
        var id="pay_"+Guid.NewGuid().ToString("N");
        var publicToken=Guid.NewGuid().ToString("N");
        var result=await AdminQueryAsync<AdminPayablPaymentDto>("""
            SELECT finance.create_admin_payabl_payment(
              @digest,@owner,@id,@token,@purpose,@title,
              @amount,@first,@last,@email)::text
            """,cookie,owner,[
                new("id",id),new("token",publicToken),new("purpose",body.Purpose),
                new("title",body.Title),new("amount",body.AmountCents.Value),
                new("first",body.FirstName),new("last",body.LastName),
                new("email",body.Email)
            ],token);
        if(result.Id!=id||result.PublicToken!=publicToken||
           result.AmountCents!=body.AmountCents||result.Currency!="EUR"||
           result.Status!="draft"||result.Version!=1)
            throw Unavailable();
        return result;
    }

    public async Task<AdminPayablPaymentsDto> ListAsync(string cookie,string owner,
        CancellationToken token)
    {
        Verify(cookie,owner);
        var data=await AdminQueryAsync<AdminPayablPaymentsDto>("""
            SELECT finance.read_admin_payabl_payments(@digest,@owner)::text
            """,cookie,owner,[],token);
        if(data.OrganizationId!=owner||data.Payments is null||
           data.Payments.Count>100||data.Payments.Any(x=>
               !ValidId(x.Id)||!ValidToken(x.PublicToken)||
               x.AmountCents is <1 or >10000000||x.Currency!="EUR"||
               x.Status is not ("draft" or "initiating" or "checkout_ready" or
                  "paid" or "declined" or "cancelled")))
            throw Unavailable();
        return data;
    }

    public async Task<PayablCheckoutClaimDto> ClaimCheckoutAsync(
        string cookie,string owner,string id,Guid lease,CancellationToken token)
    {
        Verify(cookie,owner);
        if(!ValidId(id)||lease==Guid.Empty)throw Invalid();
        var data=await AdminQueryAsync<PayablCheckoutClaimDto>("""
            SELECT finance.claim_admin_payabl_checkout(
              @digest,@owner,@id,@lease)::text
            """,cookie,owner,[new("id",id),new("lease",lease)],token);
        if(data.Id!=id||data.Lease!=lease||
           data.OrderId!="lfc_"+id[4..]||data.AmountCents is <1 or >10000000||
           !ValidName(data.PayerFirstName)||!ValidName(data.PayerLastName)||
           !ValidEmail(data.PayerEmail))throw Unavailable();
        return data;
    }

    public async Task<PayablCheckoutResultDto> CompleteCheckoutAsync(
        string cookie,string owner,string id,Guid lease,
        string? url,string? transaction,bool success,CancellationToken token)
    {
        Verify(cookie,owner);
        if(!ValidId(id)||lease==Guid.Empty||
            (success&&(url is null||url.Length>2048||
              transaction is null||!transaction.All(char.IsAsciiDigit)||
              transaction.Length is <1 or >50)))throw Invalid();
        var data=await AdminQueryAsync<PayablCheckoutResultDto>("""
            SELECT finance.complete_admin_payabl_checkout(
              @digest,@owner,@id,@lease,@url,@transaction,@success)::text
            """,cookie,owner,[
                new("id",id),new("lease",lease),
                new("url",NpgsqlDbType.Text){Value=(object?)url??DBNull.Value},
                new("transaction",NpgsqlDbType.Text){Value=(object?)transaction??DBNull.Value},
                new("success",success)
            ],token);
        if(data.Id!=id||!ValidToken(data.PublicToken)||
           data.Status!=(success?"checkout_ready":"draft")||
           data.Version<2)throw Unavailable();
        return data;
    }

    public Task<PublicPayablLinkDto> PublicLinkAsync(string publicToken,
        CancellationToken token)
    {
        if(!ValidToken(publicToken))throw Invalid();
        return RuntimeQueryAsync<PublicPayablLinkDto>(
            "SELECT finance.read_public_payabl_link(@token)::text",
            [new("token",publicToken)],token);
    }
    public Task<PayablNotificationResultDto> RecordNotificationAsync(
        string orderId,string transactionId,string transactionType,
        string errorCode,DateTimeOffset timestamp,string digest,
        CancellationToken token)
    {
        if(orderId.Length!=36||!ValidToken(orderId[4..])||
           transactionId.Length is <1 or >50||
           transactionType.Length is <1 or >30||
           errorCode.Length is <1 or >8||digest.Length!=64)
            throw Invalid();
        return RuntimeQueryAsync<PayablNotificationResultDto>("""
            SELECT finance.record_payabl_notification(
              @order,@transaction,@kind,@error,@when,@digest)::text
            """,[
                new("order",orderId),new("transaction",transactionId),
                new("kind",transactionType),new("error",errorCode),
                new("when",timestamp),new("digest",digest)
            ],token);
    }

    private async Task<T> AdminQueryAsync<T>(string sql,string cookie,
        string owner,NpgsqlParameter[] extra,CancellationToken token)
    {
        try
        {
            await using var connection=await adminConnections.OpenAsync(token);
            await using var command=new NpgsqlCommand(sql,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            foreach(var arg in extra)command.Parameters.Add(arg);
            var raw=await command.ExecuteScalarAsync(token) as string;
            return raw is not null&&JsonSerializer.Deserialize<T>(raw,Json) is { } dto
                ?dto:throw Unavailable();
        }
        catch(PostgresException e){throw Translate(e);}
    }

    private async Task<T> RuntimeQueryAsync<T>(
        string sql,NpgsqlParameter[] extra,CancellationToken token)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand(sql,
                    (NpgsqlConnection)database.Database.GetDbConnection());
                foreach(var arg in extra)command.Parameters.Add(arg);
                var raw=await command.ExecuteScalarAsync(token) as string;
                return raw is not null&&JsonSerializer.Deserialize<T>(raw,Json) is { } dto
                    ?dto:throw Unavailable();
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException e){throw Translate(e);}
    }

    private void Verify(string cookie,string owner)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Choose your organization.");
    }
    private static bool ValidId(string? id)=>id is {Length:36}&&
        id.StartsWith("pay_",StringComparison.Ordinal)&&ValidToken(id[4..]);
    private static bool ValidToken(string? token)=>token is {Length:32}&&
        token.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidName(string? text)=>text is {Length:>=1 and <=50}&&
        text==text.Trim()&&!text.Any(char.IsControl);
    private static bool ValidEmail(string? email)=>email is {Length:>=3 and <=50}&&
        email.Count(x=>x=='@')==1&&!email.Any(char.IsWhiteSpace)&&
        email.IndexOf('@')>0&&email.LastIndexOf('.')>email.IndexOf('@')+1;
    private static WebAdminException Invalid()=>new(422,
        "invalid_payabl_request","Invalid payment details.");
    private static WebAdminException Unavailable()=>new(503,
        "payabl_storage_unavailable","Payment storage could not be verified.");
    private static WebAdminException Translate(PostgresException error)=>
        error.SqlState switch{
            "42501"=>new(403,"not_authorized","Finance access is not authorized."),
            "40001" or "23505"=>new(409,"payabl_conflict",
                "This checkout has already changed; refresh."),
            "22023" or "23503"=>Invalid(),
            _=>Unavailable()
        };
}
