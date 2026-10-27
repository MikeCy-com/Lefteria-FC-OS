using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LefteriaFc.Infrastructure.Identity;

internal sealed record CanonicalMobileProfile(bool SignatureMatches,bool IsAvailable,
    string? LinkedPlayerId,IReadOnlyList<string> LinkedPlayerIds,string? TeamId,
    IReadOnlyList<string> GroupIds,ManagementOrganizationGrant? Management)
{
    public override string ToString()=>"Current canonical mobile relationship snapshot";
}

internal static class CanonicalMobileProfiles
{
    public static async Task<IReadOnlyDictionary<Guid,CanonicalMobileProfile>> ReadAsync(
        LefteriaDbContext database,IReadOnlyList<MobileIdentityRecord> rows,CancellationToken token)
    {
        if(rows.Count==0)return new Dictionary<Guid,CanonicalMobileProfile>();
        var expected=rows.ToDictionary(row=>row.Id);
        var result=new Dictionary<Guid,CanonicalMobileProfile>();
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command=new NpgsqlCommand("""
                WITH requested AS (
                  SELECT id,normalized_phone,role,is_active,legacy_id,management_admin_id
                  FROM identity.mobile_identities WHERE id=ANY(@ids)
                ), player_links AS (
                  SELECT l.identity_id,p.id,p.team_id,p.organization_id,l.is_primary
                  FROM identity.mobile_record_links l
                  JOIN requested i ON i.id=l.identity_id AND i.role=l.role AND i.normalized_phone=l.normalized_phone
                  JOIN identity.canonical_contact_bindings b ON b.collection=l.collection AND b.source_id=l.record_id
                    AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
                  JOIN club.players p ON p.id=l.record_id
                  WHERE l.collection='players' AND l.kind IN ('player_own','guardian')
                    AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ), player_groups AS (
                  SELECT DISTINCT p.identity_id,g.academy_group_id
                  FROM player_links p JOIN club.player_academy_groups g
                    ON g.player_id=p.id AND g.organization_id=p.organization_id
                ), coach_links AS (
                  SELECT l.identity_id,s.id FROM identity.mobile_record_links l
                  JOIN requested i ON i.id=l.identity_id AND i.role=l.role AND i.normalized_phone=l.normalized_phone
                  JOIN identity.canonical_contact_bindings b ON b.collection=l.collection AND b.source_id=l.record_id
                    AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
                  JOIN club.staff s ON s.id=l.record_id
                  WHERE l.role='coach' AND l.collection='staff' AND l.kind='staff_own' AND s.is_active AND NOT s.is_archived
                ), coach_teams AS (
                  SELECT DISTINCT c.identity_id,t.team_id FROM coach_links c JOIN club.staff_teams t ON t.staff_id=c.id
                ), coach_groups AS (
                  SELECT DISTINCT c.identity_id,g.academy_group_id
                  FROM coach_links c JOIN club.staff_academy_groups g ON g.staff_id=c.id
                  WHERE g.from_plural OR g.from_singular OR (g.from_group_coach AND EXISTS (
                    SELECT 1 FROM club.academy_group_coaches actual
                    WHERE actual.academy_group_id=g.academy_group_id AND actual.staff_id=c.id))
                )
                SELECT i.id,i.normalized_phone,i.role,i.is_active,i.legacy_id IS NULL,a.id,
                  coalesce(a.is_active AND NOT a.invite_pending AND
                    ((a.normalized_phone IS NULL AND i.legacy_id IS NULL) OR a.normalized_phone=i.normalized_phone)
                    AND (admin_staff.admin_account_id IS NULL OR (staff.is_active AND NOT staff.is_archived)),false),
                  a.active_organization_id,
                  ARRAY(SELECT organization_id::text FROM identity.admin_organization_grants grant_row
                    WHERE grant_row.admin_account_id=a.id ORDER BY organization_id COLLATE "C"),
                  ARRAY(SELECT p.id::text FROM player_links p WHERE p.identity_id=i.id ORDER BY p.is_primary DESC,p.id COLLATE "C"),
                  CASE WHEN i.role='coach' THEN
                    (SELECT min(t.team_id)::text FROM coach_teams t WHERE t.identity_id=i.id HAVING count(DISTINCT t.team_id)=1)
                  ELSE (SELECT min(p.team_id)::text FROM player_links p WHERE p.identity_id=i.id HAVING count(DISTINCT p.team_id)=1) END,
                  CASE WHEN i.role='coach' THEN
                    ARRAY(SELECT g.academy_group_id::text FROM coach_groups g WHERE g.identity_id=i.id ORDER BY g.academy_group_id COLLATE "C")
                  ELSE ARRAY(SELECT g.academy_group_id::text FROM player_groups g WHERE g.identity_id=i.id ORDER BY g.academy_group_id COLLATE "C") END,
                  EXISTS(SELECT 1 FROM coach_links c WHERE c.identity_id=i.id)
                FROM requested i
                LEFT JOIN identity.admin_accounts a ON a.id=i.management_admin_id
                LEFT JOIN identity.admin_staff_links admin_staff ON admin_staff.admin_account_id=a.id
                LEFT JOIN club.staff staff ON staff.id=admin_staff.staff_id
                """,(NpgsqlConnection)database.Database.GetDbConnection());
            command.Transaction=database.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
            command.Parameters.AddWithValue("ids",rows.Select(row=>row.Id).ToArray());
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
            {
                var id=reader.GetGuid(0);var original=expected[id];var phone=reader.GetString(1);var role=reader.GetString(2);
                var signature=phone==original.NormalizedPhone&&role==original.Role;
                var active=reader.GetBoolean(3);var native=reader.GetBoolean(4);var manager=reader.GetBoolean(6);
                var players=reader.GetFieldValue<string[]>(9);var coach=reader.GetBoolean(12);
                var available=signature&&active&&(role switch
                {
                    "management"=>manager,
                    "fan"=>true,
                    _ when native=>true, // Explicit owner-provisioned grant; private operations still require canonical links.
                    "parent"=>players.Length>0,
                    "player"=>players.Length==1,
                    "coach"=>coach,
                    _=>false
                });
                ManagementOrganizationGrant? management=role=="management"&&!reader.IsDBNull(5)
                    ?new(reader.GetGuid(5),manager,reader.GetFieldValue<string[]>(8),reader.IsDBNull(7)?null:reader.GetString(7)):null;
                result.Add(id,new(signature,available,
                    (role is "parent" or "player")&&players.Length>0?players[0]:null,
                    role=="parent"?players:[],reader.IsDBNull(10)?null:reader.GetString(10),reader.GetFieldValue<string[]>(11),management));
            }
            return result;
        }
        finally{await database.Database.CloseConnectionAsync();}
    }
}
