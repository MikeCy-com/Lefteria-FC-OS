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
public sealed record AdminAnnouncementPublishBody(long Version);
public sealed record AdminAppAnnouncementDto(string Id,string OrganizationId,string GroupId,
    string Title,string Body,DateTimeOffset PublishedAt,string Status,long Version);
public sealed record AdminAppAnnouncementsDto(string OrganizationId,
    IReadOnlyList<AdminAppAnnouncementDto> Announcements);
public sealed record AdminAppAnnouncementRemovedDto(string Id,bool Removed,long Version);

public sealed record AdminAppAnnouncementReaderDto(Guid Id,string Name);
public sealed record AdminAppAnnouncementReadersDto(string AnnouncementId,
    IReadOnlyList<AdminAppAnnouncementReaderDto> Readers);

public interface IAdminAnnouncementDraftStore
{
    Task<AdminAnnouncementDraftDto> CreateAsync(string cookie,string owner,
        AdminAnnouncementDraftCommand command,CancellationToken token);
    Task<AdminAnnouncementDraftsDto> ReadAsync(string cookie,string owner,
        CancellationToken token);
    Task<AdminAppAnnouncementDto> PublishAsync(string cookie,string owner,
        string id,long version,CancellationToken token);
    Task<AdminAppAnnouncementsDto> ReadPublishedAsync(string cookie,string owner,
        CancellationToken token);
    Task<AdminAppAnnouncementRemovedDto> RemovePublishedAsync(string cookie,string owner,
        string id,long version,CancellationToken token);
    Task<AdminAppAnnouncementReadersDto> ReadPublishedReadersAsync(string cookie,string owner,
        string id,CancellationToken token);
}
