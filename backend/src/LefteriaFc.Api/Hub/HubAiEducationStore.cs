using System.Text.Json;
using Npgsql;

namespace LefteriaFc.Api.Hub;

/// <summary>
/// Uses ONLY the independently credentialed worker database role. The worker
/// cannot SELECT a queue row or any roster, cannot approve, publish or send.
/// </summary>
public sealed class HubAiEducationStore(string connectionString)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AiEducationJob?> ClaimAsync(CancellationToken token)
    {
        var lease=Guid.NewGuid();
        var raw=await CallAsync(
            "SELECT hub.claim_ai_education_brief(@lease)::text",
            command=>command.Parameters.AddWithValue("lease",lease),token);
        if (raw is null || raw=="null")return null;
        var job=JsonSerializer.Deserialize<AiEducationJob>(raw,Json);
        if (job is null || job.Id==Guid.Empty || job.LeaseId!=lease ||
            job.OrganizationId is not ("lefteria_fc" or "lefteria_fc_academy") ||
            job.Audience is not ("parent" or "player") ||
            job.AgeMin<6 || job.AgeMax>18 || job.AgeMin>job.AgeMax ||
            job.Language is not ("el" or "en") ||
            job.Attempts is <1 or >3 || !HubEducationProvider.AllowedTopic(job))
            throw new InvalidOperationException("Untrusted AI job descriptor.");
        return job;
    }

    public async Task CompleteAsync(AiEducationJob job,
        AiEducationArticle article, string model,CancellationToken token)
    {
        var contentId=Guid.NewGuid();
        var raw=await CallAsync("""
            SELECT hub.complete_ai_education_brief(@id,@lease,@content,@revision,
              @title,@body,@model,@prompt)::text
            """, command=>
        {
            command.Parameters.AddWithValue("id",job.Id);
            command.Parameters.AddWithValue("lease",job.LeaseId);
            command.Parameters.AddWithValue("content",contentId);
            command.Parameters.AddWithValue("revision",Guid.NewGuid());
            command.Parameters.AddWithValue("title",article.Title);
            command.Parameters.AddWithValue("body",article.Body);
            command.Parameters.AddWithValue("model",model);
            command.Parameters.AddWithValue("prompt","education-v1");
        },token);
        var result=raw is null ? null :
            JsonSerializer.Deserialize<AiEducationCompleteResult>(raw,Json);
        if (result is null || result.Id!=job.Id || result.ContentId!=contentId ||
            result.OrganizationId!=job.OrganizationId ||
            result.Status!="completed" || result.DraftStatus!="ai_draft" ||
            result.TextReview!="pending" || result.VisualReview!="pending")
            throw new InvalidOperationException("Invalid AI draft transaction result.");
    }

    public async Task FailAsync(AiEducationJob job,string reason,CancellationToken token)
    {
        if (reason is not ("provider_unavailable" or "invalid_output" or
            "moderation_blocked" or "provider_rejected" or "quota_exceeded"))
            throw new ArgumentOutOfRangeException(nameof(reason));
        var raw=await CallAsync("SELECT hub.fail_ai_education_brief(@id,@lease,@reason)::text",
            command=>
            {
                command.Parameters.AddWithValue("id",job.Id);
                command.Parameters.AddWithValue("lease",job.LeaseId);
                command.Parameters.AddWithValue("reason",reason);
            },token);
        var result=raw is null ? null :
            JsonSerializer.Deserialize<AiEducationFailResult>(raw,Json);
        if (result is null || result.Id!=job.Id ||
            result.Status is not ("pending" or "failed"))
            throw new InvalidOperationException("Invalid AI job failure result.");
    }

    private async Task<string?> CallAsync(string sql,
        Action<NpgsqlCommand> bind,CancellationToken token)
    {
        await using var connection=new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command=new NpgsqlCommand(sql,connection) { CommandTimeout=10 };
        bind(command);
        return await command.ExecuteScalarAsync(token) as string;
    }
}

public sealed record AiEducationJob(Guid Id,string OrganizationId,string Audience,
    short AgeMin,short AgeMax,string Topic,string Language,Guid LeaseId,short Attempts);

public sealed record AiEducationArticle(string Title,string Body);

public sealed record AiEducationCompleteResult(Guid Id,Guid ContentId,
    string OrganizationId,string Status,string DraftStatus,string TextReview,
    string VisualReview);

public sealed record AiEducationFailResult(Guid Id,string Status);
