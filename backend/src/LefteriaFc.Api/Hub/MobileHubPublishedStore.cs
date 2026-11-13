using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record MobileHubArticle(
    Guid Id,string OrganizationId,string Title,string Body,
    DateTimeOffset PublishedAt,string Audience,short? AgeMin,short? AgeMax);
public sealed record MobileHubPublishedResult(
    string OrganizationId,string Audience,IReadOnlyList<MobileHubArticle> Items);

/// <summary>
/// No direct table access: the PostgreSQL SECURITY DEFINER reader validates
/// the currently active identity, scope, review state and audience per call.
/// </summary>
public sealed class MobileHubPublishedStore(LefteriaDbContext database)
{
    public async Task<MobileHubPublishedResult> ReadAsync(
        MobileSession actor,string organization,int offset,CancellationToken token)
    {
        if(actor.Role is not (MobileRole.Parent or MobileRole.Player) ||
           organization is not ("lefteria_fc" or "lefteria_fc_academy") ||
           offset is <0 or >1000)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Education audience is not available.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT hub.read_mobile_published_education(
                      @identity,@phone,@role,@expires,@owner,@offset)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",
                    actor.Role==MobileRole.Parent?"parent":"player");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",organization);
                command.Parameters.AddWithValue("offset",offset);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var result=raw is null?null:JsonSerializer.Deserialize<MobileHubPublishedResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                var role=actor.Role==MobileRole.Parent?"parent":"player";
                if(result is null || result.OrganizationId!=organization ||
                   result.Audience!=role || result.Items is null ||
                   result.Items.Count>20 ||
                   result.Items.Any(item=>item is null || item.Id==Guid.Empty ||
                       item.OrganizationId!=organization || item.Audience!=role ||
                       string.IsNullOrWhiteSpace(item.Title) || item.Title.Length>180 ||
                       string.IsNullOrWhiteSpace(item.Body) || item.Body.Length>20000 ||
                       item.PublishedAt==default || item.PublishedAt>DateTimeOffset.UtcNow ||
                       (item.AgeMin is null)!=(item.AgeMax is null) ||
                       (item.AgeMin is not null &&
                        (item.AgeMin<4 || item.AgeMax>99 || item.AgeMax<item.AgeMin)) ||
                       (role=="player" && (item.AgeMin is not null || item.AgeMax is not null))))
                    throw Unavailable();
                return result;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Education access is no longer authorized.");
            throw Unavailable();
        }
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Published education is temporarily unavailable.");
}
