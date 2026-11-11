using System.Security.Cryptography;
using System.Text;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Validates payabl.'s *simplified server notification* checksum.
/// This does NOT itself confirm a captured payment or authorize ledger writes:
/// order/merchant binding, amount/currency, provider-side status and idempotent
/// database reconciliation are independent required gates.
/// </summary>
public static class PayablNotificationVerifier
{
    public sealed record VerifiedNotification(
        string TransactionId, string Type, string ErrorCode,
        long Timestamp, string OrderId, string EvidenceSha256);

    private static bool Digits(string? input, int max) =>
        input is { Length: > 0 } value && value.Length <= max &&
        value.All(c => c is >= '0' and <= '9');

    public static VerifiedNotification? Verify(
        IReadOnlyDictionary<string, string> fields,
        string merchantSecret,
        DateTimeOffset receivedAt,
        TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(merchantSecret) ||
            merchantSecret.Length > 512 ||
            maxAge <= TimeSpan.Zero || maxAge > TimeSpan.FromHours(24) ||
            fields.Count is < 6 or > 32 ||
            fields.Keys.Any(k => k.Length is < 1 or > 80) ||
            !fields.TryGetValue("transactionid", out var id) ||
            !Digits(id, 30) ||
            !fields.TryGetValue("type", out var kind) ||
            kind is not ("capture" or "authorization" or "refund") ||
            !fields.TryGetValue("errorcode", out var errorCode) ||
            !Digits(errorCode, 8) ||
            !fields.TryGetValue("timestamp", out var textTimestamp) ||
            !Digits(textTimestamp, 12) ||
            !long.TryParse(textTimestamp, out var timestamp) ||
            !fields.TryGetValue("orderid", out var orderId) ||
            string.IsNullOrWhiteSpace(orderId) || orderId.Length > 40 ||
            !fields.TryGetValue("security", out var signature) ||
            signature.Length != 64 ||
            !signature.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return null;

        DateTimeOffset occurredAt;
        try { occurredAt = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
        catch (ArgumentOutOfRangeException) { return null; }
        if (occurredAt > receivedAt.AddMinutes(5) ||
            occurredAt < receivedAt.Subtract(maxAge))
            return null;

        // payabl. 2026 simplified notification:
        // sha256(transactionid + type + errorcode + timestamp + merchantSecret).
        // No value is URI encoded before hashing. This does not use the request
        // signature (SHA-1), which has a DIFFERENT protocol.
        var input = id + kind + errorCode + textTimestamp + merchantSecret;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // The 64-character lowercase hex check above guarantees that
        // FromHexString decodes exactly 32 bytes without malformed input.
        var supplied = Convert.FromHexString(signature);
        if (!CryptographicOperations.FixedTimeEquals(hash, supplied))
            return null;

        // Evidence digest is for deduplicated audit, never the raw signature
        // or secret. It is intentionally not a payment success assertion.
        var evidence = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                id + "|" + kind + "|" + errorCode + "|" +
                textTimestamp + "|" + orderId))).ToLowerInvariant();
        return new VerifiedNotification(
            id, kind, errorCode, timestamp, orderId, evidence);
    }

    public static bool IsSuccessfulCapture(VerifiedNotification value) =>
        value.Type == "capture" && value.ErrorCode == "0";
}