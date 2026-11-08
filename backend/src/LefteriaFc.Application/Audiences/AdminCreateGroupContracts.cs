namespace LefteriaFc.Application.Audiences;

/// <summary>Manager-only creation of one canonical group, not a chat room
/// or an announcement target grant. The server generates the unique ID.</summary>
public sealed record AdminCreateGroupCommand(string? Name);
public interface IAdminRosterGroupWriter
{
    Task<AdminAudienceGroupDto> CreateAsync(string adminCookie,string owner,
        AdminCreateGroupCommand request,CancellationToken cancellationToken);
}
