using System.Text.Json;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.Hub;

public sealed record PlayerClassroomLessons(
    int Approved,int Completed,string? NextTitle,string? NextKind);
public sealed record PlayerClassroomQuizzes(
    bool Started,int CompletedWeeks,int CurrentWeek,
    string? NextQuizTitle,string? NextQuizTopic);
public sealed record PlayerClassroomSubject(
    string Topic,int PublishedQuizzes,int CompletedQuizzes,
    int CorrectAnswers,int TotalAnswers,int ApprovedLessons,int CompletedLessons);
public sealed record PlayerClassroomPage(
    string OrganizationId,string Audience,string VerifiedPosition,
    PlayerClassroomLessons Lessons,PlayerClassroomQuizzes Quizzes,
    IReadOnlyList<PlayerClassroomSubject> Subjects);

/// <summary>
/// An authorized mobile player's evidence-based classroom overview only.
/// Raw learning, progress, question and roster tables are never read here.
/// </summary>
public sealed class MobilePlayerClassroomStore(LefteriaDbContext database)
{
    private static readonly JsonSerializerOptions Json=new(){
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
    private static readonly HashSet<string> Topics=[
        "positioning","defending","attacking","transition","passing",
        "movement","teamwork","rules","decision_making","goalkeeping","general"];
    private static readonly HashSet<string> Positions=[
        "all","goalkeeper","defender","fullback","winger","midfielder","striker"];

    public async Task<PlayerClassroomPage> ReadAsync(
        MobileSession actor,string organization,CancellationToken token)
    {
        if(actor.Role!=MobileRole.Player ||
           organization is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                MobileAuthErrorCode.RoleUnavailable,"Verified player role required.");
        try
        {
            await database.Database.OpenConnectionAsync(token);
            try
            {
                await using var command=new NpgsqlCommand("""
                    SELECT hub.read_mobile_player_classrooms(
                      @identity,@phone,@role,@expires,@owner)::text
                    """,(NpgsqlConnection)database.Database.GetDbConnection());
                command.Parameters.AddWithValue("identity",actor.IdentityId);
                command.Parameters.AddWithValue("phone",actor.NormalizedPhone);
                command.Parameters.AddWithValue("role","player");
                command.Parameters.AddWithValue("expires",actor.ExpiresAt);
                command.Parameters.AddWithValue("owner",organization);
                var raw=await command.ExecuteScalarAsync(token) as string;
                var result=raw is null?null:JsonSerializer.Deserialize<PlayerClassroomPage>(raw,Json);
                if(result is null || result.OrganizationId!=organization ||
                   result.Audience!="player" || !Positions.Contains(result.VerifiedPosition) ||
                   result.Lessons is null || result.Quizzes is null ||
                   result.Lessons.Approved is <0 or >100000 ||
                   result.Lessons.Completed<0 || result.Lessons.Completed>result.Lessons.Approved ||
                   result.Lessons.NextTitle is {Length:>180} ||
                   result.Lessons.NextKind is not (null or "article" or "course") ||
                   (result.Lessons.NextTitle is null)!=(result.Lessons.NextKind is null) ||
                   result.Quizzes.CompletedWeeks is <0 or >520 ||
                   result.Quizzes.CurrentWeek!=Math.Min(521,result.Quizzes.CompletedWeeks+1) ||
                   result.Quizzes.NextQuizTitle is {Length:>180} ||
                   (result.Quizzes.NextQuizTitle is null)!=(result.Quizzes.NextQuizTopic is null) ||
                   (result.Quizzes.NextQuizTopic is { } topic && !Topics.Contains(topic)) ||
                   (!result.Quizzes.Started &&
                     (result.Quizzes.NextQuizTitle is not null || result.Quizzes.CompletedWeeks!=0)) ||
                   result.Subjects is null || result.Subjects.Count!=Topics.Count ||
                   result.Subjects.Any(item=>item is null || !Topics.Contains(item.Topic) ||
                     item.PublishedQuizzes is <0 or >520 ||
                     item.CompletedQuizzes<0 ||
                     item.CompletedQuizzes>item.PublishedQuizzes ||
                     item.CorrectAnswers<0 || item.TotalAnswers<0 ||
                     item.CorrectAnswers>item.TotalAnswers ||
                     item.ApprovedLessons is <0 or >100000 ||
                     item.CompletedLessons<0 ||
                     item.CompletedLessons>item.ApprovedLessons) ||
                   result.Subjects.Select(item=>item.Topic).Distinct().Count()!=Topics.Count)
                    throw Unavailable();
                return result;
            }
            finally {await database.Database.CloseConnectionAsync();}
        }
        catch(PostgresException ex)
        {
            if(ex.SqlState=="42501")
                throw new MobileAuthException(MobileAuthFaultKind.Forbidden,
                    MobileAuthErrorCode.RoleUnavailable,
                    "Player classroom access is no longer authorized.");
            throw Unavailable();
        }
    }

    private static MobileAuthException Unavailable()=>new(
        MobileAuthFaultKind.ServiceUnavailable,MobileAuthErrorCode.AccountUnavailable,
        "Player classrooms are temporarily unavailable.");
}
