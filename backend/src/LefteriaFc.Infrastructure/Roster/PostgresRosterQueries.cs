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
                    AND (i.role <> 'management' OR EXISTS (
                      SELECT 1 FROM identity.admin_accounts a WHERE a.id=i.management_admin_id AND a.is_active))
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
