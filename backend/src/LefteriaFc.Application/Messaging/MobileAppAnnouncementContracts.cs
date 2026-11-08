namespace LefteriaFc.Application.Messaging;

// Separate app notices are sourced only from admin-published roster groups.
// No draft, raw contacts, personal readers or announcement attachments leak.
public sealed record AppAnnouncementMediaDto(Guid Id,string Kind,string FileName,
    string ContentType,int Size);
public sealed record AppAnnouncementMediaDownloadDto(Guid Id,string Kind,string FileName,
    string ContentType,int Size,string Base64);
public sealed record MobileAppAnnouncementDto(string Id,string OrganizationId,
    string GroupId,string Title,string Body,DateTimeOffset PublishedAt,string Status,
    IReadOnlyList<MobileChatReactionDto>? Reactions=null,
    IReadOnlyList<AppAnnouncementMediaDto>? Media=null);
public sealed record MobileAppAnnouncementPageDto(
    IReadOnlyList<MobileAppAnnouncementDto> Announcements,int? NextOffset);
public sealed record MobileAppAnnouncementReactionSnapshotDto(
    string AnnouncementId,IReadOnlyList<MobileChatReactionDto> Reactions);
public sealed record MobileAppAnnouncementSeenAckDto(string AnnouncementId,bool Seen);
