namespace LefteriaFc.Application.Messaging;

// Only active roster-backed team/Academy group rooms. Never use these DTOs
// for a private one-to-one conversation or public website announcement.
public sealed record MobileChatReactionDto(string Emoji,int Count,bool Mine);
public sealed record MobileChatReactionBody(string? Emoji,bool? Active);
public sealed record MobileChatReactionSnapshotDto(Guid MessageId,IReadOnlyList<MobileChatReactionDto> Reactions);

public sealed record MobileChatMessageDto(Guid Id,string OrganizationId,string GroupId,
    Guid SenderId,string SenderName,string Content,DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt=null,bool IsDeleted=false,long Version=1,
    IReadOnlyList<MobileChatReactionDto>? Reactions=null);
public sealed record MobileChatPageDto(IReadOnlyList<MobileChatMessageDto> Messages,int? NextOffset);
public sealed record MobileChatSendBody(string? Content);
public sealed record MobileChatEditBody(string? Content,long Version);
