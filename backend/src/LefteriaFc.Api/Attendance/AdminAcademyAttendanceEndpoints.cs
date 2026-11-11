using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Attendance;

/// <summary>
/// The OS manager's actual attendance register, linked to the imported
/// training calendar. RSVP data and source attendance remain immutable.
/// No coach/parent/public write delegation is implied by this initial slice.
/// </summary>
public static class AdminAcademyAttendanceEndpoints
{
    public sealed record AttendanceMarkCommand(
        string? PlayerId,string? Status,int ExpectedVersion);

    public static IEndpointRouteBuilder MapAdminAcademyAttendance(
        this IEndpointRouteBuilder endpoints)
    {
        var admin=endpoints.MapGroup("/api/admin/academy/training")
            .WithTags("Academy training attendance")
            .RequireAuthorization(WebAdminServices.Policy);

        admin.MapGet("/",async (
            string? organization_id,string? group_id,
            DateOnly? start,DateOnly? end,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct);
            if(context.Request.Query["group_id"].Count!=1 ||
               context.Request.Query["start"].Count!=1 ||
               context.Request.Query["end"].Count!=1 ||
               group_id is null || group_id.Length is <1 or >128 ||
               group_id.Any(char.IsControl) ||
               start is null || end is null ||
               end<start || end.Value.DayNumber-start.Value.DayNumber>93)
                throw new WebAdminException(400,"invalid_training_filters",
                    "Choose one Academy group and a date range of at most 93 days.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_academy_training_sessions(
                  @digest,@owner,@group,@from,@to)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("group",group_id);
            command.Parameters.AddWithValue("from",NpgsqlDbType.Date,start.Value);
            command.Parameters.AddWithValue("to",NpgsqlDbType.Date,end.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminAcademyTrainingSessions");

        admin.MapGet("/{eventId}/attendance",async (
            string eventId,string? organization_id,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct);
            ValidateId(eventId);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_academy_training_attendance(
                  @digest,@owner,@event)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("event",eventId);
            return await Run(command,ct);
        }).WithName("ReadAdminAcademyTrainingAttendance");

        admin.MapPost("/{eventId}/attendance",async (
            string eventId,string? organization_id,AttendanceMarkCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct);
            ValidateId(eventId);
            ValidateId(body.PlayerId);
            if(body.Status is not ("present" or "absent") ||
                body.ExpectedVersion is <0 or >9999)
                throw new WebAdminException(422,"invalid_attendance_mark",
                    "Choose a valid attendance status and current version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.record_admin_academy_training_attendance(
                  @digest,@owner,@event,@player,@status,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("event",eventId);
            command.Parameters.AddWithValue("player",body.PlayerId!);
            command.Parameters.AddWithValue("status",body.Status);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            return await Run(command,ct);
        }).WithName("RecordAdminAcademyTrainingAttendance")
          .Accepts<AttendanceMarkCommand>("application/json");
        return endpoints;
    }

    private static void ValidateId(string? id)
    {
        if(id is null || id.Length is <1 or >128 || id.Any(char.IsControl))
            throw new WebAdminException(400,"invalid_attendance_identity",
                "A valid training session and roster member are required.");
    }

    private static async Task<IResult> Run(NpgsqlCommand command,CancellationToken ct)
    {
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null ? Results.Problem(statusCode:503)
                : Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException e) when(e.SqlState is
            "42501" or "22023" or "23505" or "55000" or "54000")
        {
            var status=e.SqlState switch
            {
                "42501"=>403,"22023"=>422,"23505"=>409,
                "54000"=>413,_=>503
            };
            throw new WebAdminException(status,"academy_attendance_unavailable",
                "Training records changed or are not available for this account.");
        }
    }

    private static async Task<string> Authorized(
        WebAdminService auth,HttpContext context,string? org,
        bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(org!="lefteria_fc_academy" ||
           context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization",
                "Select exactly one Academy organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated",
                "Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin") ||
           !AdminPermissions.Has(session.User.Role,
                write?"attendance:write":"attendance:read") ||
           !session.User.Organizations.Any(x=>x.Id==org))
            throw new WebAdminException(403,"attendance_denied",
                "Only authorized Academy managers may manage attendance.");
        return cookie!;
    }
}
