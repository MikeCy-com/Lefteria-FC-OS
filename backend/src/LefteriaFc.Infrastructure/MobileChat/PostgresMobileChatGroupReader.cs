using System.Text.Json;
using LefteriaFc.Application.MobileChat;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.MobileChat;

public sealed class PostgresMobileChatGroupReader(LefteriaDbContext database)
    :IMobileChatGroupReader
{
    private sealed record Payload(bool IdentityAvailable,bool RosterReady,MobileChatGroupDto[] Groups);
    public async Task<MobileChatGroupSnapshot> ReadAsync(MobileSession session,CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            // SQL definer exposes only verified group name/kind/owner; runtime
            // cannot read private roster contacts or direct staff relationships.
            await using var command=new NpgsqlCommand("""
                SELECT club.read_mobile_chat_groups(
                  @identity,@phone,@role,@expires)::text
                """,(NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity",session.IdentityId);
            command.Parameters.AddWithValue("phone",session.NormalizedPhone);
            command.Parameters.AddWithValue("role",session.Role.ToWireValue());
            command.Parameters.AddWithValue("expires",session.ExpiresAt);
            string? raw;
            try{raw=await command.ExecuteScalarAsync(token) as string;}
            catch(PostgresException error) when(error.SqlState=="42501")
            {return new(false,false,[]);}
            catch(PostgresException error) when(error.SqlState=="55000")
            {return new(true,false,[]);}

            var payload=raw is null?null:JsonSerializer.Deserialize<Payload>(raw,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
                    UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
                });
            if(payload is null || payload.Groups is null || payload.Groups.Length>150 ||
                payload.Groups.Any(g=>g.Id is null or {Length:0 or >128} ||
                    g.Name is null or {Length:0 or >200} ||
                    g.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
                    (g.Kind=="team" && g.OrganizationId!="lefteria_fc") ||
                    (g.Kind=="academy_group" && g.OrganizationId!="lefteria_fc_academy") ||
                    g.Kind is not ("team" or "academy_group")))
                throw new InvalidOperationException("Invalid scoped chat group projection.");
            return new(payload.IdentityAvailable,payload.RosterReady,payload.Groups);
        }
        finally{await database.Database.CloseConnectionAsync();}
    }
}
