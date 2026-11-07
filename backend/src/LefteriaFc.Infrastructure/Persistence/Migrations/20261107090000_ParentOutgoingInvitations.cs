using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Only expose currently pending, guardian-owned invitation metadata. Target
// phone numbers and all original audit records remain strictly inaccessible
// to the application database role.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107090000_ParentOutgoingInvitations")]
public sealed class ParentOutgoingInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.read_parent_family_outgoing(
              _actor uuid, _phone text, _role text, _expires timestamptz
            )
            RETURNS TABLE (
              invitation_id uuid,
              player_id varchar(128),
              organization_id varchar(32),
              created_at timestamptz,
              expires_at timestamptz
            )
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_outgoing$
            BEGIN
              IF _actor IS NULL OR _phone IS NULL OR _role IS DISTINCT FROM 'parent'
                OR _expires IS NULL OR _expires <= statement_timestamp()
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE = 'P0001';
              END IF;

              IF NOT EXISTS (
                  SELECT 1 FROM identity.mobile_identities i
                  WHERE i.id = _actor AND i.normalized_phone = _phone
                    AND i.role = 'parent' AND i.is_active
                )
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE = 'P0001';
              END IF;

              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id = 'initial')
              THEN
                RAISE EXCEPTION 'family_invitation_unavailable' USING ERRCODE = 'P0001';
              END IF;

              -- Only the invitation's current owner and the current canonical
              -- guardian relationship for that exact child can produce rows.
              -- No phone, token or historical event fields are exposed.
              RETURN QUERY
                SELECT f.invitation_id, f.player_id, f.organization_id,
                       f.created_at, f.expires_at
                FROM identity.family_invitations f
                JOIN identity.mobile_identities i
                  ON i.id = _actor AND i.normalized_phone = _phone
                  AND i.role = 'parent' AND i.is_active
                JOIN identity.mobile_record_links l
                  ON l.identity_id = i.id AND l.normalized_phone = i.normalized_phone
                  AND l.role = 'parent' AND l.collection = 'players'
                  AND l.kind = 'guardian' AND l.record_id = f.player_id
                JOIN identity.canonical_contact_bindings b
                  ON b.collection = l.collection AND b.source_id = l.record_id
                  AND b.kind = 'guardian' AND b.normalized_phone = i.normalized_phone
                JOIN club.players p
                  ON p.id = f.player_id AND p.organization_id = f.organization_id
                WHERE f.parent_identity_id = i.id AND f.parent_phone = i.normalized_phone
                  AND f.status = 'pending' AND f.expires_at > statement_timestamp()
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ORDER BY f.created_at DESC, f.player_id COLLATE "C"
                LIMIT 101;
            END
            $family_outgoing$;

            REVOKE ALL ON FUNCTION identity.read_parent_family_outgoing(uuid,text,text,timestamptz) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION identity.read_parent_family_outgoing(uuid,text,text,timestamptz);
            """);
    }
}
