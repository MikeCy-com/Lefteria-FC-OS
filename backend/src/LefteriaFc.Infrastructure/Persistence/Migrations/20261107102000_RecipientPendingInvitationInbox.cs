using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Read-only recipient inbox: phone possession has already been established by
// the mobile OTP login, but no youth/player identity or grant is created here.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107102000_RecipientPendingInvitationInbox")]
public sealed class RecipientPendingInvitationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.read_recipient_family_invitations(
              _identity uuid, _phone text, _role text, _expires timestamptz
            )
            RETURNS TABLE (
              invitation_id uuid,
              organization_id varchar(32),
              created_at timestamptz,
              expires_at timestamptz
            )
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $recipient_inbox$
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR
                 _role IS DISTINCT FROM 'fan' OR _expires IS NULL OR
                 _expires <= statement_timestamp() OR
                 _phone !~ '^[1-9][0-9]{7,14}$' THEN
                 RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE = 'P0001';
              END IF;

              -- Phone association alone is never an authorization grant.
              -- The request must carry the current active FAN identity of the
              -- same phone, which is issued only after mobile OTP verification.
              IF NOT EXISTS (
                SELECT 1 FROM identity.mobile_identities i
                WHERE i.id = _identity AND i.role = 'fan'
                  AND i.normalized_phone = _phone AND i.is_active
              ) THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE = 'P0001';
              END IF;

              IF NOT EXISTS (
                SELECT 1 FROM club.roster_import_state WHERE id = 'initial'
              ) THEN
                RAISE EXCEPTION 'family_invitation_unavailable' USING ERRCODE = 'P0001';
              END IF;

              -- A pending reservation is displayed only while the ORIGINAL
              -- parent's canonical guardian binding remains live. It does NOT
              -- authorize the recipient for that player, even if displayed.
              -- Child IDs, names, guardian IDs, phones, record links and any
              -- historical actions are never projected to the recipient.
              RETURN QUERY
                SELECT f.invitation_id, f.organization_id, f.created_at, f.expires_at
                FROM identity.family_invitations f
                JOIN identity.mobile_identities parent_i
                  ON parent_i.id = f.parent_identity_id
                  AND parent_i.role = 'parent' AND parent_i.is_active
                  AND parent_i.normalized_phone = f.parent_phone
                JOIN identity.mobile_record_links l
                  ON l.identity_id = parent_i.id
                  AND l.role = 'parent' AND l.normalized_phone = parent_i.normalized_phone
                  AND l.collection = 'players' AND l.kind = 'guardian'
                  AND l.record_id = f.player_id
                JOIN identity.canonical_contact_bindings b
                  ON b.collection = l.collection AND b.source_id = l.record_id
                  AND b.kind = 'guardian'
                  AND b.normalized_phone = parent_i.normalized_phone
                JOIN club.players p
                  ON p.id = f.player_id AND p.organization_id = f.organization_id
                WHERE f.target_phone = _phone
                  AND f.status = 'pending'
                  AND f.expires_at > statement_timestamp()
                  AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                ORDER BY f.created_at DESC, f.invitation_id
                LIMIT 2;
            END
            $recipient_inbox$;

            REVOKE ALL ON FUNCTION identity.read_recipient_family_invitations(uuid,text,text,timestamptz)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION identity.read_recipient_family_invitations(uuid,text,text,timestamptz);
            """);
    }
}
