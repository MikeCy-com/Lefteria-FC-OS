using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Api.Payabl;

/// <summary>Gateway-only adapter. No card numbers, CVV or salary-card details
/// ever pass through the OS. Sandbox only until contract/merchant approval.</summary>
public sealed class PayablOptions
{
    public bool Enabled { get; set; }
    public bool Sandbox { get; set; } = true;
    public string MerchantId { get; set; } = "";
    public string Secret { get; set; } = "";
    public string PublicOrigin { get; set; } = "";
    public static bool Safe(PayablOptions value) =>
        !value.Enabled || (value.Sandbox &&
        value.MerchantId is { Length: >= 1 and <= 40 } &&
        value.MerchantId.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-') &&
        value.Secret.Length is >= 16 and <= 128 &&
        value.Secret.All(c => c >= '!' && c <= '~') &&
        Uri.TryCreate(value.PublicOrigin, UriKind.Absolute, out var origin) &&
        origin.Scheme == Uri.UriSchemeHttps && origin.UserInfo.Length == 0 &&
        origin.AbsolutePath == "/" && origin.Query.Length == 0 && origin.Fragment.Length == 0);
}

public static class PayablSignature
{
    // payabl. gateway signature is SHA1(values ordered by ordinal parameter
    // name + secret). SHA1 is required by provider; never use for our own auth.
    public static string Request(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        var joined = string.Concat(parameters.Where(x => x.Key != "signature")
            .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value)) + secret;
        if (joined.Any(c => c > 127))
            throw new ArgumentException("Payabl gateway requests must use ASCII-compatible customer fields.");
        return Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(joined))).ToLowerInvariant();
    }

    // The simplified notification signature binds transactionid,type,errorcode,
    // timestamp. It DOES NOT bind orderid: also require an opaque per-request
    // callback URL nonce before changing ledger state.
    public static bool Notification(IReadOnlyDictionary<string, string> values,
        string secret, DateTimeOffset now)
    {
        if (!values.TryGetValue("transactionid", out var transaction) ||
            !values.TryGetValue("type", out var type) ||
            !values.TryGetValue("errorcode", out var code) ||
            !values.TryGetValue("timestamp", out var timestamp) ||
            !values.TryGetValue("security", out var supplied) ||
            transaction is not { Length: >= 1 and <= 40 } ||
            !transaction.All(char.IsAsciiDigit) ||
            type is not ("capture" or "order" or "refund" or "cft" or "reversal") ||
            !int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ||
            code.Length > 11 ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) ||
            timestamp.Length is < 9 or > 12 ||
            supplied.Length != 64 ||
            !supplied.All(Uri.IsHexDigit))
            return false;
        DateTimeOffset eventTime;
        try { eventTime = DateTimeOffset.FromUnixTimeSeconds(unix); }
        catch (ArgumentOutOfRangeException) { return false; }
        // Replay deadline is deliberately bounded; old legitimate callbacks
        // become reconciliation exceptions, never automatic paid status.
        if ((now - eventTime).Duration() > TimeSpan.FromMinutes(15)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(transaction + type + code + timestamp + secret));
        Span<byte> provided = stackalloc byte[32];
        return Convert.TryFromHexString(supplied, provided, out var written) &&
            written == 32 && CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public static string CallbackDigest(string nonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(nonce))).ToLowerInvariant();
}

public sealed record PayablCheckoutRequest(
    string OrderId, long AmountCents, string Currency, string Email,
    string FirstName, string LastName, string CustomerIp, string NotificationUrl,
    string ReturnUrl);
public sealed record PayablCheckoutResult(string CheckoutUrl, string TransactionId);

public sealed class PayablGateway(HttpClient client, IOptions<PayablOptions> options)
{
    public bool Available => options.Value.Enabled && PayablOptions.Safe(options.Value);

    public async Task<PayablCheckoutResult> InitializeAsync(PayablCheckoutRequest order, CancellationToken ct)
    {
        var settings = options.Value;
        if (!Available)
            throw new PayablUnavailableException();
        if (order.AmountCents is < 1 or > 100000000 ||
            order.Currency != "EUR" || order.OrderId.Length is < 1 or > 40 ||
            !order.OrderId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
            order.Email.Length is < 3 or > 50 ||
            !order.Email.Contains('@') ||
            order.FirstName.Length is < 1 or > 50 ||
            order.LastName.Length is < 1 or > 50 ||
            !IPAddress.TryParse(order.CustomerIp, out _) ||
            !Uri.TryCreate(order.NotificationUrl, UriKind.Absolute, out var notify) ||
            notify.Scheme != "https" ||
            !Uri.TryCreate(order.ReturnUrl, UriKind.Absolute, out var ret) ||
            ret.Scheme != "https" ||
            !SameOrigin(notify, settings.PublicOrigin) ||
            !SameOrigin(ret, settings.PublicOrigin))
            throw new ArgumentException("Invalid hosted checkout request.");
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchantid"] = settings.MerchantId,
            ["orderid"] = order.OrderId,
            ["amount"] = (order.AmountCents / 100m).ToString("F2", CultureInfo.InvariantCulture),
            ["currency"] = "EUR",
            ["email"] = order.Email,
            ["firstname"] = order.FirstName,
            ["lastname"] = order.LastName,
            ["customerip"] = order.CustomerIp,
            ["notification_url"] = order.NotificationUrl,
            ["url_return"] = order.ReturnUrl,
            ["language"] = "en"
        };
        fields["signature"] = PayablSignature.Request(fields, settings.Secret);
        using var body = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync("https://sandbox.payabl.com/pay/payment/init", body, ct);
        if (!response.IsSuccessStatusCode)
            throw new PayablUnavailableException();
        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length > 8192) throw new PayablUnavailableException();
        var data = ParseFields(text);
        if (data.GetValueOrDefault("errorcode") != "0" ||
            !data.TryGetValue("start_url", out var url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            target.Scheme != "https" || target.UserInfo.Length != 0 ||
            target.Host is not ("sandbox.payabl.com" or "pay4.sandbox.payabl.com") ||
            !data.TryGetValue("transactionid", out var id) ||
            id.Length is < 1 or > 40 || !id.All(char.IsAsciiDigit))
            throw new PayablUnavailableException();
        return new PayablCheckoutResult(target.AbsoluteUri, id);
    }

    private static bool SameOrigin(Uri uri, string configured) =>
        Uri.TryCreate(configured, UriKind.Absolute, out var origin) &&
        uri.Scheme == origin.Scheme && uri.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == origin.Port && uri.UserInfo.Length == 0;

    public static Dictionary<string, string> ParseFields(string text)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = WebUtility.UrlDecode(pieces[0]);
            if (key.Length is < 1 or > 64 || !data.TryAdd(key, WebUtility.UrlDecode(pieces.Length == 2 ? pieces[1] : "")))
                throw new PayablUnavailableException();
        }
        return data;
    }
}

public sealed class PayablUnavailableException : Exception
{
    public PayablUnavailableException() : base("The Payabl gateway is unavailable.") { }
}
