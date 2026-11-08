using System.Text.Json;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

/// <summary>One privileged, audited SQL call; no raw club table mutations
/// are authorized to the web-admin runtime connection.</summary>
public sealed class PostgresAdminRosterGroupWriter(
    WebAdminConnections connections, IWebAdminSecrets secrets) : IAdminRosterGroupWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AdminAudienceGroupDto> CreateAsync(
        string adminCookie,string owner,AdminCreateGroupCommand request,CancellationToken token)
    {
        if (!secrets.IsToken(adminCookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if (owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select one organization.");
        var name=request.Name;
        if (name is not {Length: >= 1 and <= 120} ||
            name!=name.Trim() || name.Any(char.IsControl))
            throw new WebAdminException(422,"invalid_group","Provide a valid group name.");

        var id="grp_"+Guid.NewGuid().ToString("N");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.create_admin_group(@digest,@owner,@id,@name)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(adminCookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("id",id);
            command.Parameters.AddWithValue("name",name);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var saved=raw is null ? null : JsonSerializer.Deserialize<AdminAudienceGroupDto>(raw,Json);
            if(saved is null || saved.Id!=id || saved.Name!=name ||
                saved.Kind!=(owner=="lefteria_fc"?"team":"academy_group") ||
                saved.ActivePlayers!=0)
                throw new WebAdminException(503,"group_unavailable","New group could not be verified.");
            return saved;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_session_lock_missing")
        { throw new WebAdminException(401,"not_authenticated","Sign in again."); }
        catch(PostgresException e) when(e.SqlState=="P0001" &&
            e.MessageText=="admin_club_grant_lock_missing")
        { throw new WebAdminException(403,"not_authorized","Organization grant was revoked."); }
        catch(PostgresException e) when(e.SqlState=="42501")
        { throw new WebAdminException(403,"not_authorized","Group creation is not authorized."); }
        catch(PostgresException e) when(e.SqlState=="22023")
        { throw new WebAdminException(422,"invalid_group","Invalid team or Academy group."); }
        catch(PostgresException e) when(e.SqlState=="23505")
        { throw new WebAdminException(409,"group_conflict","Group identifier conflict; retry safely."); }
        catch(PostgresException e) when(e.SqlState is "55000" or "54000" or "57014" or "55P03" or "P0001")
        { throw new WebAdminException(503,"group_unavailable","Group creation is unavailable."); }
    }
}
