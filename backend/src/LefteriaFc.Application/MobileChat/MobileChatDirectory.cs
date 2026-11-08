using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.MobileChat;

public sealed record MobileChatGroupDto(
    [property:JsonPropertyName("id")]string Id,
    [property:JsonPropertyName("name")]string Name,
    [property:JsonPropertyName("kind")]string Kind,
    [property:JsonPropertyName("organization_id")]string OrganizationId);
public sealed record MobileChatDirectoryDto(
    [property:JsonPropertyName("ready")]bool Ready,
    [property:JsonPropertyName("groups")]IReadOnlyList<MobileChatGroupDto> Groups);
public sealed record MobileChatGroupSnapshot(bool IdentityAvailable,bool RosterReady,
    IReadOnlyList<MobileChatGroupDto> Groups);
public interface IMobileChatGroupReader
{
    // Current canonical identity, guardian/staff relationships and grants must
    // be rejoined in the same database operation as the groups returned.
    Task<MobileChatGroupSnapshot> ReadAsync(MobileSession session,CancellationToken token);
}
public sealed class MobileChatDirectoryService(
    MobileAuthService identity, IMobileChatGroupReader reader)
{
    public async Task<MobileChatDirectoryDto> ReadAsync(MobileSession session,CancellationToken token)
    {
        try
        {
            await identity.AuthorizeCurrentIdentityAsync(session,token);
            if(session.Role is MobileRole.Fan)
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.AccountUnavailable,
                    "Private group conversations are not available for fans.");
            var snapshot=await reader.ReadAsync(session,token);
            if(!snapshot.IdentityAvailable)
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.AccountUnavailable,
                    "The current group relationship is not available.");
            if(!snapshot.RosterReady)throw Unavailable();
            return new(true,snapshot.Groups);
        }
        catch(DbException){throw Unavailable();}
    }
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.RosterUnavailable,
        "The current group directory is unavailable.");
}
