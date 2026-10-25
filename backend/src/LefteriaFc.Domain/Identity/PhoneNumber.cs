using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LefteriaFc.Domain.Identity;

public sealed record PhoneNumber
{
    private PhoneNumber(string digits) => Digits = digits;

    public string Digits { get; }
    public string WireValue => "+" + Digits;

    public static PhoneNumber Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
        {
            throw new FormatException("A valid phone number is required.");
        }

        var digits = new StringBuilder(17);
        var hasPlus = false;
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
            {
                digits.Append(character);
                if (digits.Length > 17)
                {
                    throw new FormatException("The phone number is too long.");
                }
            }
            else if (character == '+' && digits.Length == 0 && !hasPlus)
            {
                hasPlus = true;
            }
            else if (character is not (' ' or '-' or '(' or ')'))
            {
                throw new FormatException("The phone number contains unsupported characters.");
            }
        }

        var normalized = digits.ToString();
        if (normalized.StartsWith("00", StringComparison.Ordinal))
        {
            if (hasPlus)
            {
                throw new FormatException("Use only one international phone prefix.");
            }
            normalized = normalized[2..];
        }

        if (normalized.Length == 8)
        {
            normalized = "357" + normalized;
        }

        if (normalized.Length is < 8 or > 15 || normalized[0] == '0')
        {
            throw new FormatException("The phone number must contain 8 to 15 international digits.");
        }

        return new PhoneNumber(normalized);
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out PhoneNumber? phone)
    {
        try
        {
            phone = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            phone = null;
            return false;
        }
    }

    public override string ToString() => WireValue;
}
