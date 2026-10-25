using System.Text.Json.Serialization;

namespace LefteriaFc.Application.MobileAuth;

// The HTTP boundary must preserve JSON-property presence, not just its nullable value.
public readonly record struct RememberDeviceSelection(bool WasProvided, int? Days);

public sealed record MobileAuthRequestContext(string ClientKey, string Platform);
public sealed record MobileOtpVerificationCommand(string? Phone, string? Code, string? Role,
    string? RequestedRole, RememberDeviceSelection RememberDevice);
public sealed record FanRegistrationCommand(string? Phone, string? FirstName, string? LastName,
    string? Name, string? Birthday);

public sealed record OtpRequestResult(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("role_detected")] string? RoleDetected,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record FanRegistrationResult(
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("role_detected")] string RoleDetected)
{
    // Compatibility field: a successful response always means real delivery.
    [JsonPropertyName("simulated")]
    public bool Simulated => false;
}

[JsonDerivedType(typeof(MobileRolePickerResult))]
[JsonDerivedType(typeof(MobileLoginResult))]
public abstract record MobileVerificationResult;

public sealed record MobileIdentityOption(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("name")] string Name);

public sealed record MobileRolePickerResult(
    [property: JsonPropertyName("identities")] IReadOnlyList<MobileIdentityOption> Identities,
    [property: JsonPropertyName("phone")] string Phone) : MobileVerificationResult
{
    [JsonPropertyName("multi_role")]
    public bool MultiRole => true;
}

public sealed record MobileLoginResult(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user")] MobileUserDto User,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("available_roles")] IReadOnlyList<string> AvailableRoles)
    : MobileVerificationResult;

public sealed record MobileRoleSwitchResult
{
    [JsonPropertyName("token"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Token { get; init; }
    [JsonPropertyName("role")]
    public required string Role { get; init; }
    [JsonPropertyName("noop"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Noop { get; init; }
    [JsonPropertyName("user"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileUserDto? User { get; init; }
    [JsonPropertyName("available_roles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AvailableRoles { get; init; }
}

public sealed record MobileUserDto
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }
    [JsonPropertyName("phone")]
    public required string Phone { get; init; }
    [JsonPropertyName("role")]
    public required string Role { get; init; }
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    [JsonPropertyName("linked_player_id")]
    public string? LinkedPlayerId { get; init; }
    [JsonPropertyName("linked_player_ids")]
    public IReadOnlyList<string> LinkedPlayerIds { get; init; } = [];
    [JsonPropertyName("team_id")]
    public string? TeamId { get; init; }
    [JsonPropertyName("academy_group_ids")]
    public IReadOnlyList<string> AcademyGroupIds { get; init; } = [];
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }
    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("last_login")]
    public required DateTimeOffset LastLogin { get; init; }
    [JsonPropertyName("available_roles")]
    public IReadOnlyList<string> AvailableRoles { get; init; } = [];
    [JsonPropertyName("current_role_available")]
    public required bool CurrentRoleAvailable { get; init; }
    [JsonPropertyName("favorite_team"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FavoriteTeam { get; init; }
    [JsonPropertyName("first_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }
    [JsonPropertyName("last_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }
    [JsonPropertyName("birthday"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Birthday { get; init; }
    [JsonPropertyName("favourite_player_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FavouritePlayerId { get; init; }
    [JsonPropertyName("notification_prefs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, bool>? NotificationPreferences { get; init; }
}

public sealed record OrganizationOptionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record MobileOrganizationContextDto(
    [property: JsonPropertyName("organization_id")] string? OrganizationId,
    [property: JsonPropertyName("organization_name")] string? OrganizationName,
    [property: JsonPropertyName("available_organizations")] IReadOnlyList<OrganizationOptionDto> AvailableOrganizations,
    [property: JsonPropertyName("requires_organization_selection")] bool RequiresOrganizationSelection);
