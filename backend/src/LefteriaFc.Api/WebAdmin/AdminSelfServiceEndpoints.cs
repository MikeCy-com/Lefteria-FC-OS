using System.Text;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Verified admin-only changes to the actor's own account. No
/// caller-supplied account ID, role, owner, email or media URL is accepted.</summary>
public sealed record AdminOwnNameCommand(string? Name, string? CurrentPassword)
{
    public override string ToString() => "Own admin display name update (credential redacted)";
}
public sealed record AdminOwnPasswordCommand(string? CurrentPassword, string? NextPassword)
{
    public override string ToString() => "Own admin credential rotation (credentials redacted)";
}

public static class AdminSelfServiceEndpoints
{
    private static readonly SemaphoreSlim HashQueue = new(2,2);
    public static IEndpointRouteBuilder MapAdminSelfService(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/auth/profile/name",async (
            AdminOwnNameCommand command, HttpContext context,
            WebAdminService service,IWebAdminRepository repository,
            IWebAdminPasswordVerifier verifier,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            var name=command.Name;
            if(name is null || name.Length is <1 or >200 ||
               name!=name.Trim() || name.Any(char.IsControl) ||
               new UTF8Encoding(false,true).GetByteCount(name)>700)
                throw new WebAdminException(422,"invalid_profile_name",
                    "Enter a valid name of at most 200 characters.");
            var (cookie,credential)=await VerifyCurrentAsync(context,service,repository,verifier,
                command.CurrentPassword,token);
            var changed=await WriteAsync(connections,secrets,cookie,credential.PasswordHash!,
                "SELECT identity.write_admin_own_name(@digest,@expected,@value)",
                name,token);
            if(!changed)
                throw new WebAdminException(503,"profile_unconfirmed",
                    "The account name update could not be confirmed.");
            return Results.Json(new {updated=true,name});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("UpdateOwnAdminDisplayName")
        .WithTags("Web administration")
        .Accepts<AdminOwnNameCommand>("application/json")
        .ExcludeFromDescription();

        endpoints.MapPost("/api/admin/auth/profile/password",async (
            AdminOwnPasswordCommand command,HttpContext context,
            WebAdminService service,IWebAdminRepository repository,
            IWebAdminPasswordVerifier verifier,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            var next=command.NextPassword;
            int bytes;
            try {bytes=next is null?0:new UTF8Encoding(false,true).GetByteCount(next);}
            catch(EncoderFallbackException){bytes=0;}
            if(next is null || next.Length is <12 or >72 ||
                bytes<12 || bytes>72 || next.Contains('\0') ||
                next==command.CurrentPassword)
                throw new WebAdminException(422,"invalid_next_password",
                    "Choose a different password of 12 to 72 UTF-8 bytes.");
            var (cookie,credential)=await VerifyCurrentAsync(context,service,repository,verifier,
                command.CurrentPassword,token);
            if(!await HashQueue.WaitAsync(0,token))
                throw new WebAdminException(503,"profile_password_busy",
                    "Credential updates are busy.");
            string hash;
            try
            {
                hash=await Task.Run(()=>BCrypt.Net.BCrypt.HashPassword(next,workFactor:12),
                    CancellationToken.None);
            }
            finally{HashQueue.Release();}
            token.ThrowIfCancellationRequested();
            var changed=await WriteAsync(connections,secrets,cookie,credential.PasswordHash!,
                "SELECT identity.write_admin_own_password(@digest,@expected,@value)",
                hash,token);
            if(!changed)
                throw new WebAdminException(503,"password_unconfirmed",
                    "Password update could not be confirmed.");
            // All web-admin sessions are now revoked by the atomically audited
            // database function. The client must sign in again.
            return Results.NoContent();
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("RotateOwnAdminPassword")
        .WithTags("Web administration")
        .Accepts<AdminOwnPasswordCommand>("application/json")
        .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<(string Cookie,WebAdminCredential Credential)> VerifyCurrentAsync(
        HttpContext context,WebAdminService service,
        IWebAdminRepository repository,IWebAdminPasswordVerifier verifier,
        string? password,CancellationToken token)
    {
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await service.CurrentAsync(cookie,token);
        if(session is null || cookie is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(password is null || password.Length is <1 or >4096)
            throw new WebAdminException(422,"current_password_required",
                "Enter your current password.");
        var credential=await repository.FindCredentialAsync(session.User.Username,token);
        if(credential is null || credential.Id!=session.User.Id ||
            !credential.IsActive || credential.InvitePending ||
            credential.Otp2faEnabled || credential.PasswordHash is null ||
            !await verifier.VerifyAsync(password,credential.PasswordHash,token))
            throw new WebAdminException(403,"current_password_invalid",
                "Current password could not be verified.");
        return (cookie,credential);
    }

    private static async Task<bool> WriteAsync(WebAdminConnections connections,
        IWebAdminSecrets secrets,string cookie,string expected,string statement,
        string value,CancellationToken token)
    {
        await using var connection=await connections.OpenAsync(token);
        await using var command=new NpgsqlCommand(statement,connection);
        command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
        command.Parameters.AddWithValue("expected",expected);
        command.Parameters.AddWithValue("value",value);
        try
        {
            return await command.ExecuteScalarAsync(token) is bool success && success;
        }
        catch(PostgresException e) when(e.SqlState=="40001")
        {
            throw new WebAdminException(409,"profile_concurrent_update",
                "Account credentials changed. Sign in again.");
        }
        catch(PostgresException e) when(e.SqlState=="42501")
        {
            throw new WebAdminException(403,"profile_permission_revoked",
                "The account session is no longer authorized.");
        }
        catch(PostgresException e) when(e.SqlState=="22023")
        {
            throw new WebAdminException(422,"invalid_profile_change",
                "The profile change is invalid.");
        }
        catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
        {
            throw new WebAdminException(503,"profile_update_unavailable",
                "Account updates are unavailable in this environment.");
        }
    }
}
