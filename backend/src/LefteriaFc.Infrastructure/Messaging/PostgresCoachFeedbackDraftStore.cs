using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Messaging;

/// <summary>Stores a coach-authored, unpublished feedback brief with a
/// fresh server-side coach/player/team membership check in PostgreSQL.</summary>
public sealed class PostgresCoachFeedbackDraftStore(LefteriaDbContext database)
{
    public async Task<CoachFeedbackTargetsResult> TargetsAsync(
        MobileSession actor,CancellationToken token)
    {
        if(actor.Role!=MobileRole.Coach)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Coach role required.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT club.read_mobile_coach_feedback_targets(
                      @identity,@phone,@role,@expires)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","coach");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null ? null : JsonSerializer.Deserialize<CoachFeedbackTargetsResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(dto?.Items is null || dto.Items.Count>150 ||
                    dto.Items.Select(item=>item.OrganizationId+"/"+item.Id).Distinct().Count()!=dto.Items.Count ||
                    dto.Items.Any(item=>item.OrganizationId is not
                        ("lefteria_fc" or "lefteria_fc_academy") ||
                        string.IsNullOrWhiteSpace(item.Id) || item.Id.Length>128 ||
                        string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>200))
                    throw Unavailable();
                return dto;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Current coach assignment is unavailable."),
                "54000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.AccountUnavailable,"Too many eligible players."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Coach roster is not ready."),
                _ => Unavailable()
            };
        }
    }

    public async Task<CoachFeedbackSendResult> SendAsync(
        MobileSession actor,Guid draftId,long version,CancellationToken token)
    {
        if(actor.Role!=MobileRole.Coach || draftId==Guid.Empty || version!=2)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid coach send request.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT club.send_mobile_coach_feedback(
                      @identity,@phone,@role,@expires,@id,@version)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","coach");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("id",draftId);
                command.Parameters.AddWithValue("version",version);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null ? null : JsonSerializer.Deserialize<CoachFeedbackSendResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(dto is null || dto.Id!=draftId ||
                    dto.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
                    dto.Status!="sent" || dto.Version!=3 ||
                    !dto.InboxAvailable || dto.Read)
                    throw Unavailable();
                return dto;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "Coach or recipient permission is no longer available."),
                "P0002" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Coach draft is unavailable."),
                "22023" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Invalid coach feedback send."),
                "40001" => new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Feedback changed; refresh and retry."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Roster is not ready."),
                _ => Unavailable()
            };
        }
    }

    public async Task<CoachFeedbackInboxResult> InboxAsync(
        MobileSession actor,int offset,CancellationToken token)
    {
        if(actor.Role!=MobileRole.Player || offset is <0 or >10000)
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Player role required.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT club.read_mobile_player_coach_feedback(
                      @identity,@phone,@role,@expires,@offset)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","player");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("offset",offset);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null ? null : JsonSerializer.Deserialize<CoachFeedbackInboxResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(dto?.Items is null || dto.Items.Count>20 ||
                    dto.Items.Any(item=>item is null || item.Id==Guid.Empty ||
                        item.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
                        string.IsNullOrWhiteSpace(item.CoachName) ||
                        item.CoachName.Length>200 ||
                        string.IsNullOrWhiteSpace(item.Message) ||
                        item.Message.Length is <30 or >1600 ||
                        item.SentAt==default || item.SentAt>DateTimeOffset.UtcNow))
                    throw Unavailable();
                return dto;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Player account is no longer authorized."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Roster is not ready."),
                _ => Unavailable()
            };
        }
    }

    public async Task<CoachFeedbackSignoffResult> ApproveAsync(
        MobileSession actor,Guid draftId,long version,string finalText,
        CancellationToken token)
    {
        if(actor.Role!=MobileRole.Coach || draftId==Guid.Empty || version!=1 ||
            finalText is not {Length:>=30 and <=1600} ||
            finalText!=finalText.Trim() || finalText.Any(char.IsControl))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid coach feedback signoff.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT club.approve_mobile_coach_feedback_draft(
                      @identity,@phone,@role,@expires,@id,@version,@text)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","coach");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("id",draftId);
                command.Parameters.AddWithValue("version",version);
                command.Parameters.AddWithValue("text",finalText);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var dto=raw is null ? null : JsonSerializer.Deserialize<CoachFeedbackSignoffResult>(
                    raw,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower});
                if(dto is null || dto.Id!=draftId || dto.OrganizationId is not
                    ("lefteria_fc" or "lefteria_fc_academy") ||
                    string.IsNullOrWhiteSpace(dto.PlayerId) ||
                    dto.Status!="approved" || dto.Version!=2 ||
                    !dto.AuthorizedByCoach || dto.Delivered)
                    throw Unavailable();
                return dto;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Coach assignment is no longer valid."),
                "P0002" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Coach draft or player unavailable."),
                "22023" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Invalid coach feedback signoff."),
                "40001" => new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Draft changed; refresh and retry."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable,"Coach roster is not ready."),
                _ => Unavailable()
            };
        }
    }

    public async Task<CoachFeedbackDraftResult> CreateAsync(
        MobileSession actor, string organization, string playerId,
        string observation, string focusPoint, string language,
        CancellationToken token)
    {
        if (actor.Role != MobileRole.Coach ||
            organization is not ("lefteria_fc" or "lefteria_fc_academy") ||
            string.IsNullOrEmpty(playerId) || playerId.Length > 128 ||
            !playerId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            !ValidText(observation) || !ValidText(focusPoint) ||
            language is not ("el" or "en"))
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput, "Invalid coach feedback draft.");

        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command = new NpgsqlCommand("""
                    SELECT club.create_mobile_coach_feedback_draft(
                      @identity,@phone,@role,@expires,@id,@owner,
                      @player,@observation,@focus,@language)::text
                    """, (NpgsqlConnection)database.Database.GetDbConnection());
                var id = Guid.NewGuid();
                command.Parameters.AddWithValue("identity", actor.IdentityId);
                command.Parameters.AddWithValue("phone", actor.NormalizedPhone);
                command.Parameters.AddWithValue("role", "coach");
                command.Parameters.AddWithValue("expires", actor.ExpiresAt);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("owner", organization);
                command.Parameters.AddWithValue("player", playerId);
                command.Parameters.AddWithValue("observation", observation);
                command.Parameters.AddWithValue("focus", focusPoint);
                command.Parameters.AddWithValue("language", language);
                var json = await command.ExecuteScalarAsync(token) as string;
                if (json is null) throw Unavailable();
                var dto = JsonSerializer.Deserialize<CoachFeedbackDraftResult>(json,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
                if (dto is null || dto.Id != id || dto.OrganizationId != organization ||
                    dto.PlayerId != playerId || dto.Status != "draft" ||
                    dto.Version != 1 || dto.Language != language ||
                    !dto.RequiresCoachApproval)
                    throw Unavailable();
                return dto;
            }
            finally { await database.Database.CloseConnectionAsync(); }
        }
        catch (PostgresException error)
        {
            throw error.SqlState switch
            {
                "42501" => new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable, "Coach assignment is no longer valid."),
                "P0002" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput, "Player is unavailable."),
                "22023" => new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput, "Invalid coach feedback."),
                "55000" => new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable, "Coach roster is not ready."),
                "23505" => new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput, "Duplicate coach feedback draft."),
                _ => Unavailable()
            };
        }
    }

    private static bool ValidText(string? value) =>
        value is { Length: >= 5 and <= 500 } &&
        value == value.Trim() && !value.Any(char.IsControl);

    private static MobileAuthException Unavailable() => new(
        MobileAuthFaultKind.ServiceUnavailable, MobileAuthErrorCode.AccountUnavailable,
        "Coach feedback is unavailable.");
}

public sealed record CoachFeedbackDraftResult(
    Guid Id, string OrganizationId, string PlayerId, string Status,
    long Version, string Language, bool RequiresCoachApproval);

public sealed record CoachFeedbackSignoffResult(Guid Id,string OrganizationId,
    string PlayerId,string Status,long Version,bool AuthorizedByCoach,bool Delivered);

public sealed record CoachFeedbackSendResult(Guid Id,string OrganizationId,
    string Status,long Version,bool InboxAvailable,bool Read);
public sealed record CoachFeedbackInboxItem(Guid Id,string OrganizationId,
    string CoachName,string Message,DateTimeOffset SentAt);
public sealed record CoachFeedbackInboxResult(IReadOnlyList<CoachFeedbackInboxItem> Items);

public sealed record CoachFeedbackTarget(string Id,string OrganizationId,string Name);
public sealed record CoachFeedbackTargetsResult(IReadOnlyList<CoachFeedbackTarget> Items);
