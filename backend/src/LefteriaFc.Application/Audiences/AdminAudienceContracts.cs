namespace LefteriaFc.Application.Audiences;

// No player names, birth dates, contact details or imported source JSON.
// The directory is the canonical target selector for future announcements;
// it does NOT itself authorize sending or grant access to private team chats.
public sealed record AdminAudienceGroupDto(
    string Id, string Name, string Kind, int ActivePlayers);
public sealed record AdminAudienceDirectoryDto(
    string OrganizationId, bool Ready, int UnassignedPlayers,
    IReadOnlyList<AdminAudienceGroupDto> Groups);
public interface IAdminAudienceDirectory
{
    Task<AdminAudienceDirectoryDto> ReadAsync(
        string adminCookie, string organizationId, CancellationToken cancellationToken);
}
