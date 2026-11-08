using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>Calls only the exact audited SQL membership writer, never raw
/// club table UPDATE or INSERT from an admin-auth connection.</summary>
public sealed class PostgresAdminRosterAssignmentWriter(
    WebAdminConnections connections, IWebAdminSecrets secrets)
    : IAdminRosterAssignmentWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminRosterAssignmentDto> SaveAsync(
        string cookie, string owner, AdminRosterAssignmentCommand request,
        CancellationToken token)
    {
        if (!secrets.IsToken(cookie))
            throw new WebAdminException(401, "not_authenticated", "Sign in again.");
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400, "invalid_organization", "Select one organization.");

        static bool ValidIds(string[]? ids) =>
            ids is { Length: <= 20 } &&
            ids.All(id => id is { Length: >= 1 and <= 128 } &&
                id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) &&
            ids.Distinct(StringComparer.Ordinal).Count() == ids.Length;

        if (request.PlayerId is not { Length: >= 1 and <= 128 } ||
            !request.PlayerId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            !ValidIds(request.ExpectedGroupIds) || !ValidIds(request.GroupIds) ||
            (owner == "lefteria_fc" && (request.ExpectedGroupIds!.Length > 1 ||
                                        request.GroupIds!.Length > 1)))
            throw new WebAdminException(422, "invalid_assignment", "Invalid player or groups.");

        try
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                SELECT club.write_admin_roster_assignment(
                    @digest,@owner,@player,@expected,@desired)::text
                """, connection);
            command.Parameters.AddWithValue("digest", secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("player", request.PlayerId);
            command.Parameters.Add("expected", NpgsqlDbType.Jsonb).Value =
                JsonSerializer.Serialize(request.ExpectedGroupIds);
            command.Parameters.Add("desired", NpgsqlDbType.Jsonb).Value =
                JsonSerializer.Serialize(request.GroupIds);
            var raw = await command.ExecuteScalarAsync(token) as string;
            var saved = raw is null ? null :
                JsonSerializer.Deserialize<AdminRosterAssignmentDto>(raw, Json);
            if (saved is null || saved.OrganizationId != owner ||
                saved.PlayerId != request.PlayerId || saved.AcademyGroupIds is null ||
                saved.AcademyGroupIds.Count > 20 ||
                (owner == "lefteria_fc" &&
                    (saved.AcademyGroupIds.Count != 0 ||
                     saved.TeamId != request.GroupIds!.SingleOrDefault())) ||
                (owner == "lefteria_fc_academy" &&
                    (saved.TeamId is not null ||
                     !saved.AcademyGroupIds.Order(StringComparer.Ordinal)
                         .SequenceEqual(request.GroupIds!.Order(StringComparer.Ordinal)))))
                throw new WebAdminException(503, "assignment_unavailable",
                    "Saved assignment could not be verified.");
            return saved;
        }
        catch (PostgresException e) when (e.SqlState == "P0001" &&
            e.MessageText == "admin_club_session_lock_missing")
        { throw new WebAdminException(401, "not_authenticated", "Sign in again."); }
        catch (PostgresException e) when (e.SqlState == "P0001" &&
            e.MessageText == "admin_club_grant_lock_missing")
        { throw new WebAdminException(403, "not_authorized", "Organization access changed."); }
        catch (PostgresException e) when (e.SqlState == "42501")
        { throw new WebAdminException(403, "not_authorized", "Roster write denied."); }
        catch (PostgresException e) when (e.SqlState == "40001")
        { throw new WebAdminException(409, "assignment_conflict", "Membership changed. Refresh and retry."); }
        catch (PostgresException e) when (e.SqlState == "22023" || e.SqlState == "23503")
        { throw new WebAdminException(422, "invalid_assignment", "Invalid player or groups."); }
        catch (PostgresException e) when (e.SqlState is "55000" or "57014" or "55P03" or "P0001")
        { throw new WebAdminException(503, "assignment_unavailable", "Roster assignment is unavailable."); }
    }
}
