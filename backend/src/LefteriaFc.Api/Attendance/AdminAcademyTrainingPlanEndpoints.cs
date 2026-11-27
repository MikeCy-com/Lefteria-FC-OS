using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Attendance;

/// <summary>
/// Private manager planning only. No published calendar mutation, SMS, fan
/// visibility, attendance side effects or athlete-specific information.
/// Every write is reauthorized and audited within the database transaction.
/// </summary>
public static class AdminAcademyTrainingPlanEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrainingDrillSelectionCommand(Guid DrillId,int DrillVersion,int DurationMinutes);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrainingDrillsCommand(int ExpectedVersion,TrainingDrillSelectionCommand[]? Drills);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record TrainingDeliveryCommand(int ScheduleVersion,int ExpectedSequenceVersion,TrainingDrillSelectionCommand[]? Drills);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CompleteTrainingReviewCommand(int ExpectedVersion,string? MemberSummary,bool ConfirmHumanReview);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SubmitTrainingPlanCommand(int ExpectedVersion,bool ConfirmSubmission);

    public sealed record CancelPublishedTrainingCommand(
        int ExpectedVersion,string Reason,bool ConfirmCancellation);

    public sealed record PublishTrainingPlanCommand(
        int ExpectedVersion,bool ConfirmPublication);

    public sealed record ApproveTrainingPlanCommand(
        int ExpectedVersion,bool ConfirmHumanReview);

    public sealed record TrainingPlanCommand(
        string GroupId, DateOnly Day, string StartTime,
        int DurationMinutes, string Title, string Location,
        string Notes, int ExpectedVersion, bool Archive);

    public static IEndpointRouteBuilder MapAdminAcademyTrainingPlans(
        this IEndpointRouteBuilder endpoints)
    {
        var admin=endpoints.MapGroup("/api/admin/academy/training/plans")
            .WithTags("Academy private training plans")
            .RequireAuthorization(WebAdminServices.Policy);

        admin.MapGet("/", async (
            string? organization_id,string? group_id,DateOnly? start,DateOnly? end,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            if(context.Request.Query["group_id"].Count!=1 ||
                context.Request.Query["start"].Count!=1 ||
                context.Request.Query["end"].Count!=1 ||
                !ValidId(group_id) || start is null || end is null ||
                end<start || end.Value.DayNumber-start.Value.DayNumber>93)
                throw new WebAdminException(400,"invalid_training_plan_filters",
                    "Select one Academy group and a date range of at most 93 days.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_academy_training_plans(
                  @digest,@owner,@group,@from,@to)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("from",NpgsqlDbType.Date,start.Value);
            command.Parameters.AddWithValue("to",NpgsqlDbType.Date,end.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminAcademyTrainingPlanDrafts");

        admin.MapPost("/",async (
            string? organization_id,TrainingPlanCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct,allowAssigned:true);
            var start=Validate(body,true);
            return await Write(Guid.NewGuid(),0,false,start,body,organization_id!,
                cookie,connections,secrets,ct,true);
        }).WithName("CreateAdminAcademyTrainingPlanDraft")
            .Accepts<TrainingPlanCommand>("application/json");

        admin.MapPut("/{planId:guid}",async (
            Guid planId,string? organization_id,TrainingPlanCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct,allowAssigned:true);
            var start=Validate(body,false);
            if(planId==Guid.Empty)
                throw new WebAdminException(422,"invalid_training_plan",
                    "Select a valid training plan.");
            return await Write(planId,body.ExpectedVersion,body.Archive,start,
                body,organization_id!,cookie,connections,secrets,ct,false);
        }).WithName("UpdateAdminAcademyTrainingPlanDraft")
            .Accepts<TrainingPlanCommand>("application/json");

        admin.MapGet("/approvals",async (
            string? organization_id,string? group_id,DateOnly? start,DateOnly? end,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            if(context.Request.Query["group_id"].Count!=1 ||
                context.Request.Query["start"].Count!=1 ||
                context.Request.Query["end"].Count!=1 ||
                !ValidId(group_id) || start is null || end is null ||
                end<start || end.Value.DayNumber-start.Value.DayNumber>93)
                throw new WebAdminException(400,"invalid_training_plan_approval_filters",
                    "Choose exactly one Academy group and date range.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_academy_training_plan_approvals(
                  @digest,@owner,@group,@from,@to)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("from",NpgsqlDbType.Date,start.Value);
            command.Parameters.AddWithValue("to",NpgsqlDbType.Date,end.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminAcademyTrainingPlanApprovals");

        admin.MapPost("/{planId:guid}/approve",async (
            Guid planId,string? organization_id,ApproveTrainingPlanCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct);
            if(planId==Guid.Empty || body.ExpectedVersion is <1 or >10000 ||
               !body.ConfirmHumanReview)
                throw new WebAdminException(422,"invalid_training_plan_approval",
                    "Confirm the exact current plan version for human review.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.approve_admin_academy_training_plan(
                  @digest,@owner,@id,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            return await Run(command,ct);
        }).WithName("ApproveAdminAcademyTrainingPlanDraft")
          .Accepts<ApproveTrainingPlanCommand>("application/json");

        admin.MapGet("/{planId:guid}/publication-preview",async (
            Guid planId,int? version,string? organization_id,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct);
            if(planId==Guid.Empty || version is null ||
               version is <1 or >10000 ||
               context.Request.Query["version"].Count!=1)
                throw new WebAdminException(422,"invalid_training_preview",
                    "Select the exact approved training plan version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.preview_admin_academy_training_distribution(
                  @digest,@owner,@id,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",version.Value);
            return await Run(command,ct);
        }).WithName("PreviewAdminAcademyTrainingDistribution");

        admin.MapPost("/{planId:guid}/publish",async (
            Guid planId,string? organization_id,PublishTrainingPlanCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct);
            if(planId==Guid.Empty||body.ExpectedVersion is <1 or >10000||
               !body.ConfirmPublication)
                throw new WebAdminException(422,"training_publication_not_confirmed",
                    "Explicitly confirm publication of the exact approved version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.publish_admin_academy_training_plan(
                  @digest,@owner,@id,@expected,@confirmed)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            command.Parameters.AddWithValue("confirmed",body.ConfirmPublication);
            return await Run(command,ct,201);
        }).WithName("PublishAdminAcademyTrainingPlan")
          .Accepts<PublishTrainingPlanCommand>("application/json");

        admin.MapGet("/publications",async (
            string? organization_id,string? group_id,DateOnly? start,DateOnly? end,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            if(context.Request.Query["group_id"].Count!=1 ||
                context.Request.Query["start"].Count!=1 ||
                context.Request.Query["end"].Count!=1 ||
                !ValidId(group_id)||start is null||end is null||
                end<start||end.Value.DayNumber-start.Value.DayNumber>93)
                throw new WebAdminException(400,"invalid_training_publication_filters",
                    "Select exactly one Academy group and date range.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_academy_training_publications(
                  @digest,@owner,@group,@from,@to)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("from",NpgsqlDbType.Date,start.Value);
            command.Parameters.AddWithValue("to",NpgsqlDbType.Date,end.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminAcademyTrainingPublications");

        admin.MapPost("/{planId:guid}/cancel",async (
            Guid planId,string? organization_id,CancelPublishedTrainingCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,true,ct);
            if(planId==Guid.Empty || body.ExpectedVersion is <1 or >10000 ||
               !body.ConfirmCancellation || body.Reason is null ||
               body.Reason.Length is <8 or >500 ||
               body.Reason!=body.Reason.Trim() ||
               body.Reason.Any(char.IsControl))
                throw new WebAdminException(422,"training_cancellation_invalid",
                    "Confirm the exact published plan and enter a public-safe reason.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.cancel_admin_academy_training_publication(
                  @digest,@owner,@id,@version,@reason,@confirmed)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("version",body.ExpectedVersion);
            command.Parameters.AddWithValue("reason",body.Reason);
            command.Parameters.AddWithValue("confirmed",body.ConfirmCancellation);
            return await Run(command,ct);
        }).WithName("CancelAdminAcademyPublishedTraining")
          .Accepts<CancelPublishedTrainingCommand>("application/json");

        admin.MapGet("/{planId:guid}/drills",async (
            Guid planId,string? organization_id,string? group_id,int? version,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            if(planId==Guid.Empty||!ValidId(group_id)||version is null or <1 or >10000||
              context.Request.Query["group_id"].Count!=1||context.Request.Query["version"].Count!=1)
                throw new WebAdminException(422,"training_drill_selector_invalid","Select the exact group and plan version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_training_drills(@digest,@group,@id,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",version.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminTrainingDrills");

        admin.MapPut("/{planId:guid}/drills",async (
            Guid planId,string? organization_id,string? group_id,TrainingDrillsCommand body,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,true,ct,allowAssigned:true);
            if(planId==Guid.Empty||!ValidId(group_id)||context.Request.Query["group_id"].Count!=1||
              body.ExpectedVersion is <1 or >9999||body.Drills is null||body.Drills.Length>24||
              body.Drills.Any(x=>x is null||x.DrillId==Guid.Empty||x.DrillVersion is <1 or >10000||x.DurationMinutes is <1 or >120))
                throw new WebAdminException(422,"training_drill_input_invalid","Check the plan version and selected exercises.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_training_drills(@digest,@group,@id,@expected,@items)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            command.Parameters.AddWithValue("items",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(body.Drills.Select(x=>new {
                drill_id=x.DrillId,drill_version=x.DrillVersion,duration_minutes=x.DurationMinutes
            })));
            return await Run(command,ct);
        }).WithName("WriteAdminTrainingDrills").Accepts<TrainingDrillsCommand>("application/json");

        admin.MapGet("/{planId:guid}/delivery",async (
            Guid planId,string? organization_id,string? group_id,int? version,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            if(planId==Guid.Empty||!ValidId(group_id)||version is null or <1 or >10000||
              context.Request.Query["group_id"].Count!=1||context.Request.Query["version"].Count!=1)
                throw new WebAdminException(422,"training_drill_selector_invalid","Select the exact group and plan version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_training_delivery(@digest,@group,@id,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",version.Value);
            return await Run(command,ct);
        }).WithName("ReadAdminTrainingDelivery");

        admin.MapPut("/{planId:guid}/delivery",async (
            Guid planId,string? organization_id,string? group_id,TrainingDeliveryCommand body,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,true,ct,allowAssigned:true);
            if(planId==Guid.Empty||!ValidId(group_id)||context.Request.Query["group_id"].Count!=1||
              body.ScheduleVersion is <1 or >10000||body.ExpectedSequenceVersion is <0 or >9999||body.Drills is null||body.Drills.Length>24||
              body.Drills.Any(x=>x is null||x.DrillId==Guid.Empty||x.DrillVersion is <1 or >10000||x.DurationMinutes is <1 or >120))
                throw new WebAdminException(422,"training_drill_input_invalid","Check the plan version and selected exercises.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_training_delivery(@digest,@group,@id,@schedule,@expected,@items)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("schedule",body.ScheduleVersion);
            command.Parameters.AddWithValue("expected",body.ExpectedSequenceVersion);
            command.Parameters.AddWithValue("items",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(body.Drills.Select(x=>new {
                drill_id=x.DrillId,drill_version=x.DrillVersion,duration_minutes=x.DurationMinutes
            })));
            return await Run(command,ct);
        }).WithName("WriteAdminTrainingDelivery").Accepts<TrainingDeliveryCommand>("application/json");

        admin.MapPost("/{planId:guid}/drills/approve",async (
            Guid planId,string? organization_id,string? group_id,CompleteTrainingReviewCommand body,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,true,ct);
            if(planId==Guid.Empty||!ValidId(group_id)||context.Request.Query["group_id"].Count!=1||
              body.ExpectedVersion is <1 or >10000||!body.ConfirmHumanReview||
              body.MemberSummary is null||body.MemberSummary.Length is <1 or >1200||
              body.MemberSummary!=body.MemberSummary.Trim()||body.MemberSummary.Any(c=>char.IsControl(c)&&c!='\n'))
                throw new WebAdminException(422,"training_complete_review_invalid","Review the exact version and enter a member-safe summary.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.approve_admin_training_drill_plan(@digest,@group,@id,@expected,@summary,@confirmed)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            command.Parameters.AddWithValue("summary",body.MemberSummary);
            command.Parameters.AddWithValue("confirmed",body.ConfirmHumanReview);
            return await Run(command,ct);
        }).WithName("ApproveAdminTrainingDrillPlan").Accepts<CompleteTrainingReviewCommand>("application/json");

        admin.MapGet("/assigned-groups",async (
            string? organization_id,WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,false,ct,allowAssigned:true);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("SELECT club.read_admin_coach_training_groups(@digest)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            return await Run(command,ct);
        }).WithName("ReadAssignedCoachTrainingGroups");

        admin.MapPost("/{planId:guid}/submit",async (
            Guid planId,string? organization_id,string? group_id,SubmitTrainingPlanCommand body,
            WebAdminService auth,WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>{
            var cookie=await Authorized(auth,context,organization_id,true,ct,allowAssigned:true);
            if(planId==Guid.Empty||!ValidId(group_id)||context.Request.Query["group_id"].Count!=1||
                body.ExpectedVersion is <1 or >10000||!body.ConfirmSubmission)
                throw new WebAdminException(422,"training_submission_invalid","Confirm the exact draft version and group.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("SELECT club.submit_admin_training_plan(@digest,@group,@id,@expected,@confirm)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("group",group_id!);
            command.Parameters.AddWithValue("id",planId);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            command.Parameters.AddWithValue("confirm",body.ConfirmSubmission);
            return await Run(command,ct);
        }).WithName("SubmitAssignedCoachTrainingPlan").Accepts<SubmitTrainingPlanCommand>("application/json");

        return endpoints;
    }

    private static bool ValidId(string? value) =>
        value is { Length: >=1 and <=128 } && !value.Any(char.IsControl);

    private static TimeOnly Validate(TrainingPlanCommand body,bool create)
    {
        if(!ValidId(body.GroupId) ||
            body.DurationMinutes is <15 or >240 ||
            body.Title is not { Length: >=1 and <=180 } ||
            body.Title!=body.Title.Trim() || body.Title.Any(char.IsControl) ||
            body.Location is null || body.Location.Length>200 ||
            body.Location.Any(char.IsControl) ||
            body.Notes is null || body.Notes.Length>2000 ||
            body.Notes.Any(char.IsControl) ||
            body.StartTime is null ||
            !Regex.IsMatch(body.StartTime,@"^([01][0-9]|2[0-3]):[0-5][0-9]$",RegexOptions.CultureInvariant) ||
            !TimeOnly.TryParseExact(body.StartTime,"HH:mm",CultureInfo.InvariantCulture,
                DateTimeStyles.None,out var start) ||
            (create && (body.ExpectedVersion!=0 || body.Archive)) ||
            (!create && body.ExpectedVersion is <1 or >9999))
            throw new WebAdminException(422,"invalid_training_plan",
                "Check the group, date, time, duration and draft version.");
        return start;
    }

    private static async Task<IResult> Write(
        Guid id,int expected,bool archive,TimeOnly start,TrainingPlanCommand body,
        string organization,string cookie,WebAdminConnections connections,
        IWebAdminSecrets secrets,CancellationToken ct,bool create)
    {
        await using var connection=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand("""
            SELECT club.write_admin_academy_training_plan(
              @digest,@owner,@id,@group,@day,@start,@duration,
              @title,@location,@notes,@expected,@archive)::text
            """,connection);
        command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
        command.Parameters.AddWithValue("owner",organization);
        command.Parameters.AddWithValue("id",id);
        command.Parameters.AddWithValue("group",body.GroupId);
        command.Parameters.AddWithValue("day",NpgsqlDbType.Date,body.Day);
        command.Parameters.AddWithValue("start",NpgsqlDbType.Time,start);
        command.Parameters.AddWithValue("duration",body.DurationMinutes);
        command.Parameters.AddWithValue("title",body.Title);
        command.Parameters.AddWithValue("location",body.Location);
        command.Parameters.AddWithValue("notes",body.Notes);
        command.Parameters.AddWithValue("expected",expected);
        command.Parameters.AddWithValue("archive",archive);
        return await Run(command,ct,create?201:200);
    }

    private static async Task<IResult> Run(
        NpgsqlCommand command,CancellationToken ct,int status=200)
    {
        try
        {
            var raw=await command.ExecuteScalarAsync(ct) as string;
            return raw is null?Results.Problem(statusCode:503)
                :Results.Content(raw,"application/json",Encoding.UTF8,status);
        }
        catch(PostgresException e) when(e.SqlState is
            "42501" or "22023" or "23505" or "55000" or "54000")
        {
            var statusCode=e.SqlState switch {
                "42501"=>403,"22023"=>422,"23505"=>409,
                "54000"=>413,_=>503
            };
            throw new WebAdminException(statusCode,"training_plan_unavailable",
                "Private training plan changed or is unavailable to this account.");
        }
    }

    private static async Task<string> Authorized(
        WebAdminService auth,HttpContext context,string? owner,
        bool write,CancellationToken ct,bool allowAssigned=false)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(owner!="lefteria_fc_academy" ||
           context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"invalid_organization",
                "Select exactly one Academy organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        var manager=session.User.Role is ("manager" or "super_admin") &&
            AdminPermissions.Has(session.User.Role,write?"training:write":"training:read");
        var assigned=allowAssigned && session.User.Role=="coach" &&
            AdminPermissions.Has(session.User.Role,write?"training:write_assigned":"training:read_assigned");
        if((!manager&&!assigned)||!session.User.Organizations.Any(x=>x.Id==owner))
            throw new WebAdminException(403,"training_plan_denied",
                "This account does not have the required Academy training permission.");
        return cookie!;
    }
}
