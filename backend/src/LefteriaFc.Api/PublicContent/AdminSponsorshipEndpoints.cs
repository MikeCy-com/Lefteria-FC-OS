using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using Npgsql;

namespace LefteriaFc.Api.PublicContent;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SponsorshipDraftChange(
    string Kind, string ItemId, long ExpectedVersion, SponsorshipEditorialCopy Draft);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SponsorshipPublicationChange(
    string Kind, string ItemId, long ExpectedVersion, bool Publish);
public sealed record SponsorshipAdminRecord(
    string OrganizationId, string Kind, string ItemId, long Version,
    SponsorshipEditorialCopy Draft, SponsorshipEditorialCopy? Published,
    DateTimeOffset? PublishedAt, DateTimeOffset? UpdatedAt);

/// <summary>Imported sponsor/partner identity stays immutable. Editors save only
/// private draft copy; a current owner-approved manager must publish it.</summary>
public static class AdminSponsorshipEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 12
    };

    public static IEndpointRouteBuilder MapAdminSponsorships(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/website/sponsorships")
            .RequireAuthorization(WebAdminServices.Policy).WithTags("Website administration");

        group.MapGet("", async (string? organization_id, IPublicSectionsRepository publicSections,
            HttpContext context, CancellationToken ct) =>
        {
            var owner = Owner(organization_id);
            return Results.Ok(await WebsitePageEndpoints.Authorized(context, owner, "sponsors:read",
                async (db,tx,_,_) => await Read(db,tx,owner,publicSections,ct), ct));
        }).WithName("ListAdminSponsorshipEditorial")
          .Produces<IReadOnlyList<SponsorshipAdminRecord>>();

        group.MapPost("/draft", async (string? organization_id, HttpContext context, CancellationToken ct) =>
        {
            var owner = Owner(organization_id);
            var change = await Parse<SponsorshipDraftChange>(context,ct);
            ValidateItem(change.Kind,change.ItemId);
            if(change.ExpectedVersion < 0 || change.Draft is null || !change.Draft.Valid()) throw Invalid();
            return Results.Ok(await WebsitePageEndpoints.Authorized(context,owner,"sponsors:write",
                async (db,tx,actor,session) =>
                {
                    await RequireImported(db,tx,owner,change.Kind,change.ItemId,ct);
                    var json = JsonSerializer.Serialize(change.Draft,Json);
                    var inserted = 0;
                    if(change.ExpectedVersion == 0)
                    {
                        await using var create = new NpgsqlCommand("""
                            INSERT INTO public_content.sponsorship_editorial
                              (organization_id,kind,item_id,draft_json)
                            VALUES(@org,@kind,@id,@draft::jsonb)
                            ON CONFLICT DO NOTHING
                            """,db,tx);
                        Bind(create,owner,change.Kind,change.ItemId);
                        create.Parameters.AddWithValue("draft",json);
                        inserted = await create.ExecuteNonQueryAsync(ct);
                    }
                    else
                    {
                        await using var update = new NpgsqlCommand("""
                            UPDATE public_content.sponsorship_editorial
                            SET draft_json=@draft::jsonb,version=version+1,
                                updated_at=clock_timestamp()
                            WHERE organization_id=@org AND kind=@kind
                                AND item_id=@id AND version=@expected
                            """,db,tx);
                        Bind(update,owner,change.Kind,change.ItemId);
                        update.Parameters.AddWithValue("draft",json);
                        update.Parameters.AddWithValue("expected",change.ExpectedVersion);
                        inserted = await update.ExecuteNonQueryAsync(ct);
                    }
                    if(inserted != 1) throw Conflict();
                    await Audit(db,tx,owner,actor,session,"admin.sponsorship.draft",ct);
                    return await ReadSaved(db,tx,owner,change.Kind,change.ItemId,ct);
                },ct));
        }).WithName("SaveAdminSponsorshipDraft").Accepts<SponsorshipDraftChange>("application/json")
          .Produces<SponsorshipAdminRecord>();

        group.MapPost("/publication", async (string? organization_id, HttpContext context, CancellationToken ct) =>
        {
            var owner = Owner(organization_id);
            var change = await Parse<SponsorshipPublicationChange>(context,ct);
            ValidateItem(change.Kind,change.ItemId);
            if(change.ExpectedVersion < 1) throw Invalid();
            return Results.Ok(await WebsitePageEndpoints.Authorized(context,owner,"sponsors:write",
                async (db,tx,actor,session) =>
                {
                    await RequireImported(db,tx,owner,change.Kind,change.ItemId,ct);
                    await using var cmd = new NpgsqlCommand("""
                        UPDATE public_content.sponsorship_editorial
                          SET published_json=CASE WHEN @publish THEN draft_json ELSE NULL END,
                              published_at=CASE WHEN @publish THEN clock_timestamp() ELSE NULL END,
                              version=version+1,updated_at=clock_timestamp()
                        WHERE organization_id=@org AND kind=@kind
                            AND item_id=@id AND version=@expected
                        """,db,tx);
                    Bind(cmd,owner,change.Kind,change.ItemId);
                    cmd.Parameters.AddWithValue("expected",change.ExpectedVersion);
                    cmd.Parameters.AddWithValue("publish",change.Publish);
                    if(await cmd.ExecuteNonQueryAsync(ct)!=1) throw Conflict();
                    await Audit(db,tx,owner,actor,session,
                        change.Publish?"admin.sponsorship.publish":"admin.sponsorship.unpublish",ct);
                    return await ReadSaved(db,tx,owner,change.Kind,change.ItemId,ct);
                },ct,requirePublisher:true));
        }).WithName("PublishAdminSponsorshipEditorial").Accepts<SponsorshipPublicationChange>("application/json")
          .Produces<SponsorshipAdminRecord>();
        return endpoints;
    }

    private static async Task<List<SponsorshipAdminRecord>> Read(NpgsqlConnection db,NpgsqlTransaction tx,
        string owner,IPublicSectionsRepository source,CancellationToken ct)
    {
        var published = await source.ReadAsync(owner,ct) ??
            throw new WebAdminException(503,"sponsorship_source_unavailable","Published sponsors are not initialized.");
        if(published.OrganizationId!=owner) throw new WebAdminException(503,"sponsorship_source_invalid","Organization mismatch.");
        var existing=new Dictionary<(string,string),SponsorshipAdminRecord>();
        await using(var cmd=new NpgsqlCommand("""
            SELECT kind,item_id,version,draft_json::text,published_json::text,published_at,updated_at
            FROM public_content.sponsorship_editorial WHERE organization_id=@org
            ORDER BY kind,item_id LIMIT 400
            """,db,tx))
        {
            cmd.Parameters.AddWithValue("org",owner);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                var state=Saved(owner,reader);
                existing[(state.Kind,state.ItemId)]=state;
            }
        }
        var results=new List<SponsorshipAdminRecord>();
        foreach(var sponsor in published.Sponsors.Where(s=>s.OrganizationId==owner))
        {
            var original=new SponsorshipEditorialCopy(sponsor.Name,sponsor.Description,
                sponsor.LogoUrl,sponsor.Website,sponsor.DisplayOrder);
            results.Add(existing.TryGetValue(("sponsor",sponsor.Id),out var state)?state:
                new(owner,"sponsor",sponsor.Id,0,original,null,null,null));
        }
        foreach(var partner in published.Partners.Where(p=>p.OrganizationId==owner))
        {
            var original=new SponsorshipEditorialCopy(partner.Name,partner.Tagline,
                partner.HeroImageUrl,partner.WebsiteUrl,partner.DisplayOrder);
            results.Add(existing.TryGetValue(("partner",partner.Id),out var state)?state:
                new(owner,"partner",partner.Id,0,original,null,null,null));
        }
        return results.OrderBy(s=>s.Kind,StringComparer.Ordinal).ThenBy(s=>s.Draft.DisplayOrder)
            .ThenBy(s=>s.Draft.Name,StringComparer.Ordinal).Take(400).ToList();
    }

    private static SponsorshipAdminRecord Saved(string owner,NpgsqlDataReader reader) =>
        new(owner,reader.GetString(0),reader.GetString(1),reader.GetInt64(2),
            JsonSerializer.Deserialize<SponsorshipEditorialCopy>(reader.GetString(3),Json)!,
            reader.IsDBNull(4)?null:JsonSerializer.Deserialize<SponsorshipEditorialCopy>(reader.GetString(4),Json),
            reader.IsDBNull(5)?null:reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6));

    private static async Task<SponsorshipAdminRecord> ReadSaved(
        NpgsqlConnection db,NpgsqlTransaction tx,string owner,string kind,string id,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("""
            SELECT kind,item_id,version,draft_json::text,published_json::text,published_at,updated_at
            FROM public_content.sponsorship_editorial
            WHERE organization_id=@org AND kind=@kind AND item_id=@id
            """,db,tx);
        Bind(cmd,owner,kind,id);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw Conflict();
        return Saved(owner,reader);
    }

    private static async Task RequireImported(NpgsqlConnection db,NpgsqlTransaction tx,
        string org,string kind,string id,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand(
            "SELECT public_content.current_sponsorship_exists(@org,@kind,@id)",db,tx);
        Bind(cmd,org,kind,id);
        if(await cmd.ExecuteScalarAsync(ct) is not true)
            throw new WebAdminException(404,"sponsorship_not_found","Only currently published imported records can be edited.");
    }
    private static void Bind(NpgsqlCommand cmd,string org,string kind,string id)
    {
        cmd.Parameters.AddWithValue("org",org);
        cmd.Parameters.AddWithValue("kind",kind);
        cmd.Parameters.AddWithValue("id",id);
    }
    private static async Task Audit(NpgsqlConnection db,NpgsqlTransaction tx,
        string org,Guid actor,Guid session,string action,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,device_session_id,
              action,outcome,organization_id,reason_code)
            VALUES(@id,'admin',clock_timestamp(),@actor,@session,
              @action,'succeeded',@org,'sponsorship_editorial')
            """,db,tx);
        command.Parameters.AddWithValue("id",Guid.NewGuid());
        command.Parameters.AddWithValue("actor",actor);
        command.Parameters.AddWithValue("session",session);
        command.Parameters.AddWithValue("action",action);
        command.Parameters.AddWithValue("org",org);
        await command.ExecuteNonQueryAsync(ct);
    }
    private static async Task<T> Parse<T>(HttpContext context,CancellationToken ct)
    {
        try
        {
            using var json=await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions{MaxDepth=12},ct);
            Unique(json.RootElement);
            return json.Deserialize<T>(Json)??throw Invalid();
        }
        catch(JsonException){throw Invalid();}
    }
    private static void Unique(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var prop in element.EnumerateObject())
            {
                if(!keys.Add(prop.Name))throw new JsonException("Duplicate field");
                Unique(prop.Value);
            }
        }
        else if(element.ValueKind==JsonValueKind.Array)
            foreach(var child in element.EnumerateArray())Unique(child);
    }
    private static string Owner(string? org) =>
        org is "lefteria_fc" or "lefteria_fc_academy"?org:
        throw new WebAdminException(400,"invalid_organization","Choose a known organization.");
    private static void ValidateItem(string kind,string id)
    {
        if(kind is not ("sponsor" or "partner")||id is not {Length:>0 and <=128}||
            id.Any(char.IsControl))throw Invalid();
    }
    private static WebAdminException Invalid()=>new(422,"sponsorship_invalid","Check sponsor and partner fields.");
    private static WebAdminException Conflict()=>new(409,"sponsorship_conflict","Reload before saving this record.");
}
