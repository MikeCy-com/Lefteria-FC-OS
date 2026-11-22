using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LefteriaFc.Api.WebAdmin;
using LefteriaFc.Application.PublicContent;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Domain.AdminAuth;
using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Api.PublicContent;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsiteBlockLayout(string Width, int MinHeight, int MarginTop, int MarginBottom)
{
    public bool Valid() =>
        Width is "full" or "half" or "third" &&
        MinHeight is 0 or 120 or 240 or 360 or 480 &&
        MarginTop is 0 or 8 or 16 or 24 or 32 or 48 or 64 &&
        MarginBottom is 0 or 8 or 16 or 24 or 32 or 48 or 64;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsitePageLayout(int MaxWidth, int Gap, int SidePadding)
{
    public bool Valid() =>
        MaxWidth is 960 or 1120 or 1280 or 1440 &&
        Gap is 8 or 16 or 24 or 32 or 48 &&
        SidePadding is 12 or 16 or 24 or 32 or 48;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsiteBlock(string Type, string Text, string? Url, WebsiteBlockLayout? Layout = null)
{
    public bool Valid() =>
        (Layout is null || Layout.Valid()) &&
        Type is "heading" or "paragraph" or "image" or "button" &&
        Text is { Length: <= 5000 } &&
        Text.All(c => c >= ' ' && c != '\u007f' || c is '\n' or '\r' or '\t') &&
        (Type switch {
            "image" => WebsitePageContent.SafeUrl(Url, image: true),
            "button" => WebsitePageContent.SafeUrl(Url, image: false),
            _ => Url is null
        });
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsitePageContent(
    string Title, string Summary, string SeoTitle, string SeoDescription,
    string MenuParent, int MenuOrder, IReadOnlyList<WebsiteBlock> Blocks,
    WebsitePageLayout? Layout = null)
{
    public static bool SafeUrl(string? value, bool image) =>
        value is { Length: > 0 and <= 2048 } &&
        !value.Contains('\\') && !value.Contains('"') &&
        (value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith("//", StringComparison.Ordinal) ||
         value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri) &&
        (!uri.IsAbsoluteUri || uri.Scheme == Uri.UriSchemeHttps) &&
        (!image || !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
    public void Validate()
    {
        if (Title is not {Length: > 0 and <= 180} || string.IsNullOrWhiteSpace(Title) ||
            Summary is not {Length: <= 500} ||
            SeoTitle is not {Length: <= 180} || SeoDescription is not {Length: <= 300} ||
            MenuParent is not ("hidden" or "root" or "club" or "team" or "academy" or "matches" or "news" or "events" or "shop" or "contact") ||
            MenuOrder is < 0 or > 1000 || Blocks is null || Blocks.Count is < 1 or > 40 ||
            (Layout is not null && !Layout.Valid()) ||
            Blocks.Any(b => b is null || !b.Valid()) ||
            Blocks.Any(b => b.Type is "heading" or "paragraph" or "button" && string.IsNullOrWhiteSpace(b.Text)) ||
            Blocks.Any(b => b.Type == "image" && string.IsNullOrWhiteSpace(b.Text)))
            throw new WebAdminException(422, "website_page_invalid", "Check page fields and blocks.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsitePageCreate(string Slug, WebsitePageContent Page);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsitePageRevision(long ExpectedVersion, WebsitePageContent Page);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebsitePagePublication(long ExpectedVersion, bool Publish);
public sealed record WebsitePageRecord(
    string OrganizationId, string Slug, long Version, WebsitePageContent Draft,
    WebsitePageContent? Published, DateTimeOffset? PublishedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// First-party, plugin-free website CMS. Editing is ALWAYS a private draft;
/// a fresh manager approval is required to change the public projection.
/// </summary>
public static class WebsitePageEndpoints
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 12
    };

    public static IEndpointRouteBuilder MapWebsitePages(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/website/pages")
            .RequireAuthorization(WebAdminServices.Policy).WithTags("Website administration");
        admin.MapGet("", async (string? organization_id, HttpContext context, CancellationToken ct) =>
            Results.Ok(await Authorized(context, Owner(organization_id), "news:read",
                (db, tx, _, _) => Read(db, tx, Owner(organization_id), ct), ct)))
            .WithName("ListAdminWebsitePages").Produces<IReadOnlyList<WebsitePageRecord>>();
        admin.MapPost("", async (string? organization_id, HttpContext context, CancellationToken ct) =>
        {
            var owner = Owner(organization_id);
            var body = await Parse<WebsitePageCreate>(context, ct);
            Slug(body.Slug); body.Page.Validate(); ValidateHomeHero(owner, body.Slug, body.Page); ValidateAcademySpotlight(owner, body.Slug, body.Page); ValidateCanonicalAboutIntro(body.Slug, body.Page); ValidateCanonicalContactLead(body.Slug, body.Page); ValidateCanonicalAboutHistory(body.Slug, body.Page); ValidateAcademyEditorialLead(owner, body.Slug, body.Page); ValidateShopEditorialLead(owner, body.Slug, body.Page); ValidateNativeEditorialRegions(owner, body.Slug, body.Page); ValidateGlobalDesign(body.Slug, body.Page); ValidateNavigationDesign(owner, body.Slug, body.Page); ValidateMenuPlacement(owner, body.Page);
            return Results.Ok(await Authorized(context, owner, "news:write", async (db, tx, actor, session) =>
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO public_content.website_pages(organization_id,slug,draft_json)
                    VALUES(@org,@slug,@draft::jsonb)
                    """, db, tx);
                command.Parameters.AddWithValue("org", owner);
                command.Parameters.AddWithValue("slug", body.Slug);
                command.Parameters.AddWithValue("draft", JsonSerializer.Serialize(body.Page, Json));
                try { await command.ExecuteNonQueryAsync(ct); }
                catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
                { throw new WebAdminException(409, "website_slug_exists", "This slug is already used."); }
                await Audit(db, tx, actor, session, owner, "admin.website_page.create", ct);
                return (await Read(db, tx, owner, ct)).Single(p => p.Slug == body.Slug);
            }, ct));
        }).WithName("CreateAdminWebsitePage").Accepts<WebsitePageCreate>("application/json").Produces<WebsitePageRecord>();
        admin.MapPut("/{slug}", async (string slug, string? organization_id, HttpContext context, CancellationToken ct) =>
        {
            Slug(slug);
            var owner = Owner(organization_id);
            var body = await Parse<WebsitePageRevision>(context, ct);
            if (body.ExpectedVersion < 1) throw Invalid();
            body.Page.Validate(); ValidateHomeHero(owner, slug, body.Page); ValidateAcademySpotlight(owner, slug, body.Page); ValidateCanonicalAboutIntro(slug, body.Page); ValidateCanonicalContactLead(slug, body.Page); ValidateCanonicalAboutHistory(slug, body.Page); ValidateAcademyEditorialLead(owner, slug, body.Page); ValidateShopEditorialLead(owner, slug, body.Page); ValidateNativeEditorialRegions(owner, slug, body.Page); ValidateGlobalDesign(slug, body.Page); ValidateNavigationDesign(owner, slug, body.Page); ValidateMenuPlacement(owner, body.Page);
            return Results.Ok(await Authorized(context, owner, "news:write", async (db, tx, actor, session) =>
            {
                await using var command = new NpgsqlCommand("""
                    UPDATE public_content.website_pages
                       SET draft_json=@draft::jsonb,version=version+1,updated_at=clock_timestamp()
                     WHERE organization_id=@org AND slug=@slug AND version=@version
                    """, db, tx);
                command.Parameters.AddWithValue("org", owner);
                command.Parameters.AddWithValue("slug", slug);
                command.Parameters.AddWithValue("version", body.ExpectedVersion);
                command.Parameters.AddWithValue("draft", JsonSerializer.Serialize(body.Page, Json));
                if (await command.ExecuteNonQueryAsync(ct) == 0)
                    throw new WebAdminException(409, "website_page_conflict", "Reload the page before saving.");
                await Audit(db, tx, actor, session, owner, "admin.website_page.update", ct);
                return (await Read(db, tx, owner, ct)).Single(p => p.Slug == slug);
            }, ct));
        }).WithName("UpdateAdminWebsitePage").Accepts<WebsitePageRevision>("application/json").Produces<WebsitePageRecord>();
        admin.MapPost("/{slug}/publication", async (string slug, string? organization_id, HttpContext context, CancellationToken ct) =>
        {
            Slug(slug);
            var owner = Owner(organization_id);
            var body = await Parse<WebsitePagePublication>(context, ct);
            if (body.ExpectedVersion < 1) throw Invalid();
            return Results.Ok(await Authorized(context, owner, "news:write", async (db, tx, actor, session) =>
            {
                // Draft editors cannot self-publish. Publication is atomic,
                // versioned, audited, and scoped to the exact organization.
                await using var command = new NpgsqlCommand("""
                    UPDATE public_content.website_pages
                       SET published_json=CASE WHEN @publish THEN draft_json ELSE NULL END,
                           published_at=CASE WHEN @publish THEN clock_timestamp() ELSE NULL END,
                           version=version+1,updated_at=clock_timestamp()
                     WHERE organization_id=@org AND slug=@slug AND version=@version
                    """, db, tx);
                command.Parameters.AddWithValue("org", owner);
                command.Parameters.AddWithValue("slug", slug);
                command.Parameters.AddWithValue("version", body.ExpectedVersion);
                command.Parameters.AddWithValue("publish", body.Publish);
                if (await command.ExecuteNonQueryAsync(ct) == 0)
                    throw new WebAdminException(409, "website_page_conflict", "Reload before publication.");
                await Audit(db, tx, actor, session, owner,
                    body.Publish ? "admin.website_page.publish" : "admin.website_page.unpublish", ct);
                return (await Read(db, tx, owner, ct)).Single(p => p.Slug == slug);
            }, ct, requirePublisher: true));
        }).WithName("PublishAdminWebsitePage").Accepts<WebsitePagePublication>("application/json").Produces<WebsitePageRecord>();

        // Only the fixed SQL definer projection is accessible to the public
        // runtime. It returns neither draft_json nor private version/actor.
        endpoints.MapGet("/api/public/website/pages", async (
            string? organization_id, string? slug, LefteriaDbContext database, CancellationToken ct) =>
        {
            var owner = Owner(organization_id ?? "lefteria_fc");
            if (slug is not null) Slug(slug);
            var json = await database.Database.SqlQueryRaw<string>(
                "SELECT public_content.read_published_website_pages({0},{1})::text AS \"Value\"",
                owner, (object?)slug ?? DBNull.Value).SingleAsync(ct);
            return Results.Content(json, "application/json");
        }).AllowAnonymous().WithTags("Public website").WithName("GetPublishedWebsitePages")
          .Produces(200, contentType: "application/json");
        return endpoints;
    }

    private static async Task<IReadOnlyList<WebsitePageRecord>> Read(
        NpgsqlConnection db, NpgsqlTransaction tx, string owner, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT slug,version,draft_json::text,published_json::text,published_at,updated_at
            FROM public_content.website_pages WHERE organization_id=@org
            ORDER BY updated_at DESC,slug LIMIT 100
            """, db, tx);
        cmd.Parameters.AddWithValue("org", owner);
        var rows = new List<WebsitePageRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new WebsitePageRecord(owner, reader.GetString(0), reader.GetInt64(1),
                JsonSerializer.Deserialize<WebsitePageContent>(reader.GetString(2), Json)!,
                reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<WebsitePageContent>(reader.GetString(3), Json),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return rows;
    }

    internal static async Task<T> Authorized<T>(
        HttpContext context, string org, string capability,
        Func<NpgsqlConnection,NpgsqlTransaction,Guid,Guid,Task<T>> work,
        CancellationToken ct, bool requirePublisher = false)
    {
        var secrets = context.RequestServices.GetRequiredService<IWebAdminSecrets>();
        var token = context.Request.Cookies[WebAdminServices.CookieName] ?? "";
        if (!secrets.IsToken(token)) throw Denied();
        await using var db = await context.RequestServices.GetRequiredService<WebAdminConnections>().OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using (var limits = new NpgsqlCommand(
            "SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='8s';", db, tx))
            await limits.ExecuteNonQueryAsync(ct);
        var digest = secrets.TokenDigest(token);
        try
        {
            await using (var guard = new NpgsqlCommand(
                "SELECT identity.lock_admin_club_authority(@token,@org)", db, tx))
            {
                guard.Parameters.AddWithValue("token", digest);
                guard.Parameters.AddWithValue("org", org);
                await guard.ExecuteNonQueryAsync(ct);
            }
        }
        catch (PostgresException e) when (e.SqlState == "P0001")
        { throw Denied(); }
        await using var permission = new NpgsqlCommand("""
            SELECT a.id,s.id,a.role,a.password_hash,s.credential_digest,
              EXISTS(SELECT 1 FROM identity.admin_organization_grants g
                 WHERE g.admin_account_id=a.id AND g.organization_id=@org)
            FROM identity.admin_sessions s
            JOIN identity.admin_accounts a ON a.id=s.admin_account_id
            WHERE s.token_digest=@token AND s.revoked_at IS NULL
              AND s.expires_at>clock_timestamp() AND a.is_active
              AND NOT a.invite_pending AND NOT a.otp2fa_enabled
              AND a.password_hash IS NOT NULL
            """, db, tx);
        permission.Parameters.AddWithValue("token", digest);
        permission.Parameters.AddWithValue("org", org);
        Guid actor, session;
        await using (var reader = await permission.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw Denied();
            actor = reader.GetGuid(0); session = reader.GetGuid(1);
            var role = reader.GetString(2);
            if (!secrets.SameDigest(reader.GetString(4), secrets.CredentialDigest(reader.GetString(3))))
                throw Denied();
            if (!reader.GetBoolean(5) || !AdminPermissions.Has(role, capability) ||
                requirePublisher && role is not ("super_admin" or "manager"))
                throw new WebAdminException(403, "website_page_forbidden", "This website page is outside your publishing permissions.");
        }
        var value = await work(db, tx, actor, session);
        await tx.CommitAsync(ct);
        return value;
    }

    private static async Task Audit(NpgsqlConnection db, NpgsqlTransaction tx,
        Guid actor, Guid session, string owner, string action, CancellationToken ct)
    {
        await using var audit = new NpgsqlCommand("""
            INSERT INTO audit.security_events(
                id,actor_realm,occurred_at,actor_identity_id,device_session_id,
                action,outcome,organization_id,reason_code)
            VALUES(@id,'admin',clock_timestamp(),@actor,@session,
                @action,'succeeded',@org,'website_page_cms')
            """, db, tx);
        audit.Parameters.AddWithValue("id", Guid.NewGuid());
        audit.Parameters.AddWithValue("actor", actor);
        audit.Parameters.AddWithValue("session", session);
        audit.Parameters.AddWithValue("action", action);
        audit.Parameters.AddWithValue("org", owner);
        await audit.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> Parse<T>(HttpContext context, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 12 }, ct);
            Unique(document.RootElement);
            return document.Deserialize<T>(Json) ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in value.EnumerateObject())
            {
                if (!names.Add(prop.Name)) throw new JsonException("Duplicate property.");
                Unique(prop.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) Unique(element);
    }
    // A public First Team submenu must never become the destination for an
    // Academy-owned CMS page, and Academy pages must never appear under the
    // First Team fixture menu. Verify at write time even for forged requests.
    private static void ValidateMenuPlacement(string owner, WebsitePageContent content)
    {
        if (owner == "lefteria_fc_academy" && content.MenuParent is "team" or "matches" ||
            owner == "lefteria_fc" && content.MenuParent == "academy")
            throw Invalid();
    }

    // Protected native homepage regions can only edit their own owner's copy.
    // Their block order and hidden-menu status are fixed; they never replace
    // the fixture carousel, navigation, roster or other dynamic homepage data.
    private static void ValidateHomeHero(string owner, string slug, WebsitePageContent content)
    {
        var requiredOwner = slug switch {
            "homepage-hero-academy" => "lefteria_fc_academy",
            "homepage-hero-first-team" => "lefteria_fc",
            _ => null
        };
        if (requiredOwner is null) return;
        if (owner != requiredOwner || content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 2 || content.Blocks[0].Type != "heading" ||
            content.Blocks[1].Type != "paragraph" ||
            content.Blocks[0].Text.Length > 180 || content.Blocks[1].Text.Length > 500 ||
            content.Blocks[0].Text.Split('\n').Length > 3)
            throw Invalid();
    }
    // Academy-only editorial slot: the three headline phrases keep the reference
    // design's accented separators, without creating arbitrary site templates.
    private static void ValidateAcademySpotlight(string owner, string slug, WebsitePageContent content)
    {
        if (slug != "homepage-academy-spotlight") return;
        if (owner != "lefteria_fc_academy" || content.MenuParent != "hidden" ||
            content.MenuOrder != 0 || content.Blocks.Count != 2 ||
            content.Blocks[0].Type != "heading" || content.Blocks[1].Type != "paragraph" ||
            content.Blocks[0].Text.Length > 180 || content.Blocks[1].Text.Length > 500 ||
            content.Blocks[0].Text.Contains('\r') || content.Blocks[0].Text.Contains('\n') ||
            content.Blocks[0].Text.Split('·').Length != 3 ||
            content.Blocks[0].Text.Split('·').Any(part => string.IsNullOrWhiteSpace(part) || part.Length > 60))
            throw Invalid();
    }
    // Canonical About-page intro: editorial text only. Layout, crest, values,
    // venue and every dynamic widget remain in the native route. This slot is
    // intentionally hidden from menus and cannot become a new public page.
    private static void ValidateCanonicalAboutIntro(string slug, WebsitePageContent content)
    {
        if (slug != "canonical-about-intro") return;
        if (content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 2 || content.Blocks[0].Type != "heading" ||
            content.Blocks[1].Type != "paragraph" ||
            content.Blocks[0].Url is not null || content.Blocks[1].Url is not null ||
            string.IsNullOrWhiteSpace(content.Blocks[0].Text) ||
            string.IsNullOrWhiteSpace(content.Blocks[1].Text) ||
            content.Blocks[0].Text.Length > 180 || content.Blocks[1].Text.Length > 500 ||
            content.Blocks[0].Text.Contains('\r') || content.Blocks[0].Text.Contains('\n'))
            throw Invalid();
    }
    // Fixed contact introduction only; the published club contact details and
    // mail-client flow remain dynamic and must never be overridden by CMS.
    private static void ValidateCanonicalContactLead(string slug, WebsitePageContent content)
    {
        if (slug != "canonical-contact-lead") return;
        if (content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 1 || content.Blocks[0].Type != "paragraph" ||
            content.Blocks[0].Url is not null ||
            string.IsNullOrWhiteSpace(content.Blocks[0].Text) ||
            content.Blocks[0].Text.Length > 500)
            throw Invalid();
    }
    // Native About history supports heading plus three editorial paragraphs.
    // Crest, venue and other existing native components remain untouched.
    private static void ValidateCanonicalAboutHistory(string slug, WebsitePageContent content)
    {
        if (slug != "canonical-about-history") return;
        if (content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 4 || content.Blocks[0].Type != "heading" ||
            content.Blocks[0].Url is not null ||
            string.IsNullOrWhiteSpace(content.Blocks[0].Text) ||
            content.Blocks[0].Text.Length > 180 ||
            content.Blocks[0].Text.Contains('\r') || content.Blocks[0].Text.Contains('\n') ||
            content.Blocks.Skip(1).Any(block => block.Type != "paragraph" ||
                block.Url is not null || string.IsNullOrWhiteSpace(block.Text) ||
                block.Text.Length > 800))
            throw Invalid();
    }
    // Preserve Academy fixtures, group rosters, registration links and core layout:
    // only the fixed introductory copy is editorial. Other owners are forbidden.
    private static void ValidateAcademyEditorialLead(string owner, string slug, WebsitePageContent content)
    {
        if (slug is not ("canonical-academy-intro" or "canonical-academy-philosophy-lead" or "canonical-academy-groups-lead" or "canonical-academy-registration-lead")) return;
        if (owner != "lefteria_fc_academy" ||
            content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 1 || content.Blocks[0].Type != "paragraph" ||
            content.Blocks[0].Url is not null ||
            string.IsNullOrWhiteSpace(content.Blocks[0].Text) ||
            content.Blocks[0].Text.Length > 500)
            throw Invalid();
    }
    // Shop copy only. No catalog stock, prices, checkout or payment status can
    // be edited via a website block and this slot belongs to First Team.
    private static void ValidateShopEditorialLead(string owner, string slug, WebsitePageContent content)
    {
        if (slug != "canonical-shop-lead") return;
        if (owner != "lefteria_fc" ||
            content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 1 || content.Blocks[0].Type != "paragraph" ||
            content.Blocks[0].Url is not null ||
            string.IsNullOrWhiteSpace(content.Blocks[0].Text) ||
            content.Blocks[0].Text.Length > 500)
            throw Invalid();
    }
    // Dynamic data is never stored as CMS blocks: only additional, explicitly
    // allowlisted editorial regions before native lists/cards are customizable.
    private static void ValidateNativeEditorialRegions(string owner, string slug, WebsitePageContent content)
    {
        var firstTeam = slug is "canonical-team-roster-sections" or
            "canonical-team-schedule-sections" or "canonical-team-results-sections" or
            "canonical-team-statistics-sections";
        var shared = slug is "canonical-news-sections" or "canonical-sponsors-sections";
        if (!firstTeam && !shared) return;
        if (firstTeam && owner != "lefteria_fc" ||
            content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count is < 1 or > 12)
            throw Invalid();
        // Additional sections cannot embed HTML or arbitrary code; blocks use
        // existing URL/length validation and the separately approved publisher.
        if (content.Blocks.Any(block => block.Type is not ("heading" or "paragraph" or "image" or "button")))
            throw Invalid();
    }
    // Published global design is data, not CSS. Require an exact JSON schema and
    // bounded numeric/color tokens. Privileged publication remains unchanged.
    private static void ValidateGlobalDesign(string slug, WebsitePageContent content)
    {
        if (slug != "canonical-global-design") return;
        if (content.MenuParent != "hidden" || content.MenuOrder != 0 ||
            content.Blocks.Count != 1 || content.Blocks[0].Type != "paragraph" ||
            content.Blocks[0].Url is not null || content.Blocks[0].Text.Length > 5000)
            throw Invalid();
        try
        {
            using var json = JsonDocument.Parse(content.Blocks[0].Text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("text", out var styles) || styles.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object ||
                colors.EnumerateObject().Count() != 7 ||
                styles.EnumerateObject().Count() != 12)
                throw Invalid();
            foreach (var name in new[] { "background", "foreground", "accent", "surface", "muted", "border", "link" })
            {
                if (!colors.TryGetProperty(name, out var color) || color.ValueKind != JsonValueKind.String ||
                    !Regex.IsMatch(color.GetString() ?? "", @"\A#[0-9A-Fa-f]{6}\z"))
                    throw Invalid();
            }
            foreach (var name in new[] { "h1", "h2", "h3", "h4", "h5", "h6", "p", "button", "nav", "caption", "label", "card" })
            {
                if (!styles.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object ||
                    t.EnumerateObject().Count() != 5 ||
                    !t.TryGetProperty("family", out var family) ||
                    family.ValueKind != JsonValueKind.String ||
                    family.GetString() is not ("condensed" or "sans" or "serif") ||
                    !t.TryGetProperty("desktop", out var desktop) || !desktop.TryGetInt32(out var d) || d is < 12 or > 80 ||
                    !t.TryGetProperty("mobile", out var mobile) || !mobile.TryGetInt32(out var m) || m is < 12 or > 80 ||
                    !t.TryGetProperty("weight", out var weight) || !weight.TryGetInt32(out var w) ||
                    w is not (400 or 500 or 600 or 700 or 800) ||
                    !t.TryGetProperty("line_height", out var line) ||
                    !line.TryGetDouble(out var h) || h is not (1.0 or 1.15 or 1.2 or 1.35 or 1.5 or 1.6 or 1.8))
                    throw Invalid();
            }
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
    }
    // Custom navigation lives in an approved, organization-scoped CMS snapshot.
    // Operational routes/categories cannot be replaced by this document.
    private static void ValidateNavigationDesign(string owner, string slug, WebsitePageContent page)
    {
        if (slug != "canonical-navigation-design") return;
        if (page.MenuParent != "hidden" || page.MenuOrder != 0 ||
            page.Blocks.Count != 1 || page.Blocks[0].Type != "paragraph" ||
            page.Blocks[0].Url is not null || page.Blocks[0].Text.Length > 5000)
            throw Invalid();
        try
        {
            using var doc = JsonDocument.Parse(page.Blocks[0].Text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array ||
                links.GetArrayLength() > 24) throw Invalid();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in links.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object || link.EnumerateObject().Count() != 5 ||
                    !link.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String ||
                    !link.TryGetProperty("label", out var labelValue) || labelValue.ValueKind != JsonValueKind.String ||
                    !link.TryGetProperty("path", out var pathValue) || pathValue.ValueKind != JsonValueKind.String ||
                    !link.TryGetProperty("parent", out var parentValue) || parentValue.ValueKind != JsonValueKind.String ||
                    !link.TryGetProperty("order", out var orderValue) || !orderValue.TryGetInt32(out var order) ||
                    order is < 0 or > 100) throw Invalid();
                var id = idValue.GetString() ?? "";
                var label = labelValue.GetString() ?? "";
                var path = pathValue.GetString() ?? "";
                var parent = parentValue.GetString() ?? "";
                if (!Regex.IsMatch(id, @"\Acustom-[a-z0-9-]{3,45}\z") || !identifiers.Add(id) ||
                    string.IsNullOrWhiteSpace(label) || label.Length > 80 ||
                    path.Length > 250 ||
                    !Regex.IsMatch(path, @"\A/[a-z0-9-]+(?:/[a-z0-9-]+)*(?:\?[a-z0-9_=&-]+)?\z") ||
                    Regex.IsMatch(path, @"\A/(admin|api|app|login)(?:/|\?|\z)") ||
                    parent is not ("root" or "club" or "team" or "academy" or "matches" or "news" or "events" or "shop" or "contact"))
                    throw Invalid();
                if (owner == "lefteria_fc_academy")
                {
                    if (parent is "team" or "matches" or "shop" ||
                        Regex.IsMatch(path, @"\A/(team|trials|shop)(?:/|\?|\z)") ||
                        Regex.IsMatch(path, @"\A/(news|about|contact)(?:\?|\z)") &&
                        !path.Contains("organization_id=lefteria_fc_academy", StringComparison.Ordinal))
                        throw Invalid();
                }
                else if (parent == "academy" || Regex.IsMatch(path, @"\A/academy(?:/|\?|\z)"))
                    throw Invalid();
            }
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
    }
    private static string Owner(string? org) =>
        org is "lefteria_fc" or "lefteria_fc_academy" ? org :
        throw new WebAdminException(400,"invalid_organization","Choose an organization.");
    private static void Slug(string slug)
    {
        if (slug.Length is < 2 or > 80 || !Regex.IsMatch(slug,
                @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant))
            throw Invalid();
    }
    private static WebAdminException Invalid() =>
        new(422, "website_page_invalid", "Check page fields.");
    private static WebAdminException Denied() =>
        new(401, "admin_session_invalid", "Sign in again.");
}
