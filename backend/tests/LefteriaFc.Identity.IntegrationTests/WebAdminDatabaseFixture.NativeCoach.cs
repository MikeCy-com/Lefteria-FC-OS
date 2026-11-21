using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class WebAdminDatabaseFixture
{
    public async Task VerifyNativeCoachAsync(string staffId,string owner,string group,
        Guid? linkedAccount,string expectedName)
    {
        if(!staffId.StartsWith("stf_admin_",StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture only verifies admin-created coaches.");
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT s.name,s.job_role,s.is_active,s.is_archived,
              s.has_explicit_active,s.has_explicit_archived,
              (SELECT count(*)::integer FROM identity.admin_staff_links l
                WHERE l.staff_id=s.id AND l.admin_account_id IS NOT DISTINCT FROM @linked),
              (SELECT count(*)::integer FROM club.staff_teams t
                WHERE t.staff_id=s.id AND t.organization_id=@owner
                  AND t.team_id=@group AND t.from_admin
                  AND NOT t.from_plural AND NOT t.from_singular),
              (SELECT count(*)::integer FROM club.staff_academy_groups a
                WHERE a.staff_id=s.id AND a.organization_id=@owner
                  AND a.academy_group_id=@group AND a.from_admin
                  AND NOT a.from_plural AND NOT a.from_singular
                  AND NOT a.from_group_coach)
            FROM club.staff s WHERE s.id=@id
            """,con);
        cmd.Parameters.AddWithValue("id",staffId);
        cmd.Parameters.AddWithValue("owner",owner);
        cmd.Parameters.AddWithValue("group",group);
        cmd.Parameters.Add("linked",NpgsqlTypes.NpgsqlDbType.Uuid).Value=
            linkedAccount is Guid link?(object)link:DBNull.Value;
        await using var reader=await cmd.ExecuteReaderAsync();
        if(!await reader.ReadAsync()||
           reader.GetString(0)!=expectedName||
           reader.GetString(1)!="Youth Coach"||
           !reader.GetBoolean(2)||reader.GetBoolean(3)||
           !reader.GetBoolean(4)||!reader.GetBoolean(5)||
           reader.GetInt32(6)!=(linkedAccount is null?0:1)||
           reader.GetInt32(7)!=(owner=="lefteria_fc"?1:0)||
           reader.GetInt32(8)!=(owner=="lefteria_fc_academy"?1:0)||
           await reader.ReadAsync())
            throw new InvalidOperationException("Native coach was not stored with one exact location and reviewed provenance.");
    }

    public async Task<long> CountNativeCoachAuditAsync(string staffId)
    {
        await using var con=await OwnerAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM audit.security_events
            WHERE action='admin.coach.provision' AND outcome='succeeded'
              AND subject_digest=md5(@staff)
            """,con);
        cmd.Parameters.AddWithValue("staff",staffId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task CleanupNativeCoachesAsync(IReadOnlyCollection<Guid> requests)
    {
        if(requests.Count==0)return;
        await using var con=await OwnerAsync();
        await using var tx=await con.BeginTransactionAsync();
        var staffIds=new List<string>();
        await using(var cmd=new NpgsqlCommand("""
            DELETE FROM club.admin_native_coach_requests
            WHERE request_id=ANY(@ids) RETURNING staff_id
            """,con,tx))
        {
            cmd.Parameters.AddWithValue("ids",requests.ToArray());
            await using var reader=await cmd.ExecuteReaderAsync();
            while(await reader.ReadAsync())staffIds.Add(reader.GetString(0));
        }
        if(staffIds.Count>0)
        {
            if(staffIds.Any(id=>!System.Text.RegularExpressions.Regex.IsMatch(
                id,"^stf_admin_[0-9a-f]{32}$")))
                throw new InvalidOperationException("Refusing non-native staff cleanup.");
            await using var cmd=new NpgsqlCommand("""
                DELETE FROM identity.admin_staff_links WHERE staff_id=ANY(@staff);
                DELETE FROM club.staff_teams WHERE staff_id=ANY(@staff) AND from_admin;
                DELETE FROM club.staff_academy_groups WHERE staff_id=ANY(@staff) AND from_admin;
                DELETE FROM club.staff WHERE id=ANY(@staff);
                """,con,tx);
            cmd.Parameters.AddWithValue("staff",staffIds.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
