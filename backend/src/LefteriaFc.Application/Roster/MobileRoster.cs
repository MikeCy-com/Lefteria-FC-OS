using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.Roster;

public sealed record MobilePlayerOptionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("image_url")] string? ImageUrl);
public sealed record MobilePlayersListResult([property: JsonPropertyName("players")] IReadOnlyList<MobilePlayerOptionDto> Players);
public sealed record RosterPickerSnapshot(bool IdentityAvailable, bool Ready, IReadOnlyList<MobilePlayerOptionDto> Players);

public interface IRosterQueries
{
    // Rejoins current identity/admin and readiness in the same DB statement as
    // player selection. Neither favourite IDs nor browser selectors are grants.
    Task<RosterPickerSnapshot> GetFirstTeamPickerAsync(MobileSession session, CancellationToken cancellationToken);
}

public sealed class MobileRosterService(MobileAuthService identity, IRosterQueries roster)
{
    public async Task<MobilePlayersListResult> GetPlayersAsync(MobileSession session, CancellationToken cancellationToken)
    {
        try
        {
            await identity.AuthorizeCurrentIdentityAsync(session, cancellationToken);
            var snapshot = await roster.GetFirstTeamPickerAsync(session, cancellationToken);
            if (!snapshot.IdentityAvailable)
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden, MobileAuthErrorCode.AccountUnavailable, "The current account is unavailable.");
            if (!snapshot.Ready) throw Unavailable();
            return new(snapshot.Players);
        }
        catch (DbException) { throw Unavailable(); }
    }
    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.RosterUnavailable, "The roster is not available yet.");
}
