using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Read-only summary for a currently verified parent. Never disclose the
// recipient's phone, fan identity, grant identifier or audit history.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107160000_ParentActiveFamilyDelegations")]
public sealed class ParentActiveFamilyDelegations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.read_parent_active_family_delegations(
              _guardian uuid,_phone text,_role text,_expires timestamptz)
            RETURNS TABLE(player_id text,organization_id text)
            LANGUAGE plpgsql STABLE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $parent_active_grants$
            BEGIN
              IF _guardian IS NULL OR
                 _guardian='00000000-0000-0000-0000-000000000000'::uuid
                 OR _role IS DISTINCT FROM 'parent'
                 OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
                 OR _expires IS NULL OR _expires<=statement_timestamp()
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;
              IF NOT EXISTS (
                SELECT 1 FROM identity.mobile_identities parent_i
                WHERE parent_i.id=_guardian AND parent_i.role='parent'
                  AND parent_i.normalized_phone=_phone AND parent_i.is_active
              ) THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial') THEN
                RAISE EXCEPTION 'family_invitation_unavailable' USING ERRCODE='P0001';
              END IF;

              -- Revalidate every joining authority on each request. An
              -- "active" grant without a valid fan, invitation or canonical
              -- guardian relationship is never returned to the parent.
              RETURN QUERY
              SELECT DISTINCT p.id::text,p.organization_id::text
              FROM identity.family_delegated_player_grants g
              JOIN identity.family_invitations f ON f.invitation_id=g.invitation_id
                AND f.player_id=g.player_id AND f.organization_id=g.organization_id
                AND f.parent_identity_id=_guardian AND f.parent_phone=_phone
                AND f.status='active'
              JOIN identity.mobile_identities recipient
                ON recipient.id=g.recipient_identity_id
                AND recipient.normalized_phone=f.target_phone
                AND recipient.role='fan' AND recipient.is_active
              JOIN identity.mobile_record_links link
                ON link.identity_id=_guardian AND link.role='parent'
                AND link.normalized_phone=_phone AND link.collection='players'
                AND link.kind='guardian' AND link.record_id=g.player_id
              JOIN identity.canonical_contact_bindings binding
                ON binding.collection=link.collection AND binding.source_id=link.record_id
                AND binding.kind='guardian' AND binding.normalized_phone=_phone
              JOIN club.players p ON p.id=g.player_id
                AND p.organization_id=g.organization_id
              WHERE g.granted_by_identity_id=_guardian AND g.status='active'
                AND g.revoked_at IS NULL
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND NOT EXISTS(SELECT 1 FROM identity.canonical_contact_bindings c
                  WHERE c.collection='players' AND c.source_id=g.player_id AND c.kind='player_own')
                AND NOT EXISTS(SELECT 1 FROM identity.canonical_contact_bindings c
                  WHERE c.kind='player_own' AND c.normalized_phone=recipient.normalized_phone)
                AND NOT EXISTS(SELECT 1 FROM identity.mobile_identities other
                  WHERE other.normalized_phone=recipient.normalized_phone AND other.role<>'fan')
              ORDER BY p.id::text,p.organization_id::text
              LIMIT 101;
            END
            $parent_active_grants$;

            REVOKE ALL ON FUNCTION
              identity.read_parent_active_family_delegations(uuid,text,text,timestamptz)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION
              identity.read_parent_active_family_delegations(uuid,text,text,timestamptz);
            """);
    }
}
