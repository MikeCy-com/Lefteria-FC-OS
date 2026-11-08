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
                    d.Body is null or {Length:0 or >4000} ||
                    (d.Media is not null && !ValidMedia(d.Media))))
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
                item.Body is null or {Length:0 or >4000}||
                (item.Media is not null && !ValidMedia(item.Media))))
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
    public async Task<AdminAppAnnouncementReadersDto> ReadPublishedReadersAsync(
        string cookie,string owner,string id,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        if(!ValidAnnouncementId(id))throw InvalidPublication();
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.read_admin_app_announcement_readers(
                  @digest,@owner,@id)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("id",id);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var readers=raw is null?null:JsonSerializer.Deserialize<AdminAppAnnouncementReadersDto>(raw,Json);
            if(readers is null||readers.AnnouncementId!=id||
               readers.Readers is null||readers.Readers.Count>150||
               readers.Readers.Any(r=>r.Id==Guid.Empty||
                 string.IsNullOrWhiteSpace(r.Name)||r.Name.Length>200))
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Readership verification failed.");
            return readers;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }
    public async Task<AdminAnnouncementMediaMutationDto> AddMediaAsync(string cookie,string owner,
        string id,long version,string kind,string fileName,string contentType,
        byte[] content,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        if(!ValidAnnouncementId(id)||version<1||
           !ValidInputMedia(kind,fileName,contentType,content))
            throw InvalidPublication();
        var mediaId=Guid.NewGuid();
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.write_admin_app_announcement_media(
                  @digest,@owner,@announcement,@version,@media,
                  @kind,@file,@mime,@bytes)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("announcement",id);
            command.Parameters.AddWithValue("version",version);
            command.Parameters.AddWithValue("media",mediaId);
            command.Parameters.AddWithValue("kind",kind);
            command.Parameters.AddWithValue("file",fileName);
            command.Parameters.AddWithValue("mime",contentType);
            command.Parameters.Add("bytes",NpgsqlTypes.NpgsqlDbType.Bytea).Value=content;
            var raw=await command.ExecuteScalarAsync(token) as string;
            var data=raw is null?null:JsonSerializer.Deserialize<AdminAnnouncementMediaMutationDto>(raw,Json);
            if(data is null||data.AnnouncementId!=id||data.Version!=version+1||
               data.Media is null||!ValidMedia(data.Media)||
               !data.Media.Any(m=>m.Id==mediaId&&m.Kind==kind&&m.FileName==fileName))
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Uploaded media could not be verified.");
            return data;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }

    public async Task<AdminAnnouncementMediaMutationDto> RemoveMediaAsync(string cookie,string owner,
        string id,long version,Guid mediaId,CancellationToken token)
    {
        Verify(cookie,owner,secrets);
        if(!ValidAnnouncementId(id)||version<1||mediaId==Guid.Empty)throw InvalidPublication();
        try
        {
            await using var connection=await connections.OpenAsync(token);
            await using var command=new NpgsqlCommand("""
                SELECT club.remove_admin_app_announcement_media(
                  @digest,@owner,@announcement,@version,@media)::text
                """,connection);
            command.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie));
            command.Parameters.AddWithValue("owner",owner);
            command.Parameters.AddWithValue("announcement",id);
            command.Parameters.AddWithValue("version",version);
            command.Parameters.AddWithValue("media",mediaId);
            var raw=await command.ExecuteScalarAsync(token) as string;
            var data=raw is null?null:JsonSerializer.Deserialize<AdminAnnouncementMediaMutationDto>(raw,Json);
            if(data is null||data.AnnouncementId!=id||data.Version!=version+1||
                data.Media is null||!ValidMedia(data.Media)||data.Media.Any(m=>m.Id==mediaId))
                throw new WebAdminException(503,"app_announcement_unavailable",
                    "Media removal could not be verified.");
            return data;
        }
        catch(PostgresException error){throw TranslatePublished(error);}
    }

    private static bool ValidInputMedia(string kind,string file,string mime,byte[] bytes)=>
        bytes.Length>0 && (kind=="banner" &&
          file=="banner.jpg" && mime=="image/jpeg" &&
          bytes.Length<=1048576 && bytes.Length>=4 &&
          bytes[0]==0xff && bytes[1]==0xd8 &&
          bytes[^2]==0xff && bytes[^1]==0xd9 ||
        kind=="attachment" && mime=="application/pdf" &&
          bytes.Length is >=8 and <=2097152 &&
          System.Text.RegularExpressions.Regex.IsMatch(file,
            @"\A[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf\z",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase|
            System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
          bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8) &&
          bytes.AsSpan(bytes.Length-5,5).SequenceEqual("%%EOF"u8));
    private static bool ValidMedia(IReadOnlyList<AdminAnnouncementMediaDto> entries)=>
        entries.Count<=4&&entries.Count(m=>m.Kind=="banner")<=1&&
        entries.Count(m=>m.Kind=="attachment")<=3&&
        entries.Select(m=>m.Id).Distinct().Count()==entries.Count&&entries.All(m=>
          m.Id!=Guid.Empty&&m.FileName is {Length:>0 and <=96}&&
          ((m.Kind=="banner"&&m.ContentType=="image/jpeg"&&
            m.FileName=="banner.jpg"&&m.Size is >=4 and <=1048576)||
           (m.Kind=="attachment"&&m.ContentType=="application/pdf"&&
            m.Size is >=8 and <=2097152)));

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
