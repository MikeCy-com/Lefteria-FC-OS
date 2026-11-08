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

 // One durable event per published notice. Mobile inbox is not a device push.
public sealed record MobileAppNotificationDto(string AnnouncementId,string OrganizationId,
    string GroupId,string GroupName,string Title,DateTimeOffset PublishedAt,bool Seen);
public sealed record MobileAppNotificationInboxDto(
    IReadOnlyList<MobileAppNotificationDto> Notifications,int UnreadCount,
    int? NextOffset,bool Enabled);
