using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Hub;

public sealed record PlayerWeeklyQuizQuestion(
    Guid Id,int Sequence,string Prompt,string[] Choices);
public sealed record PlayerWeeklyQuiz(
    Guid Id,string Title,string Topic,string Position,int WeekNumber,
    IReadOnlyList<PlayerWeeklyQuizQuestion> Questions);
public sealed record PlayerWeeklyQuizPage(
    string OrganizationId,bool Started,PlayerWeeklyQuiz? Quiz);
public sealed record PlayerWeeklyQuizStart(
    string OrganizationId,DateTimeOffset StartedAt);
public sealed record PlayerWeeklyQuizScore(
    string OrganizationId,Guid QuizId,int Score,int Total,bool Submitted);
public sealed record PlayerWeeklyQuizSubmission(Guid QuizId,JsonElement Answers);

/// <summary>No raw private quiz or answer-key table permissions.</summary>
public sealed class MobilePlayerWeeklyQuizStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    private static readonly string[] Topics=[
        "positioning","defending","attacking","transition","passing",
        "movement","teamwork","rules","decision_making","goalkeeping","general"];
    private static readonly string[] Positions=[
        "all","goalkeeper","defender","fullback","winger","midfielder","striker"];

    public async Task<PlayerWeeklyQuizStart> StartAsync(
        MobileSession actor,string organization,CancellationToken token)
    {
        Check(actor,organization);
        var result=await ExecuteAsync<PlayerWeeklyQuizStart>(
            "hub.start_mobile_player_quiz_track",actor,organization,null,null,token);
        if(result is null || result.OrganizationId!=organization ||
           result.StartedAt==default || result.StartedAt>DateTimeOffset.UtcNow)
            throw Unavailable();
        return result;
    }

    public async Task<PlayerWeeklyQuizPage> ReadAsync(
        MobileSession actor,string organization,CancellationToken token)
    {
        Check(actor,organization);
        var result=await ExecuteAsync<PlayerWeeklyQuizPage>(
            "hub.read_mobile_player_weekly_quiz",actor,organization,null,null,token);
        if(result is null || result.OrganizationId!=organization ||
           (!result.Started && result.Quiz is not null) ||
           (result.Quiz is { } quiz &&
            (quiz.Id==Guid.Empty || string.IsNullOrWhiteSpace(quiz.Title) ||
             quiz.Title.Length>180 || quiz.WeekNumber is <1 or >520 ||
             !Topics.Contains(quiz.Topic) || !Positions.Contains(quiz.Position) ||
             quiz.Questions is null || quiz.Questions.Count is <3 or >20 ||
             quiz.Questions.Any(q=>q is null || q.Id==Guid.Empty) ||
             quiz.Questions.Select(q=>q.Id).Distinct().Count()!=quiz.Questions.Count ||
             quiz.Questions.Any(q=>q is null || q.Id==Guid.Empty ||
               q.Sequence is <1 or >20 || string.IsNullOrWhiteSpace(q.Prompt) ||
               q.Prompt.Length>500 || q.Choices is not {Length:4} ||
               q.Choices.Any(choice=>string.IsNullOrWhiteSpace(choice) ||
                 choice.Length>500)))))
            throw Unavailable();
        return result;
    }

    public async Task<PlayerWeeklyQuizScore> SubmitAsync(
        MobileSession actor,string organization,PlayerWeeklyQuizSubmission submission,
        CancellationToken token)
    {
        Check(actor,organization);
        if(submission is null || submission.QuizId==Guid.Empty ||
           submission.Answers.ValueKind!=JsonValueKind.Array ||
           submission.Answers.GetArrayLength() is <3 or >20 ||
           submission.Answers.GetRawText().Length>12000)
            throw new MobileAuthException(MobileAuthFaultKind.Validation,
                MobileAuthErrorCode.InvalidInput,"Invalid weekly quiz answers.");
        var result=await ExecuteAsync<PlayerWeeklyQuizScore>(
            "hub.submit_mobile_player_weekly_quiz",actor,organization,
            submission.QuizId,submission.Answers.GetRawText(),token);
        if(result is null || result.OrganizationId!=organization ||
           result.QuizId!=submission.QuizId || !result.Submitted ||
           result.Total is <3 or >20 || result.Score<0 || result.Score>result.Total)
            throw Unavailable();
        return result;
    }

    private async Task<T?> ExecuteAsync<T>(
        string function,MobileSession actor,string organization,
        Guid? quizId,string? answers,CancellationToken token)
    {
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                var sql=function switch {
                    "hub.start_mobile_player_quiz_track"=>
                      "SELECT hub.start_mobile_player_quiz_track(@identity,@phone,@role,@expires,@owner)::text",
                    "hub.read_mobile_player_weekly_quiz"=>
                      "SELECT hub.read_mobile_player_weekly_quiz(@identity,@phone,@role,@expires,@owner)::text",
                    _=>"SELECT hub.submit_mobile_player_weekly_quiz(@identity,@phone,@role,@expires,@owner,@quiz,@answers)::text"
                };
                await using var command=new NpgsqlCommand(sql,
                    (NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","player");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",organization);
                if(quizId is { } id)command.Parameters.AddWithValue("quiz",id);
                if(answers is not null)
                    command.Parameters.Add("answers",NpgsqlDbType.Jsonb).Value=answers;
                var raw=await command.ExecuteScalarAsync(token) as string;
                return raw is null?default:JsonSerializer.Deserialize<T>(raw,Json);
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            throw ex.SqlState switch {
                "42501"=>new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,"Player quiz access unavailable."),
                "40001" or "23505"=>new MobileAuthException(MobileAuthFaultKind.Conflict,
                    MobileAuthErrorCode.InvalidInput,"Quiz already completed or changed."),
                "22023" or "22P02"=>new MobileAuthException(MobileAuthFaultKind.Validation,
                    MobileAuthErrorCode.InvalidInput,"Invalid quiz submission."),
                _=>Unavailable()
            };
        }
    }

    private static void Check(MobileSession actor,string organization)
    {
        if(actor.Role!=MobileRole.Player ||
           organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Active player role required.");
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Player learning quiz temporarily unavailable.");
}
