namespace LefteriaFc.Domain.Identity;

public enum MobileRole
{
    Parent,
    Coach,
    Management,
    Player,
    Fan
}

public static class MobileRoles
{
    public static bool TryParse(string? value, out MobileRole role)
    {
        var parsed = value switch
        {
            "parent" => MobileRole.Parent,
            "coach" => MobileRole.Coach,
            "management" => MobileRole.Management,
            "player" => MobileRole.Player,
            "fan" => MobileRole.Fan,
            _ => (MobileRole?)null
        };
        role = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    public static string ToWireValue(this MobileRole role) => role switch
    {
        MobileRole.Parent => "parent",
        MobileRole.Coach => "coach",
        MobileRole.Management => "management",
        MobileRole.Player => "player",
        MobileRole.Fan => "fan",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
