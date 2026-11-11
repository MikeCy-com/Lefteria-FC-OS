using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Evaluations;

/// <summary>
/// Private, encrypted document upload to quarantine only; no download or
/// public/mobile delivery. Requires an explicit 256-bit server-side vault key.
/// </summary>
public static class AdminPrivateDocumentUploadEndpoints
{
    private const int MaxDocumentBytes=10*1024*1024;
    private const long MaxRequestBytes=11*1024*1024;
    private static readonly HashSet<string> DocumentTypes=new(StringComparer.Ordinal)
    {
        "id_passport","birth_certificate","photo","registration_form",
        "kop_registration","transfer_certificate","medical_cert",
        "vaccination_record","consent_form","gdpr_consent","insurance",
        "school_confirmation","image_rights","other"
    };
    private static readonly HashSet<string> ExpiringTypes=new(StringComparer.Ordinal)
    {
        "id_passport","kop_registration","medical_cert","insurance","school_confirmation"
    };

    public static IEndpointRouteBuilder MapAdminPrivateDocumentUploads(
        this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/admin/club/players")
            .RequireAuthorization(WebAdminServices.Policy)
            .WithTags("Protected player document quarantine");
        group.MapPost("/{playerId}/documents",async(
            string playerId,string? organization_id,
            WebAdminService admin,WebAdminConnections connections,
            IWebAdminSecrets secrets,IConfiguration configuration,
            HttpContext context,CancellationToken ct)=>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            if(organization_id is not ("lefteria_fc" or "lefteria_fc_academy") ||
                context.Request.Query["organization_id"].Count!=1 ||
                playerId.Length is <1 or >128 || playerId.Any(char.IsControl))
                throw new WebAdminException(400,"document_scope_invalid",
                    "Choose a valid assigned player and organization.");
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await admin.CurrentAsync(cookie,ct);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role is not ("manager" or "super_admin") ||
                !AdminPermissions.Has(session.User.Role,
                    organization_id=="lefteria_fc"?"teams:write":"academy:write") ||
                !session.User.Organizations.Any(o=>o.Id==organization_id))
                throw new WebAdminException(403,"document_upload_denied",
                    "Private player uploads are limited to assigned club management.");

            // Refuse to accept sensitive bytes when the intended vault is not
            // configured. Never save plaintext locally or in a public folder.
            var keyId=configuration["DocumentVault:KeyId"];
            var encodedKey=configuration["DocumentVault:KeyBase64"];
            byte[] key;
            try { key=Convert.FromBase64String(encodedKey??""); }
            catch(FormatException)
            {
                throw new WebAdminException(503,"document_vault_unavailable",
                    "Protected document storage is not configured.");
            }
            if(key.Length!=32 || string.IsNullOrWhiteSpace(keyId) ||
               keyId.Length>120 || keyId.Any(char.IsControl))
            {
                CryptographicOperations.ZeroMemory(key);
                throw new WebAdminException(503,"document_vault_unavailable",
                    "Protected document storage is not configured.");
            }
            try
            {
                var sizeFeature=context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if(sizeFeature is null || sizeFeature.IsReadOnly)
                    throw new WebAdminException(503,"document_request_limit_unavailable",
                        "Private upload size enforcement is unavailable.");
                sizeFeature.MaxRequestBodySize=MaxRequestBytes;
                if(!context.Request.HasFormContentType)
                    throw new WebAdminException(415,"document_multipart_required",
                        "Submit a validated multipart document.");
                if(context.Request.ContentLength is >MaxRequestBytes)
                    throw new WebAdminException(413,"document_too_large",
                        "Documents must be no larger than 10 MB.");
                var form=await context.Request.ReadFormAsync(ct);
                if(form.Files.Count!=1 || form.Files[0].Name!="file" ||
                   form.Keys.Any(k=>k is not ("request_id" or "document_type"
                       or "title" or "issue_date" or "expiry_date")) ||
                   form["request_id"].Count!=1 || form["document_type"].Count!=1 ||
                   form["title"].Count!=1 ||
                   form["issue_date"].Count>1 || form["expiry_date"].Count>1)
                    throw new WebAdminException(422,"document_fields_invalid",
                        "Provide a single protected file and permitted metadata.");
                var type=form["document_type"].ToString();
                var title=form["title"].ToString();
                if(!Guid.TryParse(form["request_id"],out var requestId) ||
                   requestId==Guid.Empty || !DocumentTypes.Contains(type) ||
                   title.Length is <1 or >200 || title!=title.Trim() ||
                   title.Any(char.IsControl))
                    throw new WebAdminException(422,"document_metadata_invalid",
                        "Document title, type or request ID is invalid.");
                var issue=ParseDay(form["issue_date"].ToString());
                var expiry=ParseDay(form["expiry_date"].ToString());
                if((form["issue_date"].Count>0 && form["issue_date"].ToString()!="" && issue is null) ||
                   (form["expiry_date"].Count>0 && form["expiry_date"].ToString()!="" && expiry is null) ||
                   (issue is not null && expiry is not null && expiry<issue) ||
                   (ExpiringTypes.Contains(type) && expiry is null))
                    throw new WebAdminException(422,"document_dates_invalid",
                        "Provide valid issue and expiry dates for this document.");
                var file=form.Files[0];
                if(file.Length is <1 or >MaxDocumentBytes)
                    throw new WebAdminException(413,"document_too_large",
                        "Documents must be no larger than 10 MB.");
                await using var stream=new MemoryStream((int)file.Length);
                await file.CopyToAsync(stream,ct);
                if(stream.Length!=file.Length || stream.Length>MaxDocumentBytes)
                    throw new WebAdminException(413,"document_size_changed",
                        "The uploaded document size is not valid.");
                var plain=stream.ToArray();
                try
                {
                    var detectedMime=RecognizeSafeMime(plain);
                    if(detectedMime is null)
                        throw new WebAdminException(422,"document_type_unsupported",
                            "Upload only valid PDF, JPEG, PNG or WebP files.");
                    var sha=Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant();
                    var objectId=Guid.NewGuid();
                    var nonce=RandomNumberGenerator.GetBytes(12);
                    var tag=new byte[16];
                    var cipher=new byte[plain.Length];
                    var associated=Encoding.UTF8.GetBytes(
                        $"{organization_id}:{playerId}:{objectId:D}");
                    using(var aes=new AesGcm(key,16))
                        aes.Encrypt(nonce,plain,cipher,tag,associated);

                    await using var connection=await connections.OpenAsync(ct);
                    await using var command=new NpgsqlCommand("""
                      SELECT academy_private.quarantine_admin_player_document(
                        @digest,@owner,@player,@request,@type,@title,
                        @issued,@expires,@mime,@size,@sha,@key,@object,
                        @cipher,@nonce,@tag)::text
                      """,connection);
                    command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
                    command.Parameters.AddWithValue("owner",organization_id);
                    command.Parameters.AddWithValue("player",playerId);
                    command.Parameters.AddWithValue("request",requestId);
                    command.Parameters.AddWithValue("type",type);
                    command.Parameters.AddWithValue("title",title);
                    command.Parameters.Add("issued",NpgsqlDbType.Date).Value=
                        issue is null?DBNull.Value:issue.Value;
                    command.Parameters.Add("expires",NpgsqlDbType.Date).Value=
                        expiry is null?DBNull.Value:expiry.Value;
                    command.Parameters.AddWithValue("mime",detectedMime);
                    command.Parameters.AddWithValue("size",file.Length);
                    command.Parameters.AddWithValue("sha",sha);
                    command.Parameters.AddWithValue("key",keyId);
                    command.Parameters.AddWithValue("object",objectId);
                    command.Parameters.AddWithValue("cipher",NpgsqlDbType.Bytea,cipher);
                    command.Parameters.AddWithValue("nonce",NpgsqlDbType.Bytea,nonce);
                    command.Parameters.AddWithValue("tag",NpgsqlDbType.Bytea,tag);
                    try
                    {
                        var result=await command.ExecuteScalarAsync(ct) as string;
                        return result is null?Results.Problem(statusCode:503)
                            :Results.Content(result,"application/json",Encoding.UTF8,200);
                    }
                    catch(PostgresException e) when(e.SqlState is
                        "42501" or "22023" or "23505" or "54000")
                    {
                        var status=e.SqlState switch
                        {
                            "42501"=>403,"22023"=>422,"23505"=>409,_=>413
                        };
                        throw new WebAdminException(status,"document_quarantine_unavailable",
                            "The protected upload could not be safely recorded.");
                    }
                    finally {CryptographicOperations.ZeroMemory(cipher);}
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                    CryptographicOperations.ZeroMemory(stream.GetBuffer().AsSpan());
                }
            }
            finally {CryptographicOperations.ZeroMemory(key);}
        }).WithName("QuarantineAdminPlayerDocument");
        return endpoints;
    }

    private static DateOnly? ParseDay(string value) =>
        value==""?null:DateOnly.TryParseExact(value,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)?day:null;

    private static string? RecognizeSafeMime(ReadOnlySpan<byte> data)
    {
        if(data.Length>=5 && data[0]==0x25 && data[1]==0x50 &&
           data[2]==0x44 && data[3]==0x46 && data[4]==0x2d)
            return "application/pdf";
        if(data.Length>=8 &&
           data[0]==0x89 && data[1]==0x50 && data[2]==0x4e &&
           data[3]==0x47 && data[4]==0x0d && data[5]==0x0a &&
           data[6]==0x1a && data[7]==0x0a)
            return "image/png";
        if(data.Length>=3 && data[0]==0xff && data[1]==0xd8 && data[2]==0xff)
            return "image/jpeg";
        if(data.Length>=12 && data[0]==0x52 && data[1]==0x49 &&
           data[2]==0x46 && data[3]==0x46 &&
           data[8]==0x57 && data[9]==0x45 && data[10]==0x42 && data[11]==0x50)
            return "image/webp";
        return null;
    }
}