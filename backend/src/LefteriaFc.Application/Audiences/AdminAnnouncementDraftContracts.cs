namespace LefteriaFc.Application.Audiences;

// Drafts never enumerate player/guardian contacts or dispatch to anyone.
// Target group remains a canonical ID, resolved again at eventual publish.
public sealed record AdminAnnouncementDraftCommand(
    string? GroupId,string? Title,string? Body);
public sealed record AdminAnnouncementDraftDto(
    string Id,string OrganizationId,string GroupId,string Title,string Body,
    string Status,long Version);
public sealed record AdminAnnouncementDraftsDto(
    string OrganizationId,IReadOnlyList<AdminAnnouncementDraftDto> Drafts);
public interface IAdminAnnouncementDraftStore
{
    Task<AdminAnnouncementDraftDto> CreateAsync(string cookie,string owner,
        AdminAnnouncementDraftCommand command,CancellationToken token);
    Task<AdminAnnouncementDraftsDto> ReadAsync(string cookie,string owner,
        CancellationToken token);
}
