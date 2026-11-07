using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Database-only revocation primitive. No acceptance or delegation-based
// authorization is enabled by this migration. A live guardian must retain
// a current canonical link before a grant can be revoked.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107130000_FamilyDelegationRevocation")]
public sealed class FamilyDelegationRevocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.revoke_family_delegated_player_grant(
              _actor uuid,_phone text,_role text,_expires timestamptz,
              _device uuid,_player text)
            RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_revoke$
            DECLARE
              v_scope text;
              v_grant identity.family_delegated_player_grants%ROWTYPE;
              v_invite identity.family_invitations%ROWTYPE;
              v_now timestamptz := statement_timestamp();
            BEGIN
              IF _actor IS NULL OR _actor='00000000-0000-0000-0000-000000000000'::uuid
                OR _device IS NULL OR _device='00000000-0000-0000-0000-000000000000'::uuid
                OR _role IS DISTINCT FROM 'parent' OR _expires IS NULL OR _expires<=v_now
                OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
                OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;

              -- Revalidate identity, explicit parent link, canonical guardian
              -- binding, active roster and completed initial import in a
              -- single transaction. The row locks serialize guardian removal
              -- and child archival with revocation.
              SELECT p.organization_id INTO v_scope
              FROM identity.mobile_identities i
              JOIN identity.mobile_record_links l ON l.identity_id=i.id
                AND l.role='parent' AND l.normalized_phone=i.normalized_phone
                AND l.collection='players' AND l.kind='guardian' AND l.record_id=_player
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.source_id=l.record_id
                AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
              JOIN club.players p ON p.id=l.record_id
              WHERE i.id=_actor AND i.normalized_phone=_phone
                AND i.role='parent' AND i.is_active
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial')
              FOR SHARE OF i,l,b,p;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001';
              END IF;

              SELECT * INTO v_grant
              FROM identity.family_delegated_player_grants
              WHERE player_id=_player AND organization_id=v_scope AND status='active'
                AND granted_by_identity_id=_actor
              FOR UPDATE;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001';
              END IF;

              -- An invitation reservation is a separate mutable slot. A new
              -- invite must not have replaced the grant we are revoking.
              SELECT * INTO v_invite FROM identity.family_invitations
              WHERE player_id=_player FOR UPDATE;
              IF NOT FOUND OR v_invite.invitation_id<>v_grant.invitation_id
                OR v_invite.organization_id<>v_scope OR v_invite.status<>'active'
                OR v_invite.parent_identity_id<>_actor OR v_invite.parent_phone<>_phone
              THEN
                RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
              END IF;

              UPDATE identity.family_delegated_player_grants
                SET status='revoked',revoked_at=v_now
              WHERE grant_id=v_grant.grant_id AND status='active';

              UPDATE identity.family_invitations
                SET status='revoked',version=version+1,updated_at=v_now
              WHERE player_id=_player AND invitation_id=v_grant.invitation_id
                AND status='active';

              INSERT INTO identity.family_invitation_events
                (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
              VALUES (gen_random_uuid(),_player,v_scope,v_grant.invitation_id,_actor,'revoked',v_now);

              INSERT INTO audit.security_events
                (id,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id)
              VALUES (gen_random_uuid(),v_now,_actor,_device,
                'mobile.family.grant.revoke','success',v_scope);
              RETURN true;
            END
            $family_revoke$;

            REVOKE ALL ON FUNCTION
              identity.revoke_family_delegated_player_grant(uuid,text,text,timestamptz,uuid,text)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Removing a revocation primitive would strand existing active grants.
        migrationBuilder.Sql("""
            DO $revoke_rollback$
            BEGIN
              IF EXISTS (SELECT 1 FROM identity.family_delegated_player_grants) THEN
                RAISE EXCEPTION 'family_grant_revoke_requires_forward_recovery';
              END IF;
            END
            $revoke_rollback$;
            DROP FUNCTION
              identity.revoke_family_delegated_player_grant(uuid,text,text,timestamptz,uuid,text);
            """);
    }
}
