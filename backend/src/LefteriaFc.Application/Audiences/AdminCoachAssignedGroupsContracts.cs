namespace LefteriaFc.Application.Audiences;

// No player counts, player identities, coaching private data or location
// grants. A coach may only see canonical names of explicitly linked groups.
public sealed record AdminCoachAssignedGroupDto(string Id,string Name,string Kind);
public sealed record AdminCoachAssignedGroupsDto(
    string OrganizationId,bool Ready,IReadOnlyList<AdminCoachAssignedGroupDto> Groups);
public interface IAdminCoachAssignedGroupsReader
{
    Task<AdminCoachAssignedGroupsDto> ReadAsync(
        string adminCookie,string organizationId,CancellationToken cancellationToken);
}
