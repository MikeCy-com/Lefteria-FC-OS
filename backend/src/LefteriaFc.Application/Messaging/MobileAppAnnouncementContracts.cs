namespace LefteriaFc.Application.Messaging;

// Separate app notices are sourced only from admin-published roster groups.
// No draft, raw contacts, personal readers or announcement attachments leak.
public sealed record MobileAppAnnouncementDto(string Id,string OrganizationId,
    string GroupId,string Title,string Body,DateTimeOffset PublishedAt,string Status,
    IReadOnlyList<MobileChatReactionDto>? Reactions=null);
public sealed record MobileAppAnnouncementPageDto(
    IReadOnlyList<MobileAppAnnouncementDto> Announcements,int? NextOffset);
public sealed record MobileAppAnnouncementReactionSnapshotDto(
    string AnnouncementId,IReadOnlyList<MobileChatReactionDto> Reactions);
