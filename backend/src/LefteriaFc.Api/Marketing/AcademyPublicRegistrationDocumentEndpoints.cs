using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Application.Marketing;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Marketing;

/// <summary>
/// One-hour, one-child capability-bound PDF/image upload into the private
/// Academy intake escrow. Never returns plaintext bytes or a file URL.
/// Promotion to Player Documents occurs only on approved roster placement.
/// All uploads remain quarantined; no scan bypass or public download exists.
/// </summary>
public static class AcademyPublicRegistrationDocumentEndpoints
{
    private const long MaxFileBytes=10*1024*1024;
    private const long MaxRequestBytes=11*1024*1024;

    public static IEndpointRouteBuilder MapAcademyPublicRegistrationDocuments(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/public/academy/registration/{registrationId}/documents",
            UploadAsync).WithTags("Academy registration private documents")
            .WithName("UploadPublicAcademyRegistrationDocument")
            .ExcludeFromDescription()
            .Produces(202).Produces(400).Produces(403).Produces(409)
            .Produces(413).Produces(415).Produces(422).Produces(429)
            .Produces(503);
        return app;
    }

    public static async Task<IResult> UploadAsync(
        string registrationId,HttpContext context,IConfiguration config,
        LefteriaDbContext database,CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers.Pragma="no-cache";
        if(config["AcademyRegistration:Enabled"]!="true")
            return Failure(503,"registration_unavailable");
        var origin=context.Request.Headers.Origin;
        if(origin.Count!=1||!AcademyEnquiryOriginPolicy.IsAllowed(
            origin[0],config["AcademyRegistration:AllowedOrigin"]))
            return Failure(403,"origin_denied");
        if(!AcademyEnquiryActivationGate.TryCreateFingerprint(
            config["AcademyRegistration:Enabled"],
            config["AcademyRegistration:FingerprintKey"],
            context.Connection.RemoteIpAddress,out var fingerprint)||
            fingerprint is null)
            return Failure(503,"upload_unavailable");
        if(!Guid.TryParseExact(registrationId,"N",out var registration)||
            registration==Guid.Empty)
            return Failure(400,"invalid_registration");
        var feature=context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if(feature is null||feature.IsReadOnly)
            return Failure(503,"request_limit_unavailable");
        feature.MaxRequestBodySize=MaxRequestBytes;
        if(!context.Request.HasFormContentType)
            return Failure(415,"multipart_required");
        if(context.Request.ContentLength is >MaxRequestBytes)
            return Failure(413,"file_too_large");

        byte[] key;
        try { key=Convert.FromBase64String(config["DocumentVault:KeyBase64"]??""); }
        catch(FormatException) { return Failure(503,"vault_unavailable"); }
        var keyId=config["DocumentVault:KeyId"];
        if(key.Length!=32||string.IsNullOrWhiteSpace(keyId)||
            keyId.Length>120||keyId.Any(char.IsControl))
        {
            CryptographicOperations.ZeroMemory(key);
            return Failure(503,"vault_unavailable");
        }
        try
        {
            var form=await context.Request.ReadFormAsync(cancellationToken);
            if(form.Files.Count!=1||form.Files[0].Name!="file"||
               form.Keys.Any(k=>k is not ("upload_token" or "slot" or "expiry_date"))||
               form["upload_token"].Count!=1||form["slot"].Count!=1||
               form["expiry_date"].Count>1)
                return Failure(422,"invalid_upload_fields");
            var token=form["upload_token"].ToString();
            if(token.Length!=64||
                !System.Text.RegularExpressions.Regex.IsMatch(token,@"^[0-9a-f]{64}$"))
                return Failure(403,"upload_capability_invalid");
            byte[] secret=Convert.FromHexString(token);
            string digest;
            try { digest=Convert.ToHexStringLower(SHA256.HashData(secret)); }
            finally { CryptographicOperations.ZeroMemory(secret); }
            var slot=form["slot"].ToString();
            if(slot is not ("id_front" or "id_back" or "birth_certificate" or "passport_photo"))
                return Failure(422,"invalid_document_slot");
            var expiryValue=form["expiry_date"].ToString();
            DateOnly? expiry=null;
            if(expiryValue!="")
            {
                if(!DateOnly.TryParseExact(expiryValue,"yyyy-MM-dd",
                   CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
                    return Failure(422,"invalid_document_expiry");
                expiry=date;
            }
            if(slot is "id_front" or "id_back" && expiry is null)
                return Failure(422,"document_expiry_required");
            var file=form.Files[0];
            if(file.Length<1||file.Length>MaxFileBytes)
                return Failure(413,"file_too_large");
            await using var bytes=new MemoryStream((int)file.Length);
            await file.CopyToAsync(bytes,cancellationToken);
            if(bytes.Length!=file.Length||bytes.Length>MaxFileBytes)
                return Failure(413,"file_size_changed");
            var plaintext=bytes.ToArray();
            try
            {
                var mime=RecognizeSafeMime(plaintext);
                if(mime is null) return Failure(422,"invalid_file_signature");
                var objectId=Guid.NewGuid();
                var sha=Convert.ToHexStringLower(SHA256.HashData(plaintext));
                var nonce=RandomNumberGenerator.GetBytes(12);
                var tag=new byte[16];
                var ciphertext=new byte[plaintext.Length];
                // This is a *pre-placement* AAD. The immutable promotion
                // ledger records the original registration_id for future
                // decryptors, which must never substitute a different child.
                var aad=Encoding.UTF8.GetBytes(
                    $"lefteria_fc_academy:registration:{registrationId}:{objectId:D}");
                using(var aes=new AesGcm(key,16))
                    aes.Encrypt(nonce,plaintext,ciphertext,tag,aad);
                try
                {
                    var cs=database.Database.GetConnectionString();
                    if(string.IsNullOrWhiteSpace(cs))return Failure(503,"upload_unavailable");
                    await using var connection=new NpgsqlConnection(cs);
                    await connection.OpenAsync(cancellationToken);
                    await using var command=new NpgsqlCommand("""
                        SELECT academy_intake.store_public_registration_document(
                          @registration,@digest,@object,@slot,@expiry,
                          @mime,@size,@key,@sha,@cipher,@nonce,@tag)
                        """,connection);
                    command.CommandTimeout=15;
                    command.Parameters.AddWithValue("registration",NpgsqlDbType.Uuid,registration);
                    command.Parameters.AddWithValue("digest",NpgsqlDbType.Text,digest);
                    command.Parameters.AddWithValue("object",NpgsqlDbType.Uuid,objectId);
                    command.Parameters.AddWithValue("slot",NpgsqlDbType.Text,slot);
                    command.Parameters.Add("expiry",NpgsqlDbType.Date).Value=
                        expiry is null?DBNull.Value:expiry.Value;
                    command.Parameters.AddWithValue("mime",NpgsqlDbType.Text,mime);
                    command.Parameters.AddWithValue("size",file.Length);
                    command.Parameters.AddWithValue("key",NpgsqlDbType.Text,keyId);
                    command.Parameters.AddWithValue("sha",NpgsqlDbType.Text,sha);
                    command.Parameters.AddWithValue("cipher",NpgsqlDbType.Bytea,ciphertext);
                    command.Parameters.AddWithValue("nonce",NpgsqlDbType.Bytea,nonce);
                    command.Parameters.AddWithValue("tag",NpgsqlDbType.Bytea,tag);
                    var status=await command.ExecuteScalarAsync(cancellationToken) as string;
                    if(status is not ("stored" or "replay"))
                        return Failure(503,"upload_unavailable");
                    return Results.Json(new {slot,status="quarantined",
                        replay=status=="replay"},statusCode:202);
                }
                catch(PostgresException e) when(e.SqlState is "42501" or "23505" or "22023")
                {
                    return Failure(e.SqlState=="42501"?403:
                        e.SqlState=="23505"?409:422,"upload_rejected");
                }
                catch(NpgsqlException) { return Failure(503,"upload_unavailable"); }
                catch(TimeoutException) { return Failure(503,"upload_unavailable"); }
                finally { CryptographicOperations.ZeroMemory(ciphertext); }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(bytes.GetBuffer().AsSpan());
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static IResult Failure(int status,string code)=>
        Results.Json(new {code},statusCode:status);

    private static string? RecognizeSafeMime(ReadOnlySpan<byte> data)
    {
        if(data.Length>=5&&data[0]==0x25&&data[1]==0x50&&
           data[2]==0x44&&data[3]==0x46&&data[4]==0x2d)
            return "application/pdf";
        if(data.Length>=8&&data[0]==0x89&&data[1]==0x50&&
           data[2]==0x4e&&data[3]==0x47&&data[4]==0x0d&&
           data[5]==0x0a&&data[6]==0x1a&&data[7]==0x0a)
            return "image/png";
        if(data.Length>=3&&data[0]==0xff&&data[1]==0xd8&&data[2]==0xff)
            return "image/jpeg";
        if(data.Length>=12&&data[0]==0x52&&data[1]==0x49&&
           data[2]==0x46&&data[3]==0x46&&
           data[8]==0x57&&data[9]==0x45&&data[10]==0x42&&data[11]==0x50)
            return "image/webp";
        return null;
    }
}
