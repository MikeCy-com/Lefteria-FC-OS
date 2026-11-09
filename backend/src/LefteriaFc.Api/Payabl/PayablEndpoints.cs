using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.Payabl;

public sealed record PayablPaymentCreateCommand(
    Guid ClientRequestId, long AmountCents, string? Currency, string? Purpose,
    string? PayerEmail, string? PayerFirstName, string? PayerLastName,
    Guid? FeeIssueId = null);

/// <summary>Payment requests are an opt-in SANDBOX admin integration, not an
/// order-capture or subscription collection system. Never display a payment
/// as paid on a browser redirect; only signed callbacks alter settlement.</summary>
public static class PayablEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 5
    };

    public static IServiceCollection AddPayabl(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<PayablOptions>().Bind(config.GetSection("Payabl"))
            .Validate(PayablOptions.Safe, "Payabl is sandbox-only and requires a secure origin and merchant credentials.")
            .ValidateOnStart();
        services.AddOptions<TwilioPaymentSmsOptions>().Bind(config.GetSection("TwilioPaymentSms"))
            .Validate(TwilioPaymentSmsOptions.Safe,
                "Twilio payment SMS requires an approved Messaging Service and credentials.")
            .ValidateOnStart();
        services.AddHttpClient<TwilioPaymentSms>(client =>
        {
            client.Timeout=TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize=4096;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect=false,UseCookies=false
        });
        services.AddHttpClient<PayablGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 8192;
            client.DefaultRequestHeaders.Accept.ParseAdd("application/x-www-form-urlencoded");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
        return services;
    }

    public static IEndpointRouteBuilder MapPayabl(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/finance/payabl")
            .WithTags("Payabl finance (sandbox)").RequireAuthorization(WebAdminServices.Policy);
        var status = group.MapGet("/status", async (string? organization_id, WebAdminService admin,
            IOptions<PayablOptions> settings, HttpContext context, CancellationToken ct) =>
        {
            await Authorized(organization_id, admin, context, false, ct);
            return Results.Ok(new { provider = "payabl", configured = settings.Value.Enabled,
                environment = "sandbox", payment_links = settings.Value.Enabled,
                subscriptions = false, employee_card_payouts = false, refunds = false,
                live_transactions = false });
        }).WithName("GetPayablAdminStatus");
        var list = group.MapGet("/payment-requests", async (string? organization_id,
            WebAdminService admin, WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Authorized(organization_id, admin, context, false, ct);
            await using var db = await connections.OpenAsync(ct);
            await using var query = new NpgsqlCommand(
                "SELECT finance.read_admin_payment_requests(@digest,@owner)::text", db);
            query.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            query.Parameters.AddWithValue("owner", organization_id!);
            var value = await query.ExecuteScalarAsync(ct) as string;
            if (value is null) throw new WebAdminException(503, "payment_ledger_unavailable", "Payment ledger unavailable.");
            return Results.Content(value, "application/json", Encoding.UTF8, 200);
        }).WithName("ListPayablAdminPaymentRequests");
        var create = group.MapPost("/payment-requests", async (string? organization_id,
            WebAdminService admin, WebAdminConnections connections, IWebAdminSecrets secrets,
            PayablGateway gateway, IOptions<PayablOptions> settings,
            HttpContext context, CancellationToken ct) =>
        {
            var cookie = await Authorized(organization_id, admin, context, true, ct);
            if (!gateway.Available)
                throw new WebAdminException(503, "payabl_not_configured",
                    "Payabl sandbox has not been enabled by the operator.");
            var input = await ParseBody(context, ct);
            Validate(input);
            var id = Guid.NewGuid();
            var callbackNonce = secrets.NewToken();
            var digest = PayablSignature.CallbackDigest(callbackNonce);
            Guid storedId;
            bool created;
            string? checkoutUrl;
            string? existingStatus;
            await using (var db = await connections.OpenAsync(ct))
            await using (var query = new NpgsqlCommand("""
                SELECT finance.create_admin_payment_request(
                  @digest,@owner,@id,@client,@nonce,@purpose,@amount,
                  @email,@first,@last
                )::text
                """, db))
            {
                query.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
                query.Parameters.AddWithValue("owner", organization_id!);
                query.Parameters.AddWithValue("id", id);
                query.Parameters.AddWithValue("client", input.ClientRequestId);
                query.Parameters.AddWithValue("nonce", digest);
                query.Parameters.AddWithValue("purpose", input.Purpose!);
                query.Parameters.AddWithValue("amount", input.AmountCents);
                query.Parameters.AddWithValue("email", input.PayerEmail!);
                query.Parameters.AddWithValue("first", input.PayerFirstName!);
                query.Parameters.AddWithValue("last", input.PayerLastName!);
                var raw = await query.ExecuteScalarAsync(ct) as string;
                if (raw is null) throw new WebAdminException(503, "payment_ledger_unavailable", "Payment ledger unavailable.");
                using var row = JsonDocument.Parse(raw);
                storedId = row.RootElement.GetProperty("id").GetGuid();
                created = row.RootElement.GetProperty("created").GetBoolean();
                existingStatus = row.RootElement.GetProperty("status").GetString();
                checkoutUrl = row.RootElement.GetProperty("checkout_url").ValueKind == JsonValueKind.String
                    ? row.RootElement.GetProperty("checkout_url").GetString() : null;
            }
            // A training-fee payment must be joined to its exact child,
            // issued month, guardian and amount BEFORE any outbound gateway
            // call. A manually supplied total cannot override the invoice.
            if (created && input.FeeIssueId is Guid feeIssue)
            {
                await using var attachDb=await connections.OpenAsync(ct);
                await using var attach=new NpgsqlCommand("""
                    SELECT finance.attach_admin_training_fee_payment(
                      @digest,@owner,@fee,@request)
                    """,attachDb);
                attach.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
                attach.Parameters.AddWithValue("owner",organization_id!);
                attach.Parameters.AddWithValue("fee",feeIssue);
                attach.Parameters.AddWithValue("request",storedId);
                try
                {
                    if(await attach.ExecuteScalarAsync(ct) is not true)
                        throw new WebAdminException(503,"fee_attachment_unavailable",
                            "Training-fee payment could not be bound to the invoice.");
                }
                catch(PostgresException error) when(error.SqlState is "42501" or "23505" or "22023")
                {
                    throw new WebAdminException(error.SqlState=="23505" ? 409 : 403,
                        "fee_payment_mismatch","The payment must match an explicitly issued training fee.");
                }
            }
            // The same client request ID must never re-initiate a gateway
            // transaction after an uncertain outbound response.
            if (!created) return Results.Ok(new { id = storedId, status = existingStatus,
                checkout_url = checkoutUrl, replay = true });
            var origin = settings.Value.PublicOrigin.TrimEnd('/');
            var callbackUrl = origin + "/api/payabl/webhooks/" + storedId.ToString("D") + "/" + callbackNonce;
            var returnUrl = origin + "/payments/return";
            PayablCheckoutResult hosted;
            try
            {
                hosted = await gateway.InitializeAsync(new PayablCheckoutRequest(
                    PayablNotification.OrderId(storedId), input.AmountCents, "EUR",
                    input.PayerEmail!, input.PayerFirstName!, input.PayerLastName!,
                    context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1",
                    callbackUrl, returnUrl), ct);
            }
            catch (Exception error) when (error is PayablUnavailableException or
                HttpRequestException or TaskCanceledException)
            {
                // Intentionally leave the attempt 'initiating' for operator
                // reconciliation; a silent retry can create duplicate links.
                throw new WebAdminException(503, "payabl_init_uncertain",
                    "Gateway initialization is uncertain; do not retry this request ID.");
            }
            await using (var db = await connections.OpenAsync(ct))
            await using (var update = new NpgsqlCommand(
                "SELECT finance.ready_admin_payment_request(@digest,@owner,@id,@url,@transaction)", db))
            {
                update.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
                update.Parameters.AddWithValue("owner", organization_id!);
                update.Parameters.AddWithValue("id", storedId);
                update.Parameters.AddWithValue("url", hosted.CheckoutUrl);
                update.Parameters.AddWithValue("transaction", hosted.TransactionId);
                if (await update.ExecuteScalarAsync(ct) is not true)
                    throw new WebAdminException(503, "payment_ledger_unavailable", "Payment record cannot be confirmed.");
            }
            return Results.Ok(new { id = storedId, status = "ready",
                checkout_url = hosted.CheckoutUrl, replay = false });
        }).WithName("CreatePayablAdminPaymentRequest")
          .Accepts<PayablPaymentCreateCommand>("application/json");

        foreach (var route in new[] { status, list, create })
            foreach (var code in new[] { 400,401,403,409,413,415,422,503 })
                route.Produces<WebAdminError>(code);

        endpoints.MapPost("/api/payabl/webhooks/{id:guid}/{nonce}",
            async (Guid id, string nonce, HttpContext context,
                LefteriaDbContext database, IOptions<PayablOptions> settings,
                CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!settings.Value.Enabled || !PayablOptions.Safe(settings.Value))
                    return Results.NotFound();
                if (nonce.Length != 43 || nonce.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
                    context.Request.ContentLength is > 4096 ||
                    context.Request.ContentType is null ||
                    !context.Request.ContentType.StartsWith("application/x-www-form-urlencoded",
                        StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest();
                // Independently limit chunked bodies, reject duplicates and do
                // not log raw callback payloads, PAN or provider response.
                await using var buffer = new MemoryStream();
                var chunk = new byte[1024];
                int n;
                while ((n = await context.Request.Body.ReadAsync(chunk, ct)) != 0)
                {
                    if (buffer.Length + n > 4096) return Results.BadRequest();
                    await buffer.WriteAsync(chunk.AsMemory(0, n), ct);
                }
                Dictionary<string, string> values;
                try { values = PayablGateway.ParseFields(Encoding.ASCII.GetString(buffer.ToArray())); }
                catch (PayablUnavailableException) { return Results.BadRequest(); }
                if (!PayablNotification.TryRead(values, id, settings.Value.Secret,
                    DateTimeOffset.UtcNow, out var notice) || notice is null)
                    return Results.BadRequest();
                var db = database.Database.GetDbConnection();
                await database.Database.OpenConnectionAsync(ct);
                try
                {
                    await using var query = db.CreateCommand();
                    query.CommandText = """
                        SELECT finance.apply_payabl_notification(
                          @id,@nonce,@transaction,@kind,@code,@stamp,@order,@state,@amount,@currency)
                        """;
                    var parameters = new (string, object)[]
                    {
                        ("id",id),("nonce",PayablSignature.CallbackDigest(nonce)),
                        ("transaction",notice.TransactionId),("kind",notice.Kind),
                        ("code",notice.ErrorCode),("stamp",notice.Timestamp),
                        ("order",notice.OrderId),("state",notice.PaymentState),
                        ("amount",notice.AmountCents is long amount ? (object)amount : DBNull.Value),
                        ("currency",notice.Currency is string currency ? (object)currency : DBNull.Value)
                    };
                    foreach (var (key, value) in parameters)
                    {
                        var param = query.CreateParameter();
                        param.ParameterName = key;
                        if (key == "amount") param.DbType = System.Data.DbType.Int64;
                        if (key == "currency") param.DbType = System.Data.DbType.String;
                        param.Value = value;
                        query.Parameters.Add(param);
                    }
                    await query.ExecuteScalarAsync(ct);
                    return Results.Ok();
                }
                catch (PostgresException error) when (error.SqlState is "42501" or "22023")
                { return Results.BadRequest(); }
                finally { await database.Database.CloseConnectionAsync(); }
            })
            .WithTags("Payabl signed notifications")
            .WithName("ReceivePayablPaymentNotification")
            .Accepts<string>("application/x-www-form-urlencoded")
            .Produces(200).Produces(400).Produces(404);
        return endpoints;
    }

    private static async Task<string> Authorized(string? owner, WebAdminService admin,
        HttpContext context, bool write, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (owner is null || !PublicContentService.IsOrganization(owner) ||
            context.Request.Query["organization_id"].Count != 1)
            throw new WebAdminException(400, "invalid_organization", "Choose one organization.");
        var cookie = context.Request.Cookies[WebAdminServices.CookieName];
        var session = await admin.CurrentAsync(cookie, ct);
        if (session is null) throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (!AdminPermissions.Has(session.User.Role, write ? "finance:write" : "finance:read") ||
            !session.User.Organizations.Any(org => org.Id == owner))
            throw new WebAdminException(403, "not_authorized", "Finance access denied.");
        return cookie!;
    }

    private static async Task<PayablPaymentCreateCommand> ParseBody(HttpContext context, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 5 }, ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
                if (!names.Add(item.Name)) throw new JsonException();
            return document.Deserialize<PayablPaymentCreateCommand>(Json) ?? throw new JsonException();
        }
        catch (JsonException) { throw new WebAdminException(400, "invalid_json", "Invalid payment request."); }
    }

    private static void Validate(PayablPaymentCreateCommand request)
    {
        static bool Ascii(string? value, int min, int max) =>
            value is not null && value.Length >= min && value.Length <= max &&
            value.All(c => c >= ' ' && c <= '~');
        if (request.FeeIssueId==Guid.Empty)
            throw new WebAdminException(422,"invalid_fee_issue","Select a valid issued training fee.");
        if (request.ClientRequestId == Guid.Empty || request.Currency != "EUR" ||
            request.AmountCents is < 1 or > 100000000 ||
            !Ascii(request.Purpose, 3, 140) || request.Purpose != request.Purpose!.Trim() ||
            !Ascii(request.PayerEmail, 3, 50) || !request.PayerEmail!.Contains('@') ||
            !Ascii(request.PayerFirstName, 1, 50) ||
            !Ascii(request.PayerLastName, 1, 50))
            throw new WebAdminException(422, "invalid_payment_request",
                "Provide a valid EUR amount, a unique request ID and ASCII test customer details.");
    }
}
