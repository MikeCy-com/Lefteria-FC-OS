using System.Text.RegularExpressions;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>
/// An optional, explicitly UNVERIFIED profile contact address. It is never
/// consulted for authentication, recovery, email dispatch, or role grants.
/// </summary>
public sealed record AdminOwnContactEmailCommand(string? Email, string? CurrentPassword)
{
    public override string ToString() => "Own admin unverified contact email (credential redacted)";
}
public static class AdminContactEmailEndpoints
{
    private static readonly Regex ContactFormat=new(
        @"\A[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,63}\z",
        RegexOptions.Compiled|RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static IEndpointRouteBuilder MapAdminContactEmail(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/auth/profile/contact-email",async (
            HttpContext context,WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null || cookie is null || !secrets.IsToken(cookie))
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand(
                "SELECT identity.read_admin_own_contact_email(@digest)",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            string? email;
            try
            {
                var raw=await command.ExecuteScalarAsync(token);
                email=raw is DBNull or null ? null : raw as string ??
                    throw new WebAdminException(503,"contact_unverified_response",
                        "The contact address could not be verified.");
            }
            catch(PostgresException e) when(e.SqlState=="42501")
            {throw new WebAdminException(403,"contact_permission_revoked","Profile access changed.");}
            catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
            {throw new WebAdminException(503,"contact_storage_unavailable",
                "Contact email settings are not enabled.");}
            return Results.Json(new {email,verified=false});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("GetOwnAdminContactEmail")
        .WithTags("Web administration")
        .ExcludeFromDescription();

        endpoints.MapPost("/api/admin/auth/profile/contact-email",async (
            AdminOwnContactEmailCommand request,HttpContext context,
            WebAdminService service,IWebAdminRepository repository,
            IWebAdminPasswordVerifier verifier,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var email=request.Email?.Trim().ToLowerInvariant();
            if(email is null || email.Length is <5 or >254 ||
                !ContactFormat.IsMatch(email) || email.Contains("..",StringComparison.Ordinal) ||
                email.StartsWith(".",StringComparison.Ordinal) ||
                email.Split('@')[0].EndsWith(".",StringComparison.Ordinal) ||
                email.Split('@')[1].Split('.').Any(s=>s.Length<1||s.StartsWith("-",StringComparison.Ordinal)||
                    s.EndsWith("-",StringComparison.Ordinal)))
                throw new WebAdminException(422,"invalid_contact_email",
                    "Enter a valid contact email address.");
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null || cookie is null || !secrets.IsToken(cookie))
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(request.CurrentPassword is null || request.CurrentPassword.Length is <1 or >4096)
                throw new WebAdminException(422,"current_password_required",
                    "Enter your current password.");
            var account=await repository.FindCredentialAsync(session.User.Username,token);
            if(account is null || account.Id!=session.User.Id ||
               !account.IsActive || account.InvitePending || account.Otp2faEnabled ||
               account.PasswordHash is null ||
               !await verifier.VerifyAsync(request.CurrentPassword,account.PasswordHash,token))
                throw new WebAdminException(403,"current_password_invalid",
                    "Current password could not be verified.");
            await using var connection=await connections.OpenAsync(token);
            await using var cmd=new NpgsqlCommand(
                "SELECT identity.write_admin_own_contact_email(@digest,@expected,@value)",
                connection);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            cmd.Parameters.AddWithValue("expected",account.PasswordHash);
            cmd.Parameters.AddWithValue("value",email);
            try
            {
                if(await cmd.ExecuteScalarAsync(token) is not bool saved || !saved)
                    throw new WebAdminException(503,"contact_unconfirmed",
                        "Contact email update could not be confirmed.");
            }
            catch(PostgresException e) when(e.SqlState=="40001")
            {throw new WebAdminException(409,"contact_profile_changed","Account changed. Sign in again.");}
            catch(PostgresException e) when(e.SqlState=="42501")
            {throw new WebAdminException(403,"contact_permission_revoked","Profile access changed.");}
            catch(PostgresException e) when(e.SqlState=="22023")
            {throw new WebAdminException(422,"invalid_contact_email","Invalid contact address.");}
            catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
            {throw new WebAdminException(503,"contact_storage_unavailable",
                "Contact email settings are not enabled.");}
            return Results.Json(new {updated=true,email,verified=false});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("UpdateOwnAdminContactEmail")
        .WithTags("Web administration")
        .Accepts<AdminOwnContactEmailCommand>("application/json")
        .ExcludeFromDescription();
        return endpoints;
    }
}
