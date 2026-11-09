using System.Globalization;
using System.Text.RegularExpressions;

namespace LefteriaFc.Api.Payabl;

/// <summary>
/// Strict, provider-aware interpretation of a Payabl form callback. Only
/// transaction, type, code and timestamp are signed by the gateway. The
/// order, state, currency and amount must be checked independently and must
/// never be treated as cryptographically authenticated fields.
/// </summary>
public sealed record PayablNotice(
    string OrderId, string TransactionId, string Kind, int ErrorCode,
    long Timestamp, string PaymentState, long? AmountCents, string? Currency);

public static partial class PayablNotification
{
    [GeneratedRegex(@"\A(?:0|[1-9][0-9]{0,6})(?:\.[0-9]{2})?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex StrictAmount();

    [GeneratedRegex(@"\A[a-z_]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StrictState();

    public static bool TryRead(
        IReadOnlyDictionary<string,string> values, Guid paymentId,
        string secret, DateTimeOffset now, out PayablNotice? notice)
    {
        notice = null;
        if (paymentId == Guid.Empty ||
            !PayablSignature.Notification(values, secret, now) ||
            !values.TryGetValue("orderid", out var orderId) ||
            !string.Equals(orderId, "LFC" + paymentId.ToString("N"), StringComparison.Ordinal) ||
            !values.TryGetValue("transactionid", out var transaction) ||
            !values.TryGetValue("type", out var kind) ||
            !int.TryParse(values["errorcode"], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var code) ||
            !long.TryParse(values["timestamp"], NumberStyles.None,
                CultureInfo.InvariantCulture, out var stamp))
            return false;

        var state = values.GetValueOrDefault("payment_state") ?? "";
        if (state.Length != 0 && !StrictState().IsMatch(state)) return false;

        long? cents = null;
        if (values.TryGetValue("amount", out var amount))
        {
            if (amount is null || !StrictAmount().IsMatch(amount) ||
                !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 0m or > 1000000m)
                return false;
            cents = decimal.ToInt64(parsed * 100m);
        }

        string? currency = null;
        if (values.TryGetValue("currency", out var currencyText))
        {
            if (currencyText is null || currencyText.Length != 3 ||
                !currencyText.All(c => c is >= 'A' and <= 'Z'))
                return false;
            currency = currencyText;
        }

        notice = new PayablNotice(orderId, transaction, kind, code, stamp,
            state, cents, currency);
        return true;
    }
}
