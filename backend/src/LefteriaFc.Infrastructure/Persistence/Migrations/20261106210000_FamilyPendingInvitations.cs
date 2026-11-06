using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

// SQL-only migration: no new tracked DbSet. The phone-bearing tables are never
// exposed to the API role; application writes go through narrowly scoped
// SECURITY DEFINER functions with fresh canonical guardian reauthorization.
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261106210000_FamilyPendingInvitations")]
public sealed class FamilyPendingInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE identity.family_invitations (
              player_id varchar(128) COLLATE "C" PRIMARY KEY,
              organization_id varchar(32) NOT NULL,
              parent_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
              parent_phone varchar(15) NOT NULL,
              target_phone varchar(15) NOT NULL,
              invitation_id uuid NOT NULL UNIQUE,
              status varchar(16) NOT NULL,
              version bigint NOT NULL,
              created_at timestamptz NOT NULL,
              expires_at timestamptz NOT NULL,
              updated_at timestamptz NOT NULL,
              CONSTRAINT family_invite_player_fk FOREIGN KEY (organization_id,player_id)
                REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
              CONSTRAINT family_invite_status CHECK (status IN ('pending','cancelled','declined','expired','active','revoked','superseded')),
              CONSTRAINT family_invite_phone CHECK (parent_phone ~ '^[1-9][0-9]{7,14}$' AND target_phone ~ '^[1-9][0-9]{7,14}$'
                AND parent_phone <> target_phone),
              CONSTRAINT family_invite_clock CHECK (created_at <= updated_at AND expires_at > created_at
                AND expires_at <= created_at + interval '7 days'),
              CONSTRAINT family_invite_version CHECK (version >= 1)
            );
            CREATE UNIQUE INDEX family_one_target_slot ON identity.family_invitations(target_phone)
              WHERE status IN ('pending','active');
            CREATE INDEX family_owner_invitations ON identity.family_invitations(parent_identity_id,status);
            CREATE TABLE identity.family_invitation_events (
              id uuid PRIMARY KEY,
              player_id varchar(128) COLLATE "C" NOT NULL,
              organization_id varchar(32) NOT NULL,
              invitation_id uuid NOT NULL,
              actor_identity_id uuid NOT NULL,
              action varchar(24) NOT NULL,
              occurred_at timestamptz NOT NULL,
              CONSTRAINT family_event_child_fk FOREIGN KEY (player_id)
                REFERENCES identity.family_invitations(player_id) ON DELETE RESTRICT,
              CONSTRAINT family_event_actions CHECK (action IN ('issued','cancelled','expired','declined','accepted','revoked','superseded'))
            );
            CREATE INDEX family_events_child_time ON identity.family_invitation_events(player_id,occurred_at);
            REVOKE ALL ON identity.family_invitations,identity.family_invitation_events FROM PUBLIC;

            -- Creation can reserve a pending slot only; it cannot create a player
            -- account, identity link, canonical contact or any active grant.
            CREATE FUNCTION identity.issue_family_invitation(
              _actor uuid, _phone text, _role text, _expires timestamptz,
              _device uuid, _player text, _target text, _new_id uuid)
            RETURNS TABLE (issued_id uuid, issued_at timestamptz, valid_until timestamptz)
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_issue$
            DECLARE
              v_scope text;
              v_old identity.family_invitations%ROWTYPE;
              v_stale identity.family_invitations%ROWTYPE;
              v_now timestamptz := statement_timestamp();
            BEGIN
              IF _actor IS NULL OR _actor='00000000-0000-0000-0000-000000000000'::uuid
                OR _new_id IS NULL OR _new_id='00000000-0000-0000-0000-000000000000'::uuid
                OR _device IS NULL OR _role<>'parent' OR _expires<=v_now
                OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
                OR _target IS NULL OR _target !~ '^[1-9][0-9]{7,14}$'
                OR _target=_phone OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
              THEN RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001'; END IF;

              -- Locks prevent owner revocation, transfer or archival racing
              -- the final write. A phone match alone never implies parenthood.
              SELECT p.organization_id INTO v_scope
              FROM identity.mobile_identities i
              JOIN identity.mobile_record_links l ON l.identity_id=i.id
                AND l.role='parent' AND l.normalized_phone=i.normalized_phone
                AND l.collection='players' AND l.kind='guardian' AND l.record_id=_player
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.source_id=l.record_id
                AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
              JOIN club.players p ON p.id=l.record_id
              WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role='parent'
                AND i.is_active AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
              FOR SHARE OF i,l,b,p;
              IF NOT FOUND THEN RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001'; END IF;

              -- A child with a current club-owned phone cannot be taken over by
              -- an invitation. A target already holding any formal role or a
              -- canonical player phone may not be remapped to another child.
              IF EXISTS (SELECT 1 FROM identity.canonical_contact_bindings c
                           WHERE c.collection='players' AND c.source_id=_player AND c.kind='player_own')
                OR EXISTS (SELECT 1 FROM identity.canonical_contact_bindings c
                           WHERE c.kind='player_own' AND c.normalized_phone=_target)
                OR EXISTS (SELECT 1 FROM identity.mobile_identities mi
                           WHERE mi.normalized_phone=_target AND mi.role<>'fan')
              THEN RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001'; END IF;

              -- Expire only the specific stale target reservation. Its event
              -- remains attached to its original child/slot and invitation ID.
              UPDATE identity.family_invitations slot SET
                status='expired',version=slot.version+1,updated_at=v_now
              WHERE slot.target_phone=_target AND slot.status='pending' AND slot.expires_at<=v_now
              RETURNING slot.* INTO v_stale;
              IF FOUND THEN
                INSERT INTO identity.family_invitation_events
                  (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
                VALUES (gen_random_uuid(),v_stale.player_id,v_stale.organization_id,
                  v_stale.invitation_id,_actor,'expired',v_now);
              END IF;

              SELECT * INTO v_old FROM identity.family_invitations
                WHERE player_id=_player FOR UPDATE;
              IF FOUND THEN
                IF v_old.status='pending' AND v_old.expires_at>v_now THEN
                  IF v_old.parent_identity_id=_actor AND v_old.parent_phone=_phone
                    AND v_old.target_phone=_target THEN
                    RETURN QUERY SELECT v_old.invitation_id,v_old.created_at,v_old.expires_at;
                    RETURN;
                  END IF;
                  RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
                END IF;
                IF v_old.status='active' THEN
                  RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
                END IF;
                IF v_old.status='pending' THEN
                  UPDATE identity.family_invitations slot SET
                    status='expired',version=slot.version+1,updated_at=v_now
                  WHERE slot.player_id=_player;
                  INSERT INTO identity.family_invitation_events
                    (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
                  VALUES (gen_random_uuid(),v_old.player_id,v_old.organization_id,
                    v_old.invitation_id,_actor,'expired',v_now);
                END IF;
                UPDATE identity.family_invitations slot SET
                  organization_id=v_scope,parent_identity_id=_actor,parent_phone=_phone,
                  target_phone=_target,invitation_id=_new_id,status='pending',
                  version=slot.version+1,created_at=v_now,expires_at=v_now+interval '7 days',
                  updated_at=v_now
                WHERE slot.player_id=_player;
              ELSE
                INSERT INTO identity.family_invitations
                  (player_id,organization_id,parent_identity_id,parent_phone,target_phone,
                   invitation_id,status,version,created_at,expires_at,updated_at)
                VALUES (_player,v_scope,_actor,_phone,_target,_new_id,
                  'pending',1,v_now,v_now+interval '7 days',v_now);
              END IF;
              INSERT INTO identity.family_invitation_events
                (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
              VALUES (gen_random_uuid(),_player,v_scope,_new_id,_actor,'issued',v_now);
              INSERT INTO audit.security_events
                (id,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id)
              VALUES (gen_random_uuid(),v_now,_actor,_device,'mobile.family.invitation.issue','success',v_scope);
              RETURN QUERY SELECT _new_id,v_now,v_now+interval '7 days';
            EXCEPTION WHEN unique_violation THEN
              RAISE EXCEPTION 'family_invitation_conflict' USING ERRCODE='P0001';
            END
            $family_issue$;

            CREATE FUNCTION identity.cancel_family_invitation(
              _actor uuid, _phone text, _role text, _expires timestamptz,
              _device uuid, _invitation uuid)
            RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, pg_temp
            AS $family_cancel$
            DECLARE
              v_row identity.family_invitations%ROWTYPE;
              v_scope text;
              v_now timestamptz := statement_timestamp();
            BEGIN
              IF _actor IS NULL OR _phone IS NULL OR _role<>'parent'
                 OR _device IS NULL OR _invitation IS NULL OR _expires<=v_now
              THEN RAISE EXCEPTION 'family_invitation_denied' USING ERRCODE='P0001'; END IF;
              SELECT player_id INTO v_row.player_id FROM identity.family_invitations
                WHERE invitation_id=_invitation;
              IF NOT FOUND THEN RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001'; END IF;
              SELECT p.organization_id INTO v_scope FROM identity.mobile_identities i
              JOIN identity.mobile_record_links l ON l.identity_id=i.id
                AND l.role='parent' AND l.normalized_phone=i.normalized_phone
                AND l.collection='players' AND l.kind='guardian' AND l.record_id=v_row.player_id
              JOIN identity.canonical_contact_bindings b ON b.collection=l.collection AND b.source_id=l.record_id
                AND b.kind='guardian' AND b.normalized_phone=i.normalized_phone
              JOIN club.players p ON p.id=l.record_id
              WHERE i.id=_actor AND i.normalized_phone=_phone AND i.role='parent'
                AND i.is_active AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
              FOR SHARE OF i,l,b,p;
              IF NOT FOUND THEN RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001'; END IF;
              SELECT * INTO v_row FROM identity.family_invitations
                WHERE player_id=v_row.player_id FOR UPDATE;
              IF NOT FOUND OR v_row.invitation_id<>_invitation
                OR v_row.parent_identity_id<>_actor OR v_row.parent_phone<>_phone
                OR v_row.organization_id<>v_scope
              THEN RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001'; END IF;
              IF v_row.status='cancelled' THEN RETURN true; END IF;
              IF v_row.status<>'pending' OR v_row.expires_at<=v_now THEN
                RAISE EXCEPTION 'family_invitation_missing' USING ERRCODE='P0001';
              END IF;
              UPDATE identity.family_invitations slot SET
                status='cancelled',updated_at=v_now,version=slot.version+1
              WHERE slot.player_id=v_row.player_id;
              INSERT INTO identity.family_invitation_events
                (id,player_id,organization_id,invitation_id,actor_identity_id,action,occurred_at)
              VALUES (gen_random_uuid(),v_row.player_id,v_scope,_invitation,_actor,'cancelled',v_now);
              INSERT INTO audit.security_events
                (id,occurred_at,actor_identity_id,device_session_id,action,outcome,organization_id)
              VALUES (gen_random_uuid(),v_now,_actor,_device,'mobile.family.invitation.cancel','success',v_scope);
              RETURN true;
            END
            $family_cancel$;

            REVOKE ALL ON FUNCTION identity.issue_family_invitation(uuid,text,text,timestamptz,uuid,text,text,uuid) FROM PUBLIC;
            REVOKE ALL ON FUNCTION identity.cancel_family_invitation(uuid,text,text,timestamptz,uuid,uuid) FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally no data-destroying rollback after any real invitation.
        migrationBuilder.Sql("""
            DO $family_recovery$
            BEGIN
              IF EXISTS (SELECT 1 FROM identity.family_invitations)
                OR EXISTS (SELECT 1 FROM identity.family_invitation_events)
                OR EXISTS (SELECT 1 FROM audit.security_events WHERE action LIKE 'mobile.family.invitation.%')
              THEN RAISE EXCEPTION 'family_invitations_require_forward_recovery'; END IF;
            END
            $family_recovery$;
            DROP FUNCTION identity.cancel_family_invitation(uuid,text,text,timestamptz,uuid,uuid);
            DROP FUNCTION identity.issue_family_invitation(uuid,text,text,timestamptz,uuid,text,text,uuid);
            DROP TABLE identity.family_invitation_events;
            DROP TABLE identity.family_invitations;
            """);
    }
}
