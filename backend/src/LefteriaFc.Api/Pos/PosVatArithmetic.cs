namespace LefteriaFc.Api.Pos;

/// <summary>
/// Exact integer-cent arithmetic for a separately reviewed, VAT-inclusive
/// treatment. This does not decide which Cyprus VAT rate applies to any
/// product, and cannot activate an unreviewed tax policy.
/// </summary>
internal static class PosVatArithmetic
{
    internal sealed record ApprovedTreatment(
        string Code,
        int? BasisPoints,
        int Revision,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveUntil,
        bool Enabled);

    internal sealed record Breakdown(
        long GrossCents,
        long NetCents,
        long VatCents,
        int? VatBasisPoints,
        string TreatmentCode,
        int PolicyRevision);

    internal static Breakdown FromGross(
        long grossCents, DateOnly saleDate, ApprovedTreatment treatment)
    {
        if (grossCents is < 0 or > 100000000 ||
            !treatment.Enabled ||
            treatment.Revision < 1 ||
            saleDate < treatment.EffectiveFrom ||
            (treatment.EffectiveUntil is { } until && saleDate > until))
            throw new ArgumentException("The sale lacks an effective reviewed VAT treatment.");

        var expected = treatment.Code switch
        {
            "standard_19" => 1900,
            "reduced_9" => 900,
            "reduced_5" => 500,
            "reduced_3" => 300,
            "zero_0" => 0,
            "exempt" => (int?)null,
            _ => throw new ArgumentException("Unknown VAT treatment.")
        };
        if (expected != treatment.BasisPoints)
            throw new ArgumentException("VAT treatment and rate mismatch.");

        // Prices are VAT-inclusive. One line is rounded once to the nearest
        // cent, with half-cent values rounded away from zero (all gross >= 0).
        // Zero-rated and exempt remain semantically distinct even though both
        // produce a zero tax amount; do not silently interchange them.
        var vat = expected is { } rate && rate > 0
            ? checked((grossCents * rate + (10000L + rate) / 2) / (10000L + rate))
            : 0L;
        var net = checked(grossCents - vat);
        if (vat < 0 || net < 0 || net + vat != grossCents)
            throw new InvalidOperationException("VAT arithmetic failed reconciliation.");

        return new Breakdown(grossCents, net, vat,
            expected, treatment.Code, treatment.Revision);
    }
}
