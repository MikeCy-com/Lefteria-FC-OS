using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// Creates an auditable delegation only. A grant is not a mobile player
// identity: player session issuance and private-data authorization remain
// disabled until separately implemented and verified end to end.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261107140000_FamilyDelegationAcceptance")]
public sealed class FamilyDelegationAcceptance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION identity.accept_recipient_family_invitation(
              _recipient uuid,_phone text,_role text,_expires timestamptz,
              _device uuid,_invitation uuid,_grant uuid)
            RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_accept$
            DECLARE
              v_row identity.family_invitations%ROWTYPE;
              v_scope text;
              v_child text;
              v_now timestamptz := statement_timestamp();
            BEGIN
              IF _recipient IS NULL OR _recipient='00000000-0000-0000-0000-000000000000'::uuid
                OR _device IS NULL OR _device='00000000-0000-0000-0000-000000000000'::uuid
                OR _invitation IS NULL OR _invitation='00000000-0000-0000-0000-000000000000'::uuid
                OR _grant IS NULL OR _grant='00000000-0000-0000-0000-000000000000'::uuid
                OR _role IS DISTINCT FROM 'fan' OR _expires IS NULL OR _expires<=v_now
                OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
              THEN
                RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001';
              END IF;

              -- OTP-backed fan identity and the canonical guardian/player
              -- relationship must both still be valid at the commit boundary.
              SELECT p.organization_id,p.id INTO v_scope,v_child
              FROM identity.mobile_identities recipient
              JOIN identity.family_invitations f
                ON f.target_phone=recipient.normalized_phone
                AND f.invitation_id=_invitation
              JOIN identity.mobile_identities guardian
                ON guardian.id=f.parent_identity_id
                AND guardian.normalized_phone=f.parent_phone
                AND guardian.role='parent' AND guardian.is_active
              JOIN identity.mobile_record_links l
                ON l.identity_id=guardian.id AND l.role='parent'
                AND l.normalized_phone=guardian.normalized_phone
                AND l.collection='players' AND l.kind='guardian'
                AND l.record_id=f.player_id
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.source_id=l.record_id
                AND b.kind='guardian' AND b.normalized_phone=guardian.normalized_phone
              JOIN club.players p ON p.id=f.player_id
                AND p.organization_id=f.organization_id
              WHERE recipient.id=_recipient AND recipient.normalized_phone=_phone
                AND recipient.role='fan' AND recipient.is_active
                AND f.status='pending' AND f.expires_at>v_now
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
              FOR SHARE OF recipient,guardian,l,b,p;
              IF NOT FOUND THEN
                RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001';
              END IF;

              -- The club's canonical player-phone ownership remains supreme.
              -- A pending target must not be mapped to another child's player
              -- identity or formal staff/guardian role, even if changed since
              -- the original reservation was created.
              IF EXISTS(SELECT 1 FROM identity.canonical_contact_bindings c
                         WHERE c.collection='players' AND c.source_id=v_child
                           AND c.kind='player_own')
                OR EXISTS(SELECT 1 FROM identity.canonical_contact_bindings c
                         WHERE c.kind='player_own' AND c.normalized_phone=_phone)
                OR EXISTS(SELECT 1 FROM identity.mobile_identities i
                         WHERE i.normalized_phone=_phone AND i.role<>'fan')
                OR EXISTS(SELECT 1 FROM identity.family_delegated_player_grants g
                         WHERE g.status='active' AND
                           (g.recipient_identity_id=_recipient OR g.player_id=v_child))
              THEN
                RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
              END IF;

              SELECT * INTO v_row FROM identity.family_invitations
                WHERE invitation_id=_invitation AND target_phone=_phone FOR UPDATE;
              IF NOT FOUND OR v_row.status<>'pending' OR v_row.expires_at<=v_now
                OR v_row.player_id<>v_child OR v_row.organization_id<>v_scope
              THEN
                RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001';
              END IF;

              INSERT INTO identity.family_delegated_player_grants
                (grant_id,invitation_id,player_id,organization_id,
                 recipient_identity_id,granted_by_identity_id,status,granted_at)
              VALUES (_grant,_invitation,v_child,v_scope,_recipient,
                v_row.parent_identity_id,'active',v_now);

              UPDATE identity.family_invitations
                SET status='active',version=version+1,updated_at=v_now
              WHERE player_id=v_child AND invitation_id=_invitation AND status='pending';

              INSERT INTO identity.family_invitation_events
                (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
              VALUES(gen_random_uuid(),v_child,v_scope,_invitation,_recipient,'accepted',v_now);

              INSERT INTO audit.security_events
                (id,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id)
              VALUES(gen_random_uuid(),v_now,_recipient,_device,
                'mobile.family.grant.accept','success',v_scope);
              RETURN true;
            EXCEPTION WHEN unique_violation THEN
              RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
            END
            $family_accept$;

            REVOKE ALL ON FUNCTION
              identity.accept_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid,uuid)
              FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $accept_rollback$
            BEGIN
              IF EXISTS (SELECT 1 FROM identity.family_delegated_player_grants) THEN
                RAISE EXCEPTION 'family_grant_accept_requires_forward_recovery';
              END IF;
            END
            $accept_rollback$;
            DROP FUNCTION
              identity.accept_recipient_family_invitation(uuid,text,text,timestamptz,uuid,uuid,uuid);
            """);
    }
}
