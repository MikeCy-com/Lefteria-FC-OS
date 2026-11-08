using System.Text.Json;
using System.Text.Json.Serialization;
using LefteriaFc.Application.Audiences;
using LefteriaFc.Application.WebAdmin;
using LefteriaFc.Infrastructure.WebAdmin;
using Npgsql;

namespace LefteriaFc.Infrastructure.Audiences;

public sealed class PostgresAdminAnnouncementDraftStore(
    WebAdminConnections connections,IWebAdminSecrets secrets):IAdminAnnouncementDraftStore
{
    private static readonly JsonSerializerOptions Json=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow
    };
    private static void Verify(string cookie,string owner,IWebAdminSecrets secrets)
    {
        if(!secrets.IsToken(cookie))
            throw new WebAdminException(401,"not_authenticated","Sign in again.");
        if(owner is not ("lefteria_fc" or "lefteria_fc_academy"))
            throw new WebAdminException(400,"invalid_organization","Select an organization.");
    }
    public async Task<AdminAnnouncementDraftDto> CreateAsync(
        string cookie,string owner,AdminAnnouncementDraftCommand request,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        static bool ValidId(string? value) =>
            value is {Length:>=1 and <=128} &&
            value.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        if(!ValidId(request.GroupId) ||
            request.Title is not {Length:>=1 and <=140} ||
            request.Title!=request.Title.Trim() ||
            request.Title.Any(char.IsControl) ||
            request.Body is not {Length:>=1 and <=4000} ||
            request.Body!=request.Body.Trim() ||
            request.Body.Any(c=>c=='\0'))
            throw new WebAdminException(422,"invalid_announcement_draft","Invalid announcement draft.");
        var id="ann_"+Guid.NewGuid().ToString("N");
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.create_admin_announcement_draft(
                  @digest,@owner,@id,@group,@title,@body)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("id",id);
            command.Parameters.AddWithValue("group",request.GroupId!);
            command.Parameters.AddWithValue("title",request.Title!);
            command.Parameters.AddWithValue("body",request.Body!);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null?null:JsonSerializer.Deserialize<AdminAnnouncementDraftDto>(raw,Json);
            if(result is null || result.Id!=id || result.OrganizationId!=owner ||
                result.GroupId!=request.GroupId || result.Title!=request.Title ||
                result.Body!=request.Body || result.Status!="draft" || result.Version!=1)
                throw new WebAdminException(503,"announcement_draft_unavailable","Draft verification failed.");
            return result;
        }
        catch(PostgresException e){throw Translate(e);}
    }
    public async Task<AdminAnnouncementDraftsDto> ReadAsync(
        string cookie,string owner,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_announcement_drafts(@digest,@owner)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null?null:JsonSerializer.Deserialize<AdminAnnouncementDraftsDto>(raw,Json);
            if(result is null || result.OrganizationId!=owner || result.Drafts is null ||
                result.Drafts.Count>100 || result.Drafts.Any(d=>
                    d.OrganizationId!=owner || d.Status!="draft" || d.Version<1 ||
                    d.Id is null or {Length:0 or >128} ||
                    d.GroupId is null or {Length:0 or >128} ||
                    d.Title is null or {Length:0 or >140} ||
                    d.Body is null or {Length:0 or >4000}))
                throw new WebAdminException(503,"announcement_draft_unavailable","Draft list verification failed.");
            return result;
        }
        catch(PostgresException e){throw Translate(e);}
    }
    public async Task<AdminAppAnnouncementDto> PublishAsync(
        string cookie,string owner,string id,long version,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        if(!ValidAnnouncementId(id) || version<1)throw InvalidPublication();
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.publish_admin_app_announcement(
                  @digest,@owner,@id,@version)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("id",id);
            command.Parameters.AddWithValue("version",version);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var item=raw is null?null:JsonSerializer.Deserialize<AdminAppAnnouncementDto>(raw,Json);
            if(item is null || item.Id!=id || item.OrganizationId!=owner ||
               item.Status!="published" || item.Version<1 ||
               item.Title is null or {Length:0 or >140} ||
               item.Body is null or {Length:0 or >4000})
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Publish verification failed.");
            return item;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }
    public async Task<AdminAppAnnouncementsDto> ReadPublishedAsync(
        string cookie,string owner,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_app_announcements(@digest,@owner)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var data=raw is null?null:JsonSerializer.Deserialize<AdminAppAnnouncementsDto>(raw,Json);
            if(data is null || data.OrganizationId!=owner || data.Announcements is null ||
              data.Announcements.Count>100 || data.Announcements.Any(item=>
                !ValidAnnouncementId(item.Id)||item.OrganizationId!=owner||
                item.Status!="published"||item.Version<1||
                item.Title is null or {Length:0 or >140}||
                item.Body is null or {Length:0 or >4000}))
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Published notice list verification failed.");
            return data;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }
    public async Task<AdminAppAnnouncementRemovedDto> RemovePublishedAsync(
        string cookie,string owner,string id,long version,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        if(!ValidAnnouncementId(id)||version<1)throw InvalidPublication();
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.remove_admin_app_announcement(
                  @digest,@owner,@id,@version)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("id",id);
            command.Parameters.AddWithValue("version",version);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var result=raw is null?null:JsonSerializer.Deserialize<AdminAppAnnouncementRemovedDto>(raw,Json);
            if(result is null || result.Id!=id||!result.Removed||result.Version!=version+1)
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Removal verification failed.");
            return result;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }
    private static bool ValidAnnouncementId(string? id)=>
        id is {Length:36} && id.StartsWith("ann_",StringComparison.Ordinal) &&
        id.AsSpan(4).ToString().All(char.IsAsciiHexDigit);
    private static WebAdminException InvalidPublication()=>new(422,
        "invalid_app_announcement","Invalid announcement or version.");

    private static WebAdminException TranslatePublished(PostgresException error) =>
        error.SqlState switch
        {
            "42501"=>new(403,"not_authorized","App announcement access denied."),
            "22023" or "23503"=>new(422,"invalid_app_announcement",
                "Invalid announcement or current roster target."),
            "23505" or "40001"=>new(409,"app_announcement_conflict",
                "The announcement changed; refresh and retry."),
            "P0001" when error.MessageText=="admin_club_session_lock_missing" =>
                new(401,"not_authenticated","Sign in again."),
            "P0001" when error.MessageText=="admin_club_grant_lock_missing" =>
                new(403,"not_authorized","Organization access changed."),
            _=>new(503,"app_announcement_unavailable",
                "App announcement service unavailable.")
        };

    private static WebAdminException Translate(PostgresException e) =>
        e.SqlState switch
        {
            "42501"=>new(403,"not_authorized","Announcement drafts denied."),
            "22023" or "23503"=>new(422,"invalid_announcement_draft","Invalid group or draft."),
            "23505"=>new(409,"announcement_draft_conflict","Draft identifier conflict."),
            "40001"=>new(409,"app_announcement_conflict","Announcement changed; refresh and retry."),
            "54000"=>new(503,"app_announcement_unavailable","Published notice limit reached."),
            "P0001" when e.MessageText=="admin_club_session_lock_missing" =>
                new(401,"not_authenticated","Sign in again."),
            "P0001" when e.MessageText=="admin_club_grant_lock_missing" =>
                new(403,"not_authorized","Organization access changed."),
            _ => new(503,"announcement_draft_unavailable","Draft service unavailable.")
        };
}
