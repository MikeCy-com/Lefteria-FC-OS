using System.Text.Json;
using LefteriaFc.Application.Messaging;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Messaging;

/// <summary>Consent and canonical roster access are enforced again by SQL
/// SECURITY DEFINER functions. The runtime has no raw private table access.</summary>
public sealed class PostgresMobilePrivateChatStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public Task<MobilePrivateContactDirectoryDto> ContactsAsync(
        MobileSession actor,string organization,string group,CancellationToken token)
    {
        ValidateRoom(organization,group);
        return QueryAsync<MobilePrivateContactDirectoryDto>("""
            SELECT club.read_mobile_private_contacts(
              @actor,@phone,@role,@expires,@organization,@group)::text
            """,actor,token,organization,group);
    }
    public Task<MobilePrivateInboxDto> InboxAsync(MobileSession actor,CancellationToken token)=>
        QueryAsync<MobilePrivateInboxDto>("""
            SELECT club.read_mobile_private_conversations(
              @actor,@phone,@role,@expires)::text
            """,actor,token);
    public Task<MobilePrivateConversationDto> InviteAsync(MobileSession actor,
        string? organization,string? group,Guid recipient,CancellationToken token)
    {
        ValidateRoom(organization,group);
        if(recipient==Guid.Empty)throw Invalid();
        return QueryAsync<MobilePrivateConversationDto>("""
            SELECT club.request_mobile_private_conversation(
              @actor,@phone,@role,@expires,@organization,@group,@recipient)::text
            """,actor,token,organization,group,recipient:recipient);
    }
    public Task<MobilePrivateConversationDto> RespondAsync(MobileSession actor,
        Guid conversation,bool? accept,CancellationToken token)
    {
        if(conversation==Guid.Empty||accept is null)throw Invalid();
        return QueryAsync<MobilePrivateConversationDto>("""
            SELECT club.respond_mobile_private_conversation(
              @actor,@phone,@role,@expires,@conversation,@accept)::text
            """,actor,token,conversation:conversation,accept:accept);
    }
    public Task<MobilePrivateMessagePageDto> MessagesAsync(MobileSession actor,
        Guid conversation,int offset,CancellationToken token)
    {
        if(conversation==Guid.Empty||offset is <0 or >100000)throw Invalid();
        return QueryAsync<MobilePrivateMessagePageDto>("""
            SELECT club.read_mobile_private_messages(
              @actor,@phone,@role,@expires,@conversation,@offset)::text
            """,actor,token,conversation:conversation,offset:offset);
    }
    public Task<MobilePrivateMessageDto> SendAsync(MobileSession actor,
        Guid conversation,string? content,CancellationToken token)=>
        WriteAsync(actor,conversation,"send",null,null,content,token);
    public Task<MobilePrivateMessageDto> EditAsync(MobileSession actor,
        Guid conversation,Guid message,long version,string? content,CancellationToken token)=>
        WriteAsync(actor,conversation,"edit",message,version,content,token);
    public Task<MobilePrivateMessageDto> DeleteAsync(MobileSession actor,
        Guid conversation,Guid message,long version,CancellationToken token)=>
        WriteAsync(actor,conversation,"delete",message,version,null,token);
    private Task<MobilePrivateMessageDto> WriteAsync(MobileSession actor,
        Guid conversation,string operation,Guid? message,long? version,
        string? content,CancellationToken token)
    {
        if(conversation==Guid.Empty ||
          (operation is "edit" or "delete" && (message is null || message==Guid.Empty
             || version is null || version<1)) ||
          (operation is "send" or "edit" && (content is null || content.Length is <1 or >10000
             || content!=content.Trim() || content.Any(char.IsControl))) ||
          (operation=="delete" && content is not null))throw Invalid();
        return QueryAsync<MobilePrivateMessageDto>("""
            SELECT club.write_mobile_private_message(
              @actor,@phone,@role,@expires,@conversation,@operation,@message,
              @version,@content)::text
            """,actor,token,conversation:conversation,
            operation:operation,message:message,version:version,content:content);
    }
    public Task<MobilePrivateHiddenDto> HideAsync(MobileSession actor,
        Guid conversation,CancellationToken token)
    {
        if(conversation==Guid.Empty)throw Invalid();
        return QueryAsync<MobilePrivateHiddenDto>("""
            SELECT club.hide_mobile_private_conversation(
              @actor,@phone,@role,@expires,@conversation)::text
            """,actor,token,conversation:conversation);
    }
    private static void ValidateRoom(string? organization,string? group)
    {
        if(organization is not ("lefteria_fc" or "lefteria_fc_academy")
          || string.IsNullOrEmpty(group)||group.Length>128||
          !group.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-'))throw Invalid();
    }
    private async Task<T> QueryAsync<T>(string sql,MobileSession actor,CancellationToken token,
        string? organization=null,string? group=null,Guid? recipient=null,
        Guid? conversation=null,bool? accept=null,int? offset=null,
        string? operation=null,Guid? message=null,long? version=null,string? content=null)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var connection=(NpgsqlConnection)database.Database.GetDbConnection();
                await using var command=new NpgsqlCommand(sql,connection);
                command.Parameters.AddWithValue("actor",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role",actor.Role.ToString().ToLowerInvariant());
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                if(organization is not null)command.Parameters.AddWithValue("organization",organization);
                if(group is not null)command.Parameters.AddWithValue("group",group);
                if(recipient is not null)command.Parameters.AddWithValue("recipient",recipient.Value);
                if(conversation is not null)command.Parameters.AddWithValue("conversation",conversation.Value);
                if(accept is not null)command.Parameters.AddWithValue("accept",accept.Value);
                if(offset is not null)command.Parameters.AddWithValue("offset",offset.Value);
                if(operation is not null)
                {
                    command.Parameters.AddWithValue("operation",operation);
                    command.Parameters.Add("message",NpgsqlDbType.Uuid).Value=(object?)message??DBNull.Value;
                    command.Parameters.Add("version",NpgsqlDbType.Bigint).Value=(object?)version??DBNull.Value;
                    command.Parameters.Add("content",NpgsqlDbType.Text).Value=(object?)content??DBNull.Value;
                }
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null?default:JsonSerializer.Deserialize<T>(raw,Json);
                return dto is null?throw Unavailable():dto;
            }
            finally{await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"The private invitation or conversation is unavailable."),
                "22023" or "23503" => Invalid(),
                "23505" or "40001" => new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Conversation changed; refresh and retry."),
                "55000" or "54000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Private chat needs roster synchronization."),
                _ => Unavailable()
            };
        }
    }
    private static MobileAuthException Invalid()=>new(
        MobileAuthFaultKind.Validation,MobileAuthErrorCode.InvalidInput,"Invalid private conversation request.");
    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Private messaging is unavailable.");
}
