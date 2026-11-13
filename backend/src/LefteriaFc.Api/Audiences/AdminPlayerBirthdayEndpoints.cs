using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Audiences;

/// <summary>
/// Private birthday card editor. No public birthday route, automatic posting
/// or implicit guardian consent. SQL revalidates current manager authority.
/// </summary>
public static class AdminPlayerBirthdayEndpoints
{
    public sealed record BirthdayPreferencesCommand(
        int ExpectedVersion,bool Disabled,string WishText,
        string BackgroundColor,string Emoji);

    public static IEndpointRouteBuilder MapAdminPlayerBirthdayPreferences(
        this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/football/players/{playerId}/birthday-preferences")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Private player birthday card preferences");

        group.MapGet("/",async (
            string playerId,string? organization_id,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,playerId,ct);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_player_birthday_preferences(
                  @digest,@owner,@player)::text
                """,connection);
            Bind(command,secrets.TokenDigest(cookie),organization_id!,playerId);
            return await Execute(command,ct);
        }).WithName("ReadAdminPlayerBirthdayPreferences");

        group.MapPut("/",async (
            string playerId,string? organization_id,BirthdayPreferencesCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct) =>
        {
            var cookie=await Authorized(auth,context,organization_id,playerId,ct);
            if(body.ExpectedVersion is <0 or >9999 ||
               body.WishText is null || body.WishText.Length>500 ||
               body.WishText.Any(char.IsControl) ||
               body.BackgroundColor is null ||
               !System.Text.RegularExpressions.Regex.IsMatch(
                   body.BackgroundColor,@"^#[0-9A-Fa-f]{6}$") ||
               body.Emoji is not ("🎂" or "🎉" or "🎈" or "🎁"
                   or "🌟" or "⚽" or "🏆" or "❤️"))
                throw new WebAdminException(422,"invalid_birthday_preferences",
                    "Choose valid private birthday card settings.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_player_birthday_preferences(
                  @digest,@owner,@player,@version,@disabled,@wish,@color,@emoji)::text
                """,connection);
            Bind(command,secrets.TokenDigest(cookie),organization_id!,playerId);
            command.Parameters.AddWithValue("version",body.ExpectedVersion);
            command.Parameters.AddWithValue("disabled",body.Disabled);
            command.Parameters.AddWithValue("wish",body.WishText);
            command.Parameters.AddWithValue("color",body.BackgroundColor);
            command.Parameters.AddWithValue("emoji",body.Emoji);
            return await Execute(command,ct);
        }).WithName("UpdateAdminPlayerBirthdayPreferences")
          .Accepts<BirthdayPreferencesCommand>("application/json");
        return endpoints;
    }

    private static async Task<string> Authorized(
        WebAdminService auth,HttpContext context,string? owner,
        string? player,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(owner is null || !PublicContentService.IsOrganization(owner) ||
           context.Request.Query["organization_id"].Count!=1 ||
           string.IsNullOrWhiteSpace(player)||player.Length>128||
           player.Any(char.IsControl))
            throw new WebAdminException(400,"invalid_birthday_scope",
                "Select exactly one organization and player.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        var capability=owner=="lefteria_fc"?"teams:write":"academy:write";
        if(!AdminPermissions.Has(session.User.Role,capability) ||
           !session.User.Organizations.Any(org=>org.Id==owner))
            throw new WebAdminException(403,"not_authorized","Manager access required.");
        return cookie!;
    }

    private static void Bind(NpgsqlCommand command,string digest,string owner,string player)
    {
        command.Parameters.AddWithValue("digest",digest);
        command.Parameters.AddWithValue("owner",owner);
        command.Parameters.AddWithValue("player",player);
    }

    private static async Task<IResult> Execute(NpgsqlCommand command,CancellationToken ct)
    {
        try
        {
            var value=await command.ExecuteScalarAsync(ct);
            if(value is not string json)throw new WebAdminException(
                503,"birthday_preferences_unavailable","Private birthday settings unavailable.");
            return Results.Content(json,"application/json");
        }
        catch(PostgresException e) when(e.SqlState=="23505")
        {
            throw new WebAdminException(409,"birthday_version_conflict",
                "Birthday preferences changed. Reload the latest version.");
        }
        catch(PostgresException e) when(e.SqlState=="42501")
        {
            throw new WebAdminException(403,"not_authorized","Manager access revoked.");
        }
        catch(PostgresException e) when(e.SqlState=="22023")
        {
            throw new WebAdminException(422,"invalid_birthday_preferences",
                "Invalid birthday card preferences.");
        }
    }
}
