using System.Data.Common;
using System.Text.Json.Serialization;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;

namespace LefteriaFc.Application.Family;

// Only current canonical guardian grants can produce a child in this projection.
// Deliberately omit personal phone, DOB, financial and medical details.
public sealed record FamilyChildDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("organization_id")] string OrganizationId,
    [property: JsonPropertyName("team_type")] string TeamType,
    [property: JsonPropertyName("team_id")] string? TeamId,
    [property: JsonPropertyName("academy_group_ids")] IReadOnlyList<string> AcademyGroupIds);

public sealed record ParentFamilyResult(
    [property: JsonPropertyName("children")] IReadOnlyList<FamilyChildDto> Children);

public sealed record ParentFamilySnapshot(
    bool IdentityAvailable, bool RosterReady, IReadOnlyList<FamilyChildDto> Children);

public interface IParentFamilyQueries
{
    Task<ParentFamilySnapshot> GetLinkedChildrenAsync(MobileSession session, CancellationToken cancellationToken);
}

public sealed class ParentFamilyService(MobileAuthService identity, IParentFamilyQueries family)
{
    public async Task<ParentFamilyResult> GetAsync(MobileSession session, CancellationToken cancellationToken)
    {
        if (session.Role != MobileRole.Parent)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable, "Family settings require the active parent role.");
        try
        {
            // This checks current session/identity authority; the final SQL
            // independently rechecks canonical binding in one DB snapshot.
            await identity.AuthorizeCurrentIdentityAsync(session, cancellationToken);
            var snapshot = await family.GetLinkedChildrenAsync(session, cancellationToken);
            if (!snapshot.IdentityAvailable)
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.AccountUnavailable, "The current parent account is unavailable.");
            if (!snapshot.RosterReady)
                throw Unavailable();
            return new(snapshot.Children);
        }
        catch (DbException) { throw Unavailable(); }
    }

    private static MobileAuthException Unavailable() => new(MobileAuthFaultKind.ServiceUnavailable,
        MobileAuthErrorCode.RosterUnavailable, "Family information is temporarily unavailable.");
}
