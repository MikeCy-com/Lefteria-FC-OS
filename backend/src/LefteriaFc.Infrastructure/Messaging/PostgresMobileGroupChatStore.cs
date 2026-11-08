using System.Text.Json;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Messaging;

/// <summary>All protected rows are returned by PostgreSQL definer functions
/// which re-resolve the active identity and canonical roster for every call.
/// The restricted API role never receives direct chat-table privileges.</summary>
public sealed class PostgresMobileGroupChatStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<MobileChatPageDto> MessagesAsync(MobileSession actor,string organization,
        string group,int offset,CancellationToken token)
    {
        ValidateRoom(organization,group);
        if(offset is <0 or >100000)throw Invalid();
        return QueryAsync<MobileChatPageDto>("""
            SELECT club.read_mobile_chat_messages(@identity,@phone,@role,@expires,
              @organization,@group,@offset)::text
            """,actor,organization,group,offset,null,token);
    }

    public Task<MobileChatMessageDto> SendAsync(MobileSession actor,string organization,
        string group,string? content,CancellationToken token)
    {
        ValidateRoom(organization,group);
        if(content is null || content.Length is <1 or >10000 ||
           content!=content.Trim() || content.Any(char.IsControl))
            throw Invalid();
        return QueryAsync<MobileChatMessageDto>("""
            SELECT club.send_mobile_chat_message(@identity,@phone,@role,@expires,
              @organization,@group,@content)::text
            """,actor,organization,group,null,content,token);
    }

    public Task<MobileChatMessageDto> EditAsync(MobileSession actor,string organization,
        string group,Guid messageId,long version,string? content,CancellationToken token) =>
        MutateAsync(actor,organization,group,messageId,version,"edit",content,token);

    public Task<MobileChatMessageDto> DeleteAsync(MobileSession actor,string organization,
        string group,Guid messageId,long version,CancellationToken token) =>
        MutateAsync(actor,organization,group,messageId,version,"delete",null,token);

    private Task<MobileChatMessageDto> MutateAsync(MobileSession actor,string organization,
        string group,Guid messageId,long version,string operation,string? content,CancellationToken token)
    {
        ValidateRoom(organization,group);
        if(messageId==Guid.Empty || version<1 ||
          (operation=="edit" && (content is null || content.Length is <1 or >10000 ||
             content!=content.Trim() || content.Any(char.IsControl))) ||
          (operation=="delete" && content is not null))
            throw Invalid();
        return QueryAsync<MobileChatMessageDto>("""
            SELECT club.mutate_mobile_chat_message(
              @identity,@phone,@role,@expires,@organization,@group,
              @message,@version,@operation,@content)::text
            """,actor,organization,group,null,null,token,messageId,version,operation,content);
    }

    private static void ValidateRoom(string organization,string group)
    {
        if(organization is not ("lefteria_fc" or "lefteria_fc_academy") ||
           string.IsNullOrWhiteSpace(group) || group.Length>128 ||
           !group.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw Invalid();
    }

    private async Task<T> QueryAsync<T>(string sql,MobileSession actor,string? organization,
        string? group,int? offset,string? content,CancellationToken token,
        Guid? messageId=null,long? version=null,string? operation=null)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var connection=(NpgsqlConnection)database.Database.GetDbConnection();
                await using var command=new NpgsqlCommand(sql,connection);
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",actor.Role.ToString().ToLowerInvariant());
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                if(organization is not null)command.Parameters.AddWithValue("organization",organization);
                if(group is not null)command.Parameters.AddWithValue("group",group);
                if(offset is not null)command.Parameters.AddWithValue("offset",offset.Value);
                if(operation is not null)
                {
                    command.Parameters.AddWithValue("message",messageId!.Value);
                    command.Parameters.AddWithValue("version",version!.Value);
                    command.Parameters.AddWithValue("operation",operation);
                    command.Parameters.Add("content",NpgsqlTypes.NpgsqlDbType.Text).Value=
                        (object?)content??DBNull.Value;
                }
                else if(content is not null)command.Parameters.AddWithValue("content",content);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null ? default : JsonSerializer.Deserialize<T>(raw,Json);
                return dto is null?throw Unavailable():dto;
            }
            finally { await database.Database.CloseConnectionAsync(); }
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"You no longer have access to this group."),
                "22023" or "23503" => Invalid(),
                "40001" => new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Message changed; refresh and retry."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Group messaging is awaiting roster synchronization."),
                _ => Unavailable()
            };
        }
    }

    private static MobileAuthException Invalid() => new(
        MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,"Invalid group or message.");
    private static MobileAuthException Unavailable() => new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Group messaging is unavailable.");
}
