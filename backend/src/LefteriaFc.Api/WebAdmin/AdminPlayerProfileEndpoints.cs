using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.WebAdmin;

public sealed record AcademyPlayerLifecycleCommand(string? Action,int ExpectedRevision,Guid RequestId,string? Reason);
public sealed record UpdateAcademyPlayerContact(
    string? Name,string? Relationship,string? Phone,string? Email);
public sealed record CorrectAcademyPlayerName(string? ExpectedName,string? Name);
public sealed record CorrectAcademyPlayerSporting(string? ExpectedPosition,int? ExpectedNumber,string? Position,int? Number);
public sealed record UpdateAcademyPlayerPrivateProfile(
    int ExpectedRevision,string? Biography,string? PreferredPosition,int? PreferredNumber,
    UpdateAcademyPlayerContact? Contact=null,CorrectAcademyPlayerName? NameCorrection=null,CorrectAcademyPlayerSporting? SportingCorrection=null);

/// <summary>
/// Restricted staff-only Academy child profile. All reads, edits and photo
/// access are independently re-authorized against ALL current child groups and
/// city/location grants inside PostgreSQL. Photos are sanitized, EXIF stripped,
/// private and NEVER written into a publicly-addressable media URL.
/// </summary>
public static class AdminPlayerProfileEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlayerProfiles(this IEndpointRouteBuilder endpoints)
    {
        var routes=endpoints.MapGroup("/api/admin/football/players/{playerId}/private-profile")
            .WithTags("Private Academy player profiles")
            .RequireAuthorization(WebAdminServices.Policy);

        routes.MapGet("",async (string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            await using var db=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT club.read_admin_academy_player_profile(@digest,@player)::text",db);
            command.Parameters.AddWithValue("digest",digest);
            command.Parameters.AddWithValue("player",playerId);
            try
            {
                var json=await command.ExecuteScalarAsync(ct) as string;
                return json is null?Results.NotFound()
                    :Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException error){throw MapSql(error);}
        }).WithName("ReadAdminAcademyPrivatePlayerProfile").ExcludeFromDescription();

        routes.MapPut("",async (string playerId,string? organization_id,
            UpdateAcademyPlayerPrivateProfile body,HttpContext context,
            WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            if(body.SportingCorrection is not null)
            {
                if(body.NameCorrection is not null||body.Contact is not null||body.Biography is not null||
                    body.PreferredPosition is not null||body.PreferredNumber is not null)
                    throw new WebAdminException(422,"player_profile_invalid","Save sporting corrections separately.");
                return await CorrectSporting(digest,playerId,body.SportingCorrection,connections,ct);
            }
            if(body.NameCorrection is not null)
            {
                if(body.Contact is not null||body.Biography is not null||body.PreferredPosition is not null||body.PreferredNumber is not null)
                    throw new WebAdminException(422,"player_profile_invalid","Save the name correction separately.");
                return await CorrectName(digest,playerId,body.NameCorrection,connections,ct);
            }
            if(body.Contact is not null)
            {
                if(body.Biography is not null||body.PreferredPosition is not null||body.PreferredNumber is not null)
                    throw new WebAdminException(422,"player_profile_invalid",
                        "Save contact details separately from sporting preferences.");
                return await SaveContact(digest,playerId,body.ExpectedRevision,body.Contact,connections,ct);
            }
            if(body.ExpectedRevision is < 0 or > 1999999999 ||
               body.Biography is null || body.Biography.Length>1000 ||
               body.Biography!=body.Biography.Trim() ||
               body.PreferredPosition is null || body.PreferredPosition.Length>80 ||
               body.PreferredPosition!=body.PreferredPosition.Trim() ||
               body.PreferredPosition.Any(char.IsControl) ||
               body.Biography.Contains('\r') ||
               body.PreferredNumber is < 1 or > 99)
                throw new WebAdminException(422,"player_profile_invalid",
                    "Provide a concise biography and valid player preferences.");
            await using var db=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_academy_player_profile(
                  @digest,@player,@version,@bio,@position,@number)::text
                """,db);
            command.Parameters.AddWithValue("digest",digest);
            command.Parameters.AddWithValue("player",playerId);
            command.Parameters.AddWithValue("version",body.ExpectedRevision);
            command.Parameters.AddWithValue("bio",body.Biography);
            command.Parameters.AddWithValue("position",body.PreferredPosition);
            command.Parameters.Add("number",NpgsqlDbType.Integer).Value=
                body.PreferredNumber is null?DBNull.Value:body.PreferredNumber.Value;
            try
            {
                var json=await command.ExecuteScalarAsync(ct) as string;
                return json is null?Results.Problem(statusCode:503)
                    :Results.Content(json,"application/json",Encoding.UTF8,200);
            }
            catch(PostgresException error){throw MapSql(error);}
        }).WithName("EditAdminAcademyPrivatePlayerProfile").ExcludeFromDescription();

        routes.MapGet("/lifecycle",async (string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            return await Lifecycle(digest,playerId,"read",0,Guid.Empty,"",connections,ct);
        }).WithName("ReadAdminAcademyPlayerLifecycle").ExcludeFromDescription();
        routes.MapPost("/lifecycle",async (string playerId,string? organization_id,
            AcademyPlayerLifecycleCommand body,HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            if(body.Action is not ("archive" or "reactivate") || body.ExpectedRevision<0 ||
              body.RequestId==Guid.Empty || body.Reason is null || body.Reason.Length is <8 or >240 ||
              body.Reason!=body.Reason.Trim() || body.Reason.Any(char.IsControl))
                throw new WebAdminException(422,"player_lifecycle_invalid","Review the player and provide a reason.");
            return await Lifecycle(digest,playerId,body.Action!,body.ExpectedRevision,body.RequestId,body.Reason,connections,ct);
        }).WithName("ChangeAdminAcademyPlayerLifecycle").ExcludeFromDescription();

        routes.MapGet("/photo",async (string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            await using var db=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT club.read_admin_academy_player_portrait(@digest,@player)",db);
            command.Parameters.AddWithValue("digest",digest);
            command.Parameters.AddWithValue("player",playerId);
            try
            {
                var result=await command.ExecuteScalarAsync(ct);
                if(result is null or DBNull)return Results.NotFound();
                if(result is not byte[] jpeg)
                    return Results.Problem(statusCode:503);
                return Results.File(jpeg,contentType:"image/jpeg",
                    enableRangeProcessing:false);
            }
            catch(PostgresException error){throw MapSql(error);}
        }).WithName("ReadAdminAcademyPrivatePlayerPhoto").ExcludeFromDescription();

        routes.MapPost("/photo",async (string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,IAvatarImageSanitizer sanitizer,
            CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            var (input,mime)=await AdminProfilePhotoEndpoints.ReadSinglePhotoAsync(context.Request,ct);
            SanitizedMobileAvatar photo;
            try
            {
                try{photo=sanitizer.Sanitize(input,mime,ct);}
                catch(MobileAuthException e)
                {
                    throw new WebAdminException(e.Kind switch
                    {
                        MobileAuthFaultKind.TooManyRequests=>429,
                        MobileAuthFaultKind.PayloadTooLarge=>413,
                        MobileAuthFaultKind.ServiceUnavailable=>503,
                        _=>422
                    },"player_portrait_invalid","Provide one valid JPEG, PNG, GIF or WebP image.");
                }
            }
            finally{CryptographicOperations.ZeroMemory(input);}
            try
            {
                await SavePhoto(digest,playerId,photo.JpegBytes,connections,ct);
                return Results.NoContent();
            }
            finally{CryptographicOperations.ZeroMemory(photo.JpegBytes);}
        }).DisableAntiforgery()
          .WithName("UploadAdminAcademyPrivatePlayerPhoto").ExcludeFromDescription();

        routes.MapDelete("/photo",async (string playerId,string? organization_id,
            HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
            WebAdminConnections connections,CancellationToken ct)=>
        {
            var digest=await SessionDigest(playerId,organization_id,context,admin,secrets,ct);
            await SavePhoto(digest,playerId,null,connections,ct);
            return Results.NoContent();
        }).WithName("RemoveAdminAcademyPrivatePlayerPhoto").ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> CorrectSporting(string digest,string playerId,CorrectAcademyPlayerSporting correction,
        WebAdminConnections connections,CancellationToken ct)
    {
        if(correction.Position is not ("Goalkeeper" or "Defender" or "Midfielder" or "Forward")||
            correction.Number is null or <1 or >99 ||
            correction.ExpectedPosition is {Length:>32} || correction.ExpectedPosition?.Any(char.IsControl)==true)
            throw new WebAdminException(422,"player_profile_invalid","Select a valid position and number from 1 to 99.");
        await using var db=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand(
            "SELECT club.write_admin_academy_player_sporting(@digest,@player,@old_position,@old_number,@position,@number)::text",db);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("player",playerId);
        command.Parameters.Add("old_position",NpgsqlTypes.NpgsqlDbType.Text).Value=(object?)correction.ExpectedPosition??DBNull.Value;
        command.Parameters.Add("old_number",NpgsqlTypes.NpgsqlDbType.Integer).Value=(object?)correction.ExpectedNumber??DBNull.Value;
        command.Parameters.AddWithValue("position",correction.Position!);
        command.Parameters.AddWithValue("number",correction.Number.Value);
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503):
                Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException error){throw MapSql(error);}
    }

    private static async Task<IResult> CorrectName(string digest,string playerId,CorrectAcademyPlayerName correction,
        WebAdminConnections connections,CancellationToken ct)
    {
        if(correction.ExpectedName is null||correction.ExpectedName.Length is <1 or >200||
            correction.Name is null||correction.Name.Length is <1 or >200||
            correction.Name!=correction.Name.Trim()||correction.Name.Any(char.IsControl))
            throw new WebAdminException(422,"player_profile_invalid","Enter a valid player name.");
        await using var db=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand(
            "SELECT club.rename_admin_academy_player(@digest,@player,@expected,@name)::text",db);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("player",playerId);
        command.Parameters.AddWithValue("expected",correction.ExpectedName);
        command.Parameters.AddWithValue("name",correction.Name);
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503):
                Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException error){throw MapSql(error);}
    }

    private static async Task<IResult> SaveContact(string digest,string playerId,int expected,
        UpdateAcademyPlayerContact contact,WebAdminConnections connections,CancellationToken ct)
    {
        static bool Valid(string? value,int min,int max)=>value is not null &&
            value.Length>=min && value.Length<=max && value==value.Trim() && !value.Any(char.IsControl);
        if(expected is <0 or >1999999999 || !Valid(contact.Name,1,200) ||
            !Valid(contact.Relationship,1,80) || !Valid(contact.Phone,7,32) ||
            !Valid(contact.Email,3,254) ||
            !System.Text.RegularExpressions.Regex.IsMatch(contact.Phone!,@"^\+?[0-9 ()-]{7,32}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(contact.Email!,@"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            throw new WebAdminException(422,"player_profile_invalid","Enter valid contact details.");
        await using var db=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand(
            "SELECT club.write_admin_academy_player_contact(@digest,@player,@version,@name,@relationship,@phone,@email)::text",db);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("player",playerId);
        command.Parameters.AddWithValue("version",expected);
        command.Parameters.AddWithValue("name",contact.Name!);
        command.Parameters.AddWithValue("relationship",contact.Relationship!);
        command.Parameters.AddWithValue("phone",contact.Phone!);
        command.Parameters.AddWithValue("email",contact.Email!);
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503)
                :Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException error){throw MapSql(error);}
    }

    private static async Task SavePhoto(string digest,string playerId,byte[]? jpeg,
        WebAdminConnections connections,CancellationToken ct)
    {
        await using var db=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand(
            "SELECT club.write_admin_academy_player_portrait(@digest,@player,@photo)",db);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("player",playerId);
        command.Parameters.Add("photo",NpgsqlDbType.Bytea).Value=
            jpeg is null?DBNull.Value:jpeg;
        try
        {
            if(await command.ExecuteScalarAsync(ct) is not true)
                throw new WebAdminException(503,"player_portrait_unavailable",
                    "Private player portrait was not saved.");
        }
        catch(PostgresException error){throw MapSql(error);}
    }

    private static async Task<IResult> Lifecycle(string digest,string player,string action,int revision,Guid request,string reason,
        WebAdminConnections connections,CancellationToken ct)
    {
        await using var db=await connections.OpenAsync(ct);
        await using var command=new NpgsqlCommand(
            "SELECT club.admin_academy_player_lifecycle(@digest,@player,@action,@revision,@request,@reason)::text",db);
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("player",player);
        command.Parameters.AddWithValue("action",action);
        command.Parameters.AddWithValue("revision",revision);
        command.Parameters.AddWithValue("request",request);
        command.Parameters.AddWithValue("reason",reason);
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503):Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException error){throw MapSql(error);}
    }

    private static async Task<string> SessionDigest(string player,string? owner,
        HttpContext context,WebAdminService admin,IWebAdminSecrets secrets,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        context.Response.Headers.XContentTypeOptions="nosniff";
        context.Response.Headers.ContentSecurityPolicy="sandbox";
        if(owner!="lefteria_fc_academy" ||
           context.Request.Query["organization_id"].Count!=1 ||
           string.IsNullOrWhiteSpace(player) || player.Length>128 ||
           player.Any(char.IsControl))
            throw new WebAdminException(400,"player_profile_invalid_scope",
                "Select an exact Academy child and organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        if(cookie is null || !secrets.IsToken(cookie) ||
           await admin.CurrentAsync(cookie,ct) is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        return secrets.TokenDigest(cookie);
    }

    private static WebAdminException MapSql(PostgresException error)=>error.SqlState switch
    {
        "42501"=>new(403,"player_profile_access_revoked",
            "Private player profile access was revoked."),
        "23505"=>new(409,"player_profile_revision_conflict",
            "Another update changed this profile; refresh before saving."),
        "22023"=>new(422,"player_profile_invalid",
            "Invalid player profile fields or portrait."),
        _=>new(503,"player_profile_unavailable",
            "Private profile service is unavailable.")
    };
}
