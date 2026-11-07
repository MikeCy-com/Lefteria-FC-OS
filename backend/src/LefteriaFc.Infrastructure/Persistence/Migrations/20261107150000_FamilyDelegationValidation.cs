using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Read-only, phone-verified delegation revalidation primitive. This does not
// provision a player identity or authorize player data endpoints by itself.
// Future role/session projections must recheck this at the private boundary.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107150000_FamilyDelegationValidation")]
public sealed class FamilyDelegationValidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.read_current_family_delegation(
              _recipient uuid,_phone text,_role text,_expires timestamptz)
            RETURNS TABLE(grant_id uuid,player_id text,organization_id text)
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_current$
            DECLARE
              v_now timestamptz := statement_timestamp();
            BEGIN
              IF _recipient IS NULL OR
                 _recipient='00000000-0000-0000-0000-000000000000'::uuid
                OR _role IS DISTINCT FROM 'fan' OR _expires IS NULL
                OR _expires<=v_now OR _phone IS NULL
                OR _phone !~ '^[1-9][0-9]{7,14}$'
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;

              -- An active database row alone is never proof of access. Check
              -- the current OTP fan, exact accepted invitation, guardian's
              -- canonical link and active roster on every invocation.
              RETURN QUERY
              SELECT DISTINCT g.grant_id,p.id::text,p.organization_id::text
              FROM identity.mobile_identities recipient
              JOIN identity.family_delegated_player_grants g
                ON g.recipient_identity_id=recipient.id
                AND g.status='active' AND g.revoked_at IS NULL
              JOIN identity.family_invitations f
                ON f.invitation_id=g.invitation_id
                AND f.player_id=g.player_id
                AND f.organization_id=g.organization_id
                AND f.status='active' AND f.target_phone=recipient.normalized_phone
                AND f.parent_identity_id=g.granted_by_identity_id
              JOIN identity.mobile_identities guardian
                ON guardian.id=g.granted_by_identity_id
                AND guardian.normalized_phone=f.parent_phone
                AND guardian.role='parent' AND guardian.is_active
              JOIN identity.mobile_record_links l
                ON l.identity_id=guardian.id
                AND l.normalized_phone=guardian.normalized_phone
                AND l.role='parent' AND l.collection='players'
                AND l.kind='guardian' AND l.record_id=g.player_id
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.source_id=l.record_id
                AND b.kind='guardian' AND b.normalized_phone=guardian.normalized_phone
              JOIN club.players p ON p.id=g.player_id
                AND p.organization_id=g.organization_id
              WHERE recipient.id=_recipient
                AND recipient.normalized_phone=_phone
                AND recipient.role='fan' AND recipient.is_active
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
                AND NOT EXISTS(
                  SELECT 1 FROM identity.canonical_contact_bindings c
                  WHERE c.collection='players' AND c.source_id=g.player_id
                    AND c.kind='player_own')
                AND NOT EXISTS(
                  SELECT 1 FROM identity.canonical_contact_bindings c
                  WHERE c.kind='player_own' AND c.normalized_phone=_phone)
                AND NOT EXISTS(
                  SELECT 1 FROM identity.mobile_identities other
                  WHERE other.normalized_phone=_phone AND other.role<>'fan');
            END
            $family_current$;

            REVOKE ALL ON FUNCTION
              identity.read_current_family_delegation(uuid,text,text,timestamptz)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION
              identity.read_current_family_delegation(uuid,text,text,timestamptz);
            """);
    }
}
