namespace LefteriaFc.Application.Audiences;

// Manager-only, allowlisted roster assignment projection. No age, birth date,
// phones, parent identities, health notes, source archive or private messages.
public sealed record AdminRosterPlayerDto(string Id,string Name,string? TeamId,
    IReadOnlyList<string> AcademyGroupIds);
public sealed record AdminRosterPlayersPageDto(string OrganizationId,bool Ready,
    int Offset,int Limit,int Total,IReadOnlyList<AdminRosterPlayerDto> Players);
public interface IAdminRosterPlayerDirectory
{
    Task<AdminRosterPlayersPageDto> ReadAsync(
        string cookie,string organizationId,int offset,int limit,CancellationToken token);
}
