using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;
using LefteriaFc.Application.WebAdmin;

namespace LefteriaFc.Application.PublicContent;

public sealed record AdminClubProfileDto([property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version, PublicClub Club);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminClubUpdateCommand(
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] long ExpectedVersion,
    [property: JsonRequired] AdminClubEditable Club);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminClubEditable(string Name, string? GreekName,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? Founded, string? Description,
    string? Motto, string? History, string? LogoUrl, string? StadiumImageUrl, string? City,
    string? Country, string? Address, string? Email, string? Phone, string? Website,
    IReadOnlyDictionary<string, string> SocialLinks)
{
    public PublicClub ToPublic(string id, string organization) => new(id, organization, Name, GreekName, Founded,
        Description, Motto, History, LogoUrl, StadiumImageUrl, City, Country, Address, Email, Phone, Website, SocialLinks);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Text(Name, 200) || !Text(GreekName, 200) ||
            Founded is < 1800 or > 2200 || !Text(Description, 20000) || !Text(Motto, 1000) || !Text(History, 100000) ||
            !Image(LogoUrl) || !Image(StadiumImageUrl) || !Text(City, 200) || !Text(Country, 100) || !Text(Address, 1000) ||
            !Text(Email, 254) || Email is { Length: > 0 } && !Regex.IsMatch(Email, "^[^\\s@<>:]+@[^\\s@<>:]+\\.[^\\s@<>:]+$", RegexOptions.CultureInvariant) ||
            !Text(Phone, 40) || Phone is { Length: > 0 } && !Regex.IsMatch(Phone, "^[+0-9 ()-]{5,40}$", RegexOptions.CultureInvariant) ||
            !External(Website) || SocialLinks is null || SocialLinks.Count > 5 || SocialLinks.Any(pair =>
                pair.Key is not ("facebook" or "instagram" or "twitter" or "youtube" or "tiktok") || string.IsNullOrEmpty(pair.Value) || !External(pair.Value)))
            throw Invalid();
    }
    private static bool Text(string? value, int maximum) => value is null || value.Length <= maximum && !value.Contains('\0');
    private static bool Image(string? value) => string.IsNullOrEmpty(value) || RosterImportValidator.PublicImageUrl(value) &&
        (value.StartsWith('/') || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    private static bool External(string? value) => string.IsNullOrEmpty(value) || Text(value, 2048) && !value.Any(char.IsControl) && value == value.Trim() &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    public static WebAdminException Invalid() => new(422, "invalid_club_profile", "Check the public club profile fields.");
}

public interface IAdminClubRepository
{
    Task<AdminClubProfileDto> ReadAsync(string token, string organization, CancellationToken cancellationToken);
    Task<AdminClubProfileDto> UpdateAsync(string token, string organization, AdminClubUpdateCommand command, CancellationToken cancellationToken);
}
