using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LefteriaFc.Application.Roster;

namespace LefteriaFc.Application.AccountImport;

public static partial class AccountImportValidator
{
    private static readonly string[] Collections=["mobile_users","fans","admin_users"];
    private static readonly string[] Fields=["schema_version","source_repository","source_commit",
        "roster_sha256","staff_sha256","exported_at","complete_collections","mobile_users","fans","admin_users"];
    public static ValidatedAccountImport Validate(ReadOnlyMemory<byte> bytes)
    {
        if(bytes.Length is 0 or > RosterImportValidator.MaximumBytes) throw Invalid("input_size");
        try
        {
            var original=bytes.ToArray();
            using var document=JsonDocument.Parse(original,new JsonDocumentOptions{MaxDepth=32});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object) throw Invalid("manifest_object_required");
            CheckDuplicateKeys(root);
            if(root.EnumerateObject().Any(value=>!Fields.Contains(value.Name,StringComparer.Ordinal))) throw Invalid("unknown_manifest_field");
            if(!root.TryGetProperty("schema_version",out var version)||!version.TryGetInt32(out var number)||number!=1) throw Invalid("schema_version");
            if(Text(root,"source_repository")!=ValidatedRosterImport.Repository) throw Invalid("source_repository");
            var commit=Text(root,"source_commit"); var roster=Text(root,"roster_sha256"); var staff=Text(root,"staff_sha256");
            if(!CommitPattern().IsMatch(commit)) throw Invalid("source_commit");
            if(!DigestPattern().IsMatch(roster)||!DigestPattern().IsMatch(staff)) throw Invalid("base_digest");
            var exported=Text(root,"exported_at");
            if(exported.Length>40||!ExportedAtPattern().IsMatch(exported)||!DateTimeOffset.TryParse(exported,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)) throw Invalid("exported_at");
            if(!root.TryGetProperty("complete_collections",out var complete)||complete.ValueKind!=JsonValueKind.Array||
                !complete.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.String?value.GetString():null).SequenceEqual(Collections)) throw Invalid("complete_collections");
            var rows=new List<AccountSourceRow>();
            foreach(var collection in Collections)
            {
                if(!root.TryGetProperty(collection,out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>(collection=="admin_users"?5000:50000)) throw Invalid("collection_shape");
                var ordinal=0;
                foreach(var row in entries.EnumerateArray())
                {
                    if(row.ValueKind!=JsonValueKind.Object) throw Invalid("row_object_required");
                    rows.Add(new(collection,ordinal++,row.GetRawText()));
                }
            }
            at=at.ToUniversalTime();at=at.AddTicks(-(at.Ticks%10));
            return new(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),commit.ToLowerInvariant(),roster.ToLowerInvariant(),staff.ToLowerInvariant(),at,rows.AsReadOnly(),original);
        }
        catch(JsonException){throw Invalid("invalid_json");}
        catch(InvalidOperationException){throw Invalid("invalid_type");}
    }
    private static string Text(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()!:throw Invalid("manifest_string_required");
    private static void CheckDuplicateKeys(JsonElement row)
    {
        if(row.ValueKind==JsonValueKind.Object)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in row.EnumerateObject()) {if(!keys.Add(property.Name)) throw Invalid("duplicate_json_key");CheckDuplicateKeys(property.Value);}
        }
        else if(row.ValueKind==JsonValueKind.Array) foreach(var item in row.EnumerateArray()) CheckDuplicateKeys(item);
    }
    private static RosterImportValidationException Invalid(string code)=>new(new(code,"accounts"));
    [GeneratedRegex("\\A[0-9a-fA-F]{40}\\z",RegexOptions.CultureInvariant)]private static partial Regex CommitPattern();
    [GeneratedRegex("\\A[0-9a-fA-F]{64}\\z",RegexOptions.CultureInvariant)]private static partial Regex DigestPattern();
    [GeneratedRegex("\\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?Z\\z",RegexOptions.CultureInvariant)]private static partial Regex ExportedAtPattern();
}
