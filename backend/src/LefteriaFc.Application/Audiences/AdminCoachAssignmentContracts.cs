namespace LefteriaFc.Application.Audiences;

public sealed record AdminCoachAssignmentCommand(
    string? StaffId,string[]? ExpectedGroupIds,string[]? GroupIds);
public sealed record AdminCoachAssignmentDto(
    string OrganizationId,string StaffId,IReadOnlyList<string> GroupIds);
public interface IAdminCoachAssignmentWriter
{
    Task<AdminCoachAssignmentDto> SaveAsync(
        string cookie,string owner,AdminCoachAssignmentCommand request,CancellationToken token);
}
