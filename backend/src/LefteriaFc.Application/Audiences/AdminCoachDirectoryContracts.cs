namespace LefteriaFc.Application.Audiences;

// Scoped staff assignment projection. No phone, guardian, DOB, contact
// provenance, credentials, or player identities are exposed.
public sealed record AdminCoachDto(string Id,string Name,string JobRole,
    IReadOnlyList<string> GroupIds);
public sealed record AdminCoachPageDto(string OrganizationId,bool Ready,
    int Offset,int Limit,int Total,IReadOnlyList<AdminCoachDto> Coaches);
public interface IAdminCoachDirectory
{
    Task<AdminCoachPageDto> ReadAsync(string cookie,string owner,int offset,
        int limit,CancellationToken token);
}
