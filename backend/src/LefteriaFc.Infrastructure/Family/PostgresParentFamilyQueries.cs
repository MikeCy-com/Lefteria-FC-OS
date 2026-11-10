using LefteriaFc.Application.Family;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Family;

public sealed class PostgresParentFamilyQueries(LefteriaDbContext database) : IParentFamilyQueries
{
    private const int MaximumChildren = 100;

    public async Task<ParentFamilySnapshot> GetLinkedChildrenAsync(MobileSession session, CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                WITH requested AS (
                  SELECT i.id, i.normalized_phone, i.role
                  FROM identity.mobile_identities i
                  WHERE i.id=@identity AND i.normalized_phone=@phone AND i.role='parent'
                    AND @role='parent' AND i.is_active AND @expires > statement_timestamp()
                ), children AS (
                  SELECT p.id, p.name, p.organization_id, p.team_type, p.team_id, l.is_primary,
                    ARRAY(
                      SELECT g.academy_group_id::text FROM club.player_academy_groups g
                      WHERE g.player_id=p.id AND g.organization_id=p.organization_id
                      ORDER BY g.academy_group_id::text COLLATE "C"
                    ) AS group_ids
                  FROM requested i
                  JOIN identity.mobile_record_links l
                    ON l.identity_id=i.id AND l.normalized_phone=i.normalized_phone
                    AND l.role='parent' AND l.collection='players' AND l.kind='guardian'
                  JOIN identity.canonical_contact_bindings b
                    ON b.collection=l.collection AND b.source_id=l.record_id
                    AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
                  JOIN club.players p ON p.id=l.record_id
                  WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                  UNION ALL
                  SELECT p.id,p.name,p.organization_id,p.team_type,p.team_id,false,
                    ARRAY(
                      SELECT g.academy_group_id::text FROM club.player_academy_groups g
                      WHERE g.player_id=p.id AND g.organization_id=p.organization_id
                      ORDER BY g.academy_group_id::text COLLATE "C"
                    )
                  FROM requested i
                  JOIN LATERAL identity.current_phone_granted_children(
                    i.id,i.normalized_phone,i.role,@expires) permission ON true
                  JOIN club.players p ON p.id=permission.player_id
                    AND p.organization_id=permission.organization_id
                  WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                )
                SELECT EXISTS(SELECT 1 FROM requested),
                  EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial'),
                  child.id, child.name, child.organization_id, child.team_type,
                  child.team_id, child.group_ids
                FROM (SELECT 1) marker
                LEFT JOIN LATERAL (
                  SELECT * FROM (
                    SELECT DISTINCT ON (id) * FROM children
                    ORDER BY id COLLATE "C", is_primary DESC
                  ) unique_children
                  ORDER BY is_primary DESC, id COLLATE "C"
                  LIMIT 101
                ) child ON true
                """, (NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity", session.IdentityId);
            command.Parameters.AddWithValue("phone", session.NormalizedPhone);
            command.Parameters.AddWithValue("role", session.Role.ToWireValue());
            command.Parameters.AddWithValue("expires", session.ExpiresAt.ToUniversalTime());
            await using var reader = await command.ExecuteReaderAsync(token);
            var available = false;
            var ready = false;
            var children = new List<FamilyChildDto>();
            while (await reader.ReadAsync(token))
            {
                available = reader.GetBoolean(0);
                ready = reader.GetBoolean(1);
                if (reader.IsDBNull(2)) continue;
                children.Add(new(reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetFieldValue<string[]>(7)));
            }
            // A surprising number of current grants must not turn into a
            // silent partial child listing; fail closed pending investigation.
            if (children.Count > MaximumChildren)
                throw new MobileAuthException(MobileAuthFaultKind.ServiceUnavailable,
                    MobileAuthErrorCode.RosterUnavailable, "Family information is temporarily unavailable.");
            return new(available, ready, children);
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
