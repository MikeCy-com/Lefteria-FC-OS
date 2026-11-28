using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Restricted lifecycle for unlinked Academy players with settled
/// financial records. No implicit restoration of existing identity access.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128206000_AcademyRestrictedPlayerLifecycle")]
public sealed class AcademyRestrictedPlayerLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_player_lifecycle_events (
        request_id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL CHECK(organization_id='lefteria_fc_academy'),
        player_id varchar(128) COLLATE "C" NOT NULL,
        revision integer NOT NULL CHECK(revision>0),
        action varchar(16) NOT NULL CHECK(action IN ('archive','reactivate')),
        reason varchar(240) NOT NULL CHECK(length(btrim(reason)) BETWEEN 8 AND 240),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,player_id) REFERENCES club.players(organization_id,id) ON DELETE RESTRICT,
        UNIQUE(organization_id,player_id,revision)
      );
      ALTER TABLE club.academy_player_lifecycle_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_player_lifecycle_events FROM PUBLIC;
      CREATE TRIGGER academy_player_lifecycle_events_immutable BEFORE UPDATE OR DELETE
        ON club.academy_player_lifecycle_events FOR EACH ROW
        EXECUTE FUNCTION academy_intake.reject_evidence_mutation();
      CREATE FUNCTION club.admin_academy_player_lifecycle(
        _digest text,_player text,_action text,_expected integer,_request uuid,_reason text)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $lifecycle$
      DECLARE v_actor uuid;v_session uuid;v_role text;v_player club.players%ROWTYPE;
        v_revision integer;v_linked boolean;v_open boolean;v_state text;v_allowed boolean;
        v_existing club.academy_player_lifecycle_events%ROWTYPE;
        v_owner constant text:='lefteria_fc_academy';
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player<>btrim(_player) OR _player ~ '[[:cntrl:]]'
          OR _action IS NULL OR _action NOT IN ('read','archive','reactivate')
          OR (_action<>'read' AND (_expected IS NULL OR _expected<0
            OR _request IS NULL OR _request='00000000-0000-0000-0000-000000000000'::uuid
            OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 240
            OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'))
        THEN RAISE EXCEPTION 'lifecycle_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,v_owner);
        SELECT a.id,s.id,a.role INTO v_actor,v_session,v_role
          FROM identity.admin_sessions s JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id AND g.organization_id=v_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin'
        THEN RAISE EXCEPTION 'lifecycle_denied' USING ERRCODE='42501'; END IF;
        SELECT * INTO v_player FROM club.players p
          WHERE p.organization_id=v_owner AND p.id=_player AND p.team_type='Academy' FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'lifecycle_denied' USING ERRCODE='42501'; END IF;
        SELECT coalesce(max(revision),0) INTO v_revision FROM club.academy_player_lifecycle_events
          WHERE organization_id=v_owner AND player_id=_player;
        -- Any recorded identity relationship is a blocker in this initial
        -- lifecycle slice, even if expired or revoked. Never reactivate old
        -- entitlement by changing just the roster flags.
        SELECT EXISTS(SELECT 1 FROM identity.canonical_contact_bindings b
            WHERE b.collection='players' AND b.source_id=_player)
          OR EXISTS(SELECT 1 FROM identity.mobile_record_links l
            WHERE l.collection='players' AND l.record_id=_player)
          OR EXISTS(SELECT 1 FROM identity.family_guardian_permissions g
            WHERE g.organization_id=v_owner AND g.player_id=_player)
          OR EXISTS(SELECT 1 FROM identity.family_invitations f
            WHERE f.organization_id=v_owner AND f.player_id=_player)
          OR EXISTS(SELECT 1 FROM identity.family_delegated_player_grants f
            WHERE f.organization_id=v_owner AND f.player_id=_player)
          OR EXISTS(SELECT 1 FROM academy_intake.guardian_proof_reviews p WHERE p.player_id=_player)
          INTO v_linked;
        SELECT EXISTS(SELECT 1 FROM finance.training_fee_issues f
            WHERE f.organization_id=v_owner AND f.player_id=_player AND f.status IN ('issued','review_required'))
          OR EXISTS(SELECT 1 FROM finance.general_charge_balances f
            WHERE f.organization_id=v_owner AND f.player_id=_player AND f.status NOT IN ('paid','cancelled'))
          INTO v_open;
        v_state:=CASE WHEN v_player.is_transferred THEN 'transferred'
          WHEN v_player.is_archived AND NOT v_player.is_active THEN 'archived'
          WHEN v_player.is_active AND NOT v_player.is_archived THEN 'active' ELSE 'review' END;
        v_allowed:=NOT v_linked AND NOT v_open AND v_state IN ('active','archived');
        IF _action='read' THEN
          RETURN jsonb_build_object('organization_id',v_owner,'player_id',_player,'name',v_player.name,
            'state',v_state,'revision',v_revision,'identity_review_required',v_linked,
            'financial_review_required',v_open,'can_change',v_allowed);
        END IF;
        SELECT * INTO v_existing FROM club.academy_player_lifecycle_events WHERE request_id=_request;
        IF FOUND THEN
          IF v_existing.organization_id<>v_owner OR v_existing.player_id<>_player OR v_existing.actor_id<>v_actor
            OR v_existing.action<>_action OR v_existing.reason<>_reason OR v_existing.revision<>_expected+1
            OR v_revision<>v_existing.revision
            OR v_state<>(CASE WHEN _action='archive' THEN 'archived' ELSE 'active' END)
          THEN RAISE EXCEPTION 'lifecycle_conflict' USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('organization_id',v_owner,'player_id',_player,
            'state',v_state,'revision',v_revision,'replay',true);
        END IF;
        IF NOT v_allowed OR v_revision<>_expected OR
          (_action='archive' AND v_state<>'active') OR (_action='reactivate' AND v_state<>'archived')
        THEN RAISE EXCEPTION 'lifecycle_review_required' USING ERRCODE='23505'; END IF;
        UPDATE club.players SET is_active=(_action='reactivate'),is_archived=(_action='archive')
          WHERE organization_id=v_owner AND id=_player;
        INSERT INTO club.academy_player_lifecycle_events(request_id,organization_id,player_id,revision,action,reason,actor_id)
          VALUES(_request,v_owner,_player,v_revision+1,_action,_reason,v_actor);
        INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.player.lifecycle.'||_action,'succeeded',v_owner,'unlinked_settled_player');
        RETURN jsonb_build_object('organization_id',v_owner,'player_id',_player,
          'state',CASE WHEN _action='archive' THEN 'archived' ELSE 'active' END,'revision',v_revision+1,'replay',false);
      END $lifecycle$;
      REVOKE ALL ON FUNCTION club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text) FROM PUBLIC;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_player_lifecycle_events)
        THEN RAISE EXCEPTION 'player_lifecycle_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text);
      DROP TABLE club.academy_player_lifecycle_events;
      """);
}
