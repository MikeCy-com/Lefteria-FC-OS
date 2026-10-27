using LefteriaFc.Application.Roster;
using LefteriaFc.Domain.Identity;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LefteriaFc.Infrastructure.Roster;

public sealed class PostgresRosterQueries(LefteriaDbContext database) : IRosterQueries
{
    public async Task<RosterPickerSnapshot> GetFirstTeamPickerAsync(MobileSession session, CancellationToken token)
    {
        await database.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = new NpgsqlCommand("""
                WITH bound AS (
                  SELECT i.id FROM identity.mobile_identities i
                  WHERE i.id=@identity AND i.normalized_phone=@phone AND i.role=@role AND i.is_active
                    AND @expires > statement_timestamp()
                    AND (
                      i.role='fan'
                      OR (i.role IN ('parent','player','coach') AND (i.legacy_id IS NULL OR EXISTS (
                        SELECT 1 FROM identity.mobile_record_links l
                        JOIN identity.canonical_contact_bindings b ON b.collection=l.collection
                          AND b.source_id=l.record_id AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
                        LEFT JOIN club.players player ON l.collection='players' AND player.id=l.record_id
                        LEFT JOIN club.staff coach ON l.collection='staff' AND coach.id=l.record_id
                        WHERE l.identity_id=i.id AND l.normalized_phone=i.normalized_phone AND l.role=i.role
                          AND ((i.role IN ('parent','player') AND player.is_active
                            AND NOT player.is_archived AND NOT player.is_transferred)
                            OR (i.role='coach' AND coach.is_active AND NOT coach.is_archived)))))
                      OR (i.role='management' AND EXISTS (
                        SELECT 1 FROM identity.admin_accounts a
                        LEFT JOIN identity.admin_staff_links link ON link.admin_account_id=a.id
                        LEFT JOIN club.staff staff ON staff.id=link.staff_id
                        WHERE a.id=i.management_admin_id AND a.is_active AND NOT a.invite_pending
                          AND ((a.normalized_phone IS NULL AND i.legacy_id IS NULL) OR a.normalized_phone=i.normalized_phone)
                          AND (link.admin_account_id IS NULL OR (staff.is_active AND NOT staff.is_archived))))
                    )
                ), ready AS (SELECT id FROM club.roster_import_state WHERE id='initial')
                SELECT EXISTS(SELECT 1 FROM bound), EXISTS(SELECT 1 FROM ready),
                       p.id,p.name,p.number,p.position,p.image_url
                FROM (SELECT 1) marker
                LEFT JOIN LATERAL (
                  SELECT id,name,number,position,image_url FROM club.players
                  WHERE EXISTS(SELECT 1 FROM bound) AND EXISTS(SELECT 1 FROM ready)
                    AND organization_id='lefteria_fc' AND team_type='First Team'
                    AND has_explicit_picker_fields AND is_active AND NOT is_archived AND NOT is_transferred
                  ORDER BY number,id COLLATE "C" LIMIT 50
                ) p ON true
                ORDER BY p.number,p.id COLLATE "C"
                """, (NpgsqlConnection)database.Database.GetDbConnection());
            command.Parameters.AddWithValue("identity", session.IdentityId);
            command.Parameters.AddWithValue("phone", session.NormalizedPhone);
            command.Parameters.AddWithValue("role", session.Role.ToWireValue());
            command.Parameters.AddWithValue("expires", session.ExpiresAt);
            await using var reader = await command.ExecuteReaderAsync(token);
            var items = new List<MobilePlayerOptionDto>(); var available = false; var ready = false;
            while (await reader.ReadAsync(token))
            {
                available = reader.GetBoolean(0); ready = reader.GetBoolean(1);
                if (!reader.IsDBNull(2)) items.Add(new(reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                    reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
            return new(available, ready, items);
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
