namespace LefteriaFc.Application.Messaging;

// Only active roster-backed team/Academy group rooms. Never use these DTOs
// for a private one-to-one conversation or public website announcement.
public sealed record MobileChatMessageDto(Guid Id,string OrganizationId,string GroupId,
    Guid SenderId,string SenderName,string Content,DateTimeOffset CreatedAt);
public sealed record MobileChatPageDto(IReadOnlyList<MobileChatMessageDto> Messages,int? NextOffset);
public sealed record MobileChatSendBody(string? Content);
