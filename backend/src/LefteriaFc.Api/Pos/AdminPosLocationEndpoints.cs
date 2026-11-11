using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.Pos;

/// <summary>
/// Verified management-only city and location directory. Location creation
/// does not open a register, authorize an operator, price a sale or move stock.
/// </summary>
public static class AdminPosLocationEndpoints
{
    public sealed record CreateCityCommand(string? Name);
    public sealed record CreateLocationCommand(string? CityId,string? Name,string? Code);
    public sealed record AssignProductCommand(bool Enabled,long ExpectedVersion);
    public sealed record ReceiveStockCommand(
        string? ProductId,string? VariantId,int Quantity,Guid RequestId);

    public static IEndpointRouteBuilder MapAdminPosLocations(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/pos")
            .RequireAuthorization(WebAdminServices.Policy).WithTags("POS locations");

        group.MapGet("/locations",async(
            string? organization_id, WebAdminService auth,
            WebAdminConnections connections, IWebAdminSecrets secrets,
            HttpContext context, CancellationToken ct)=>
        {
            var cookie=await Authorize(auth,organization_id,context,false,ct);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT pos.read_admin_locations(@digest,@owner)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            return await Run(command,ct);
        }).WithName("ReadAdminPosLocations");

        group.MapPost("/cities",async(
            string? organization_id,CreateCityCommand body,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>
        {
            var cookie=await Authorize(auth,organization_id,context,true,ct);
            ValidateName(body.Name);
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand(
                "SELECT pos.create_admin_city(@digest,@owner,@name)::text",connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("name",body.Name!);
            return await Run(command,ct);
        }).WithName("CreateAdminPosCity")
          .Accepts<CreateCityCommand>("application/json");

        group.MapPost("/locations",async(
            string? organization_id,CreateLocationCommand body,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>
        {
            var cookie=await Authorize(auth,organization_id,context,true,ct);
            ValidateName(body.Name);
            if(body.CityId is null || !System.Text.RegularExpressions.Regex.IsMatch(
                 body.CityId,@"\A[A-Za-z0-9_-]{1,80}\z") ||
               body.Code is null || !System.Text.RegularExpressions.Regex.IsMatch(
                 body.Code,@"\A[a-z0-9][a-z0-9-]{1,63}\z"))
                throw new WebAdminException(422,"pos_location_invalid",
                    "Choose a city and valid location code.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT pos.create_admin_location(
                  @digest,@owner,@city,@name,@code)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("city",body.CityId);
            command.Parameters.AddWithValue("name",body.Name!);
            command.Parameters.AddWithValue("code",body.Code);
            return await Run(command,ct);
        }).WithName("CreateAdminPosLocation")
          .Accepts<CreateLocationCommand>("application/json");
        group.MapPut("/locations/{locationId}/products/{productId}",async(
            string locationId,string productId,string? organization_id,
            AssignProductCommand body,WebAdminService auth,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            HttpContext context,CancellationToken ct)=>
        {
            var cookie=await Authorize(auth,organization_id,context,true,ct);
            ValidateIdentity(locationId,80);
            ValidateIdentity(productId,80);
            if(body.ExpectedVersion is < -1 or >9007199254740991L)
                throw new WebAdminException(422,"pos_product_assignment_invalid",
                    "Provide a valid product assignment version.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT pos.write_admin_location_product(
                  @digest,@owner,@location,@product,@enabled,@expected)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("location",locationId);
            command.Parameters.AddWithValue("product",productId);
            command.Parameters.AddWithValue("enabled",body.Enabled);
            command.Parameters.AddWithValue("expected",body.ExpectedVersion);
            return await Run(command,ct);
        }).WithName("AssignAdminPosLocationProduct")
          .Accepts<AssignProductCommand>("application/json");

        group.MapPost("/locations/{locationId}/stock",async(
            string locationId,string? organization_id,ReceiveStockCommand body,
            WebAdminService auth,WebAdminConnections connections,
            IWebAdminSecrets secrets,HttpContext context,CancellationToken ct)=>
        {
            var cookie=await Authorize(auth,organization_id,context,true,ct);
            ValidateIdentity(locationId,80);
            ValidateIdentity(body.ProductId,80);
            ValidateIdentity(body.VariantId,60);
            if(body.Quantity is <1 or >100000 || body.RequestId==Guid.Empty)
                throw new WebAdminException(422,"pos_stock_receipt_invalid",
                    "Use valid quantity and unique receiving request.");
            await using var connection=await connections.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT pos.receive_admin_stock(
                  @digest,@owner,@location,@product,@variant,@quantity,@request)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",organization_id!);
            command.Parameters.AddWithValue("location",locationId);
            command.Parameters.AddWithValue("product",body.ProductId!);
            command.Parameters.AddWithValue("variant",body.VariantId!);
            command.Parameters.AddWithValue("quantity",body.Quantity);
            command.Parameters.AddWithValue("request",body.RequestId);
            return await Run(command,ct);
        }).WithName("ReceiveAdminPosStock")
          .Accepts<ReceiveStockCommand>("application/json");

        return endpoints;
    }

    private static void ValidateIdentity(string? id,int maxLength)
    {
        if(id is null || id.Length is <1 or >80 || id.Length>maxLength ||
           !System.Text.RegularExpressions.Regex.IsMatch(
             id,@"\A[A-Za-z0-9_-]+\z"))
            throw new WebAdminException(422,"pos_identity_invalid",
                "Select a valid location, product or variant.");
    }
    private static void ValidateName(string? name)
    {
        if(string.IsNullOrWhiteSpace(name) || name.Length>100 ||
           name!=name.Trim() || name.Any(char.IsControl))
            throw new WebAdminException(422,"pos_name_invalid",
                "Enter a valid location or city name.");
    }
    private static async Task<string> Authorize(
        WebAdminService auth,string? owner,HttpContext context,
        bool write,CancellationToken ct)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
           context.Request.Query["organization_id"].Count!=1)
            throw new WebAdminException(400,"pos_location_owner_invalid",
                "Select one exact organization.");
        var cookie=context.Request.Cookies[WebAdminServices.CookieName];
        var session=await auth.CurrentAsync(cookie,ct);
        if(session is null)
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(session.User.Role is not ("manager" or "super_admin") ||
           !AdminPermissions.Has(session.User.Role,write?"shop:write":"shop:read") ||
           !session.User.Organizations.Any(x=>x.Id==owner))
            throw new WebAdminException(403,"pos_location_denied",
                "Only authorized club management may access POS locations.");
        return cookie!;
    }
    private static async Task<IResult> Run(NpgsqlCommand command,CancellationToken ct)
    {
        try
        {
            var json=await command.ExecuteScalarAsync(ct) as string;
            return json is null?Results.Problem(statusCode:503)
                :Results.Content(json,"application/json",Encoding.UTF8,200);
        }
        catch(PostgresException e) when(e.SqlState is
            "42501" or "22023" or "23505" or "54000")
        {
            var code=e.SqlState switch
            {
                "42501"=>403,"22023"=>422,"23505"=>409,_=>413
            };
            throw new WebAdminException(code,"pos_location_operation_unavailable",
                "The location record or current permissions have changed.");
        }
    }
}