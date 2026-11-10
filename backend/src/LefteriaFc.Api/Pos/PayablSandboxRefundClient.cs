using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Explicit sandbox-only, server-to-server Payabl refund request.
/// Never call this directly from an admin/browser action. An independently
/// verified original captured payment, locked refund request, merchant
/// configuration and retry/unknown-outcome policy must be supplied by a
/// separately audited worker before enabling the adapter.
/// An HTTP 200 or status=0 does not change POS refund or sale state.
/// </summary>
internal static class PayablSandboxRefundClient
{
    private static readonly Uri SandboxEndpoint =
        new("https://sandbox.payabl.com/pay/backoffice/payment_refund");

    // This adapter creates its own restricted HTTP transport to guarantee
    // no redirect can forward a signed form to another URL or host.
    private static readonly HttpClient SandboxHttp = new(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    internal sealed record RefundCommand(
        string MerchantId,
        string CapturedTransactionId,
        long AmountCents,
        string MerchantSecret);

    internal sealed record RefundGatewayResponse(
        string ProviderTransactionId,
        string? ProviderStatus,
        string? ErrorCode,
        string? ErrorMessage,
        bool RequiresConfirmation);

    internal static string CreateSignature(
        IReadOnlyDictionary<string, string> parameters,
        string merchantSecret)
    {
        if (string.IsNullOrWhiteSpace(merchantSecret) ||
            merchantSecret.Any(c => c > 255))
            throw new ArgumentException("Latin-1 merchant credential required.",
                nameof(merchantSecret));
        // payabl. request signatures concatenate values in ordinal
        // alphabetical parameter-name order, then the merchant secret.
        var ordered = string.Concat(parameters
            .Where(pair => !string.Equals(pair.Key, "signature", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)) + merchantSecret;
        return Convert.ToHexString(SHA1.HashData(Encoding.Latin1.GetBytes(ordered)))
            .ToLowerInvariant();
    }

    internal static async Task<RefundGatewayResponse> RequestAsync(
        RefundCommand refund, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refund.MerchantId) ||
            refund.MerchantId.Length > 40 ||
            !refund.MerchantId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            string.IsNullOrWhiteSpace(refund.CapturedTransactionId) ||
            refund.CapturedTransactionId.Length > 50 ||
            !refund.CapturedTransactionId.All(c => c is >= '0' and <= '9') ||
            refund.AmountCents is < 1 or > 999999999 ||
            string.IsNullOrWhiteSpace(refund.MerchantSecret) ||
            refund.MerchantSecret.Length > 512)
            throw new ArgumentException("Invalid sandbox refund request.");

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchantid"] = refund.MerchantId,
            ["transactionid"] = refund.CapturedTransactionId,
            ["currency"] = "EUR",
            ["amount"] = (refund.AmountCents / 100m).ToString(
                "0.00", CultureInfo.InvariantCulture)
        };
        fields["signature"] = CreateSignature(fields, refund.MerchantSecret);

        using var message = new HttpRequestMessage(HttpMethod.Post, SandboxEndpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        // Strictly sandbox, no redirects (including downgrade or live host).
        // Redirect following is disabled in the dedicated HTTP transport.
        using var response = await SandboxHttp.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            throw new InvalidOperationException("Gateway redirect prohibited.");
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        if (raw.Length > 8192)
            throw new InvalidOperationException("Gateway reply over size limit.");
        var answer = ParseFormResponse(raw);
        if (!answer.TryGetValue("transactionid", out var id) ||
            id != refund.CapturedTransactionId ||
            !answer.TryGetValue("currency", out var currency) ||
            currency != "EUR" ||
            !answer.TryGetValue("amount", out var amountText) ||
            !decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            amount != refund.AmountCents / 100m)
            throw new InvalidOperationException("Gateway refund identity mismatch.");

        answer.TryGetValue("status", out var status);
        answer.TryGetValue("errorcode", out var error);
        answer.TryGetValue("errormessage", out var messageText);
        // Even status=0 requires independently reconciled callback/settlement.
        // No code here writes refund_events or finance journals.
        return new RefundGatewayResponse(id, status, error, messageText, true);
    }

    internal static IReadOnlyDictionary<string, string> ParseFormResponse(string input)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(input) || input.Length > 8192)
            throw new InvalidOperationException("Malformed sandbox response.");
        foreach (var pair in input.Split('&'))
        {
            var index = pair.IndexOf('=');
            if (index <= 0)
                throw new InvalidOperationException("Malformed gateway response pair.");
            var name = WebUtility.UrlDecode(pair[..index]);
            var value = WebUtility.UrlDecode(pair[(index + 1)..]);
            if (string.IsNullOrEmpty(name) || name.Length > 64 ||
                value.Length > 512 || !output.TryAdd(name, value))
                throw new InvalidOperationException("Duplicate or unbounded gateway field.");
        }
        return output;
    }
}
