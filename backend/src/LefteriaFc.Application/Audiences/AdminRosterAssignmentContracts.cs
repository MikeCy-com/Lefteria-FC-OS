namespace LefteriaFc.Application.Audiences;

// Expected membership is an optimistic concurrency token. The response reflects
// committed canonical membership, not a client-side preview.
public sealed record AdminRosterAssignmentCommand(
    string? PlayerId, string[]? ExpectedGroupIds, string[]? GroupIds);

public sealed record AdminRosterAssignmentDto(
    string OrganizationId, string PlayerId, string? TeamId,
    IReadOnlyList<string> AcademyGroupIds);

public interface IAdminRosterAssignmentWriter
{
    Task<AdminRosterAssignmentDto> SaveAsync(
        string cookie, string owner, AdminRosterAssignmentCommand command,
        CancellationToken cancellationToken);
}
