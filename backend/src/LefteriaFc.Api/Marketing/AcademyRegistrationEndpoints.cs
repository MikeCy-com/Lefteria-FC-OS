using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Marketing;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Api.Marketing;

/// <summary>
/// Academy-only intake, not enrolment or billing. Fail closed unless the
/// operator enables staging with an approved policy, encryption key and DB grant.
/// </summary>
public static class AcademyRegistrationEndpoints
{
    private const int MaxBytes = 131072;
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IEndpointRouteBuilder MapAcademyRegistration(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/public/academy/registration", SubmitAsync)
            .WithTags("Academy registration")
            .WithName("SubmitPublicAcademyRegistration")
            .Accepts<AcademyRegistrationRequest>("application/json")
            .Produces(202).Produces(400).Produces(403).Produces(413)
            .Produces(422).Produces(429).Produces(503);
        return app;
    }

    public static async Task<IResult> SubmitAsync(
        HttpContext context, IConfiguration config, LefteriaDbContext database,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (config["AcademyRegistration:Enabled"] != "true") return Unavailable();
        var origin = context.Request.Headers.Origin;
        if (origin.Count != 1 || !AcademyEnquiryOriginPolicy.IsAllowed(
            origin[0], config["AcademyRegistration:AllowedOrigin"]))
            return Failure(403,"origin_denied");

        if (!AcademyEnquiryActivationGate.TryCreateFingerprint(
            config["AcademyRegistration:Enabled"],config["AcademyRegistration:FingerprintKey"],
            context.Connection.RemoteIpAddress,out var fingerprint) || fingerprint is null)
            return Unavailable();

        var policy = config["AcademyRegistration:PolicyVersion"];
        var keyId = config["AcademyRegistration:EvidenceKeyId"];
        byte[] key;
        try { key = Convert.FromBase64String(config["AcademyRegistration:EvidenceKey"] ?? ""); }
        catch (FormatException) { return Unavailable(); }
        if (key.Length != 32 || string.IsNullOrWhiteSpace(policy) || policy.Length > 80 ||
            string.IsNullOrWhiteSpace(keyId) || keyId.Length > 120 ||
            !System.Text.RegularExpressions.Regex.IsMatch(keyId, @"^[a-zA-Z0-9_.-]+$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(policy, @"^[a-zA-Z0-9_.-]+$"))
            return Unavailable();

        if (!context.Request.HasJsonContentType()) return Failure(400,"json_required");
        if (context.Request.ContentLength is > MaxBytes) return Failure(413,"payload_too_large");
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (stream.Length + read > MaxBytes) return Failure(413,"payload_too_large");
            stream.Write(buffer,0,read);
        }
        AcademyRegistrationRequest? body;
        try
        {
            using var json = JsonDocument.Parse(stream.ToArray());
            if (json.RootElement.ValueKind != JsonValueKind.Object) return Failure(400,"invalid_json");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in json.RootElement.EnumerateObject())
                if (!names.Add(field.Name)) return Failure(400,"duplicate_field");
            body = json.RootElement.Deserialize<AcademyRegistrationRequest>(StrictJson);
        }
        catch (JsonException) { return Failure(400,"invalid_json"); }
        if (body is null || !Validate(body,out _) || !ValidKit(body)) return Failure(422,"invalid_registration");
        if (body.AdditionalPlayers is { Count: > 4 }) return Failure(422,"too_many_players");
        var children = new List<AcademyRegistrationRequest> { body };
        foreach (var additional in body.AdditionalPlayers ?? [])
            children.Add(ExpandChild(body,additional));
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            if (!Validate(child,out _) || !ValidKit(child) ||
                !identities.Add(child.PlayerFirstName.Trim()+"|"+child.PlayerLastName.Trim()+"|"+child.PlayerDob))
                return Failure(422,"invalid_family_registration");
        }

