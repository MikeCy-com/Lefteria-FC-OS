namespace LefteriaFc.Application.Messaging;

// Isolated from public site, roster/phone search and team group chat.
// Player and fan identities never become private chat participants.
public sealed record MobilePrivateContactDto(Guid Id,string Name,string Role);
public sealed record MobilePrivateContactDirectoryDto(IReadOnlyList<MobilePrivateContactDto> Contacts);
public sealed record MobilePrivateConversationDto(Guid Id,string OrganizationId,string GroupId,
    Guid PartnerId,string PartnerName,string PartnerRole,string Status,bool IsInitiator);
public sealed record MobilePrivateInboxDto(IReadOnlyList<MobilePrivateConversationDto> Conversations);
public sealed record MobilePrivateInviteBody(string? OrganizationId,string? GroupId,Guid RecipientId);
public sealed record MobilePrivateRespondBody(bool? Accept);
public sealed record MobilePrivateMessageDto(Guid Id,Guid ConversationId,
    Guid SenderId,string SenderName,string Content,DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,bool IsDeleted,long Version);
public sealed record MobilePrivateMessagePageDto(IReadOnlyList<MobilePrivateMessageDto> Messages,int? NextOffset);
public sealed record MobilePrivateSendBody(string? Content);
public sealed record MobilePrivateEditBody(string? Content,long Version);
public sealed record MobilePrivateHiddenDto(Guid ConversationId,bool Hidden);