        // Parent signs once. Each child has their own encrypted private evidence,
        // receipt and pending registration. One DB transaction prevents partial
        // registration if any sibling fails validation or durable throttling.
        var familyId=Guid.NewGuid();
        var receipts=new List<string>(children.Count);
        try
        {
            var cs=database.Database.GetConnectionString();
            if(string.IsNullOrWhiteSpace(cs)) return Unavailable();
            await using var connection=new NpgsqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await using var transaction=await connection.BeginTransactionAsync(cancellationToken);
            foreach(var child in children)
            {
                if (!DateOnly.TryParseExact(child.PlayerDob,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var birth))
                    return Failure(422,"invalid_registration");
                var id=Guid.NewGuid();
                var monthly=children.Count==1?60:100+(children.Count-2)*50;
                byte[] evidence=JsonSerializer.SerializeToUtf8Bytes(new {
                    FamilyId=familyId, FamilyChildCount=children.Count,
                    FamilyMonthlyFeeEur=monthly, RegistrationFeeEur=20,
                    KitFeeEur=140,
                    PrintingFeeEur=10,
                    child.PlayerGender,child.PlayerAddress,child.PlayerCity,child.PlayerPostalCode,
                    child.EmergencyName,child.EmergencyPhone,child.EmergencyRelationship,
                    child.HasAllergies,child.AllergiesDetails,child.HasConditions,child.ConditionsDetails,
                    child.HasMedication,child.MedicationDetails,
                    child.PreferredNumber,child.PreferredPrintName,
                    OfficialKitMandatory=true,PrintingMandatory=true,
                    child.PaymentMethod,child.SignatureData,child.SignatureDate,
                    child.CommEmail,child.CommSms
                });
                if(evidence.Length>60000) {
                    CryptographicOperations.ZeroMemory(evidence);
                    return Failure(413,"payload_too_large");
                }
                var nonce=RandomNumberGenerator.GetBytes(12);
                var ciphertext=new byte[evidence.Length];
                var tag=new byte[16];
                using(var aes=new AesGcm(key,tag.Length))
                    aes.Encrypt(nonce,evidence,ciphertext,tag,id.ToByteArray());
                CryptographicOperations.ZeroMemory(evidence);
                var sealedPayload=new byte[nonce.Length+tag.Length+ciphertext.Length];
                Buffer.BlockCopy(nonce,0,sealedPayload,0,nonce.Length);
                Buffer.BlockCopy(tag,0,sealedPayload,nonce.Length,tag.Length);
                Buffer.BlockCopy(ciphertext,0,sealedPayload,nonce.Length+tag.Length,ciphertext.Length);
                var sha=Convert.ToHexStringLower(SHA256.HashData(sealedPayload));
                var projection=JsonSerializer.Serialize(new {
                    applicant_first_name=child.PlayerFirstName.Trim(),
                    applicant_last_name=child.PlayerLastName.Trim(),
                    applicant_birth_date=birth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                    guardian_name=body.ParentName.Trim(),
                    guardian_relationship=body.ParentRelationship.Trim(),
                    guardian_phone=body.ParentPhone.Trim(),
                    guardian_email=body.ParentEmail.Trim(),
                    consent_participation=body.ConsentParticipation,
                    consent_medical_auth=body.ConsentMedicalAuth,
                    consent_gdpr=body.ConsentGdpr,
                    consent_media=body.ConsentMedia=="yes",
                    consent_communications=body.ConsentCommunications,
                    consent_liability=body.ConsentLiability,
                    consent_financial=body.ConsentFinancial
                });
                await using var command=new NpgsqlCommand("""
                    SELECT academy_intake.submit_public_registration(
                      @id,@projection,@sealed,@sha,@key_id,@fingerprint,@policy)
                    """,connection,transaction);
                command.CommandTimeout=10;
                command.Parameters.AddWithValue("id",NpgsqlDbType.Uuid,id);
                command.Parameters.AddWithValue("projection",NpgsqlDbType.Jsonb,projection);
                command.Parameters.AddWithValue("sealed",NpgsqlDbType.Bytea,sealedPayload);
                command.Parameters.AddWithValue("sha",NpgsqlDbType.Text,sha);
                command.Parameters.AddWithValue("key_id",NpgsqlDbType.Text,keyId);
                command.Parameters.AddWithValue("fingerprint",NpgsqlDbType.Text,fingerprint);
                command.Parameters.AddWithValue("policy",NpgsqlDbType.Text,policy);
                var status=await command.ExecuteScalarAsync(cancellationToken) as string;
                if(status=="rate_limited") return Failure(429,"rate_limited");
                if(status!="stored") return Unavailable();
                receipts.Add(id.ToString("N"));
            }
            await transaction.CommitAsync(cancellationToken);
            return Results.Json(new {receipt=receipts[0],receipts},statusCode:202);
        }
        catch(PostgresException error) when(error.SqlState is "22023" or "23514" or "22007")
        { return Failure(422,"invalid_registration"); }
        catch(NpgsqlException) { return Unavailable(); }
        catch(TimeoutException) { return Unavailable(); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static AcademyRegistrationRequest ExpandChild(
        AcademyRegistrationRequest family,AcademyRegistrationAdditionalPlayer child) => new()
    {
        PlayerFirstName=child.PlayerFirstName,PlayerLastName=child.PlayerLastName,
        PlayerDob=child.PlayerDob,PlayerGender=child.PlayerGender,
        PlayerAddress=child.PlayerAddress,PlayerCity=child.PlayerCity,
        PlayerPostalCode=child.PlayerPostalCode,
        HasAllergies=child.HasAllergies,AllergiesDetails=child.AllergiesDetails,
        HasConditions=child.HasConditions,ConditionsDetails=child.ConditionsDetails,
        HasMedication=child.HasMedication,MedicationDetails=child.MedicationDetails,
        PreferredNumber=child.PreferredNumber,PreferredPrintName=child.PreferredPrintName,
        ParentName=family.ParentName,ParentRelationship=family.ParentRelationship,
        ParentPhone=family.ParentPhone,ParentEmail=family.ParentEmail,
        EmergencyName=family.EmergencyName,EmergencyPhone=family.EmergencyPhone,
        EmergencyRelationship=family.EmergencyRelationship,
        ConsentParticipation=family.ConsentParticipation,ConsentMedicalAuth=family.ConsentMedicalAuth,
        ConsentGdpr=family.ConsentGdpr,ConsentMedia=family.ConsentMedia,
        ConsentCommunications=family.ConsentCommunications,CommEmail=family.CommEmail,
        CommSms=family.CommSms,ConsentLiability=family.ConsentLiability,
        ConsentFinancial=family.ConsentFinancial,PaymentMethod=family.PaymentMethod,
        SignatureData=family.SignatureData,SignatureDate=family.SignatureDate
    };
    private static bool ValidKit(AcademyRegistrationRequest child) =>
        !string.IsNullOrWhiteSpace(child.PreferredPrintName) &&
        child.PreferredPrintName.Length<=32 &&
        !child.PreferredPrintName.Any(char.IsControl) &&
        System.Text.RegularExpressions.Regex.IsMatch(child.PreferredNumber,@"^[0-9]{1,2}$");

    private static bool Validate(AcademyRegistrationRequest b,out DateOnly birth)
    {
        birth = default;
        static bool Text(string? value,int max) => value is {Length:>0} s &&
            s.Length<=max && !string.IsNullOrWhiteSpace(s) && !s.Any(char.IsControl);
        static bool Phone(string? value) => Text(value,32) &&
            System.Text.RegularExpressions.Regex.IsMatch(value!,@"^\+?[0-9 ()-]{7,32}$");
        return
            Text(b.PlayerFirstName,100) && Text(b.PlayerLastName,100) &&
            DateOnly.TryParseExact(b.PlayerDob,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out birth) &&
            birth >= new DateOnly(2000,1,1) && birth < DateOnly.FromDateTime(DateTime.UtcNow) &&
            b.PlayerGender is "male" or "female" &&
            Text(b.PlayerAddress,200) && Text(b.PlayerCity,100) &&
            (string.IsNullOrEmpty(b.PlayerPostalCode) || Text(b.PlayerPostalCode,20)) &&
            Text(b.ParentName,200) && b.ParentRelationship is "Πατέρας" or "Μητέρα" or "Κηδεμόνας" &&
            Phone(b.ParentPhone) && Text(b.ParentEmail,254) &&
            System.Text.RegularExpressions.Regex.IsMatch(b.ParentEmail!,@"^[^\s@]+@[^\s@]+\.[^\s@]+$") &&
            Text(b.EmergencyName,200) && Phone(b.EmergencyPhone) &&
            (string.IsNullOrEmpty(b.EmergencyRelationship) || Text(b.EmergencyRelationship,80)) &&
            (!b.HasAllergies || Text(b.AllergiesDetails,1500)) &&
            (!b.HasConditions || Text(b.ConditionsDetails,1500)) &&
            (!b.HasMedication || Text(b.MedicationDetails,1500)) &&
            b.ConsentParticipation && b.ConsentMedicalAuth && b.ConsentGdpr &&
            b.ConsentLiability && b.ConsentFinancial &&
            b.ConsentMedia is "yes" or "no" &&
            (!b.ConsentCommunications || b.CommEmail || b.CommSms) &&
            b.PaymentMethod is "cash" or "card" or "transfer" &&
            b.SignatureData is { Length: > 150 and < 20000 } signature &&
            signature.StartsWith("data:image/png;base64,",StringComparison.Ordinal) &&
            DateOnly.TryParseExact(b.SignatureDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
    }
    private static IResult Failure(int code,string error) =>
        Results.Json(new {code=error},statusCode:code);
    private static IResult Unavailable() => Failure(503,"registration_unavailable");
}

public sealed class AcademyRegistrationRequest
{
    public List<AcademyRegistrationAdditionalPlayer>? AdditionalPlayers {get;set;}
    public string PreferredNumber {get;set;} = "";
    public string PreferredPrintName {get;set;} = "";
    public string PlayerFirstName {get;set;} = "";
    public string PlayerLastName {get;set;} = "";
    public string PlayerDob {get;set;} = "";
    public string PlayerGender {get;set;} = "";
    public string PlayerAddress {get;set;} = "";
    public string PlayerCity {get;set;} = "";
    public string PlayerPostalCode {get;set;} = "";
    public string ParentName {get;set;} = "";
    public string ParentRelationship {get;set;} = "";
    public string ParentPhone {get;set;} = "";
    public string ParentEmail {get;set;} = "";
    public string EmergencyName {get;set;} = "";
    public string EmergencyPhone {get;set;} = "";
    public string EmergencyRelationship {get;set;} = "";
    public bool HasAllergies {get;set;}
    public string AllergiesDetails {get;set;} = "";
    public bool HasConditions {get;set;}
    public string ConditionsDetails {get;set;} = "";
    public bool HasMedication {get;set;}
    public string MedicationDetails {get;set;} = "";
    public bool ConsentParticipation {get;set;}
    public bool ConsentMedicalAuth {get;set;}
    public bool ConsentGdpr {get;set;}
    public string ConsentMedia {get;set;} = "";
    public bool ConsentCommunications {get;set;}
    public bool CommEmail {get;set;}
    public bool CommSms {get;set;}
    public bool ConsentLiability {get;set;}
    public bool ConsentFinancial {get;set;}
    public string PaymentMethod {get;set;} = "";
    public string SignatureData {get;set;} = "";
    public string SignatureDate {get;set;} = "";
}

public sealed class AcademyRegistrationAdditionalPlayer
{
    public string PlayerFirstName {get;set;} = "";
    public string PlayerLastName {get;set;} = "";
    public string PlayerDob {get;set;} = "";
    public string PlayerGender {get;set;} = "";
    public string PlayerAddress {get;set;} = "";
    public string PlayerCity {get;set;} = "";
    public string PlayerPostalCode {get;set;} = "";
    public bool HasAllergies {get;set;}
    public string AllergiesDetails {get;set;} = "";
    public bool HasConditions {get;set;}
    public string ConditionsDetails {get;set;} = "";
    public bool HasMedication {get;set;}
    public string MedicationDetails {get;set;} = "";
    public string PreferredNumber {get;set;} = "";
    public string PreferredPrintName {get;set;} = "";
}
