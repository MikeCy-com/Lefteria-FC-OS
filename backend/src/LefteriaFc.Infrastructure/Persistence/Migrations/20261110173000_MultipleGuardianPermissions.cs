using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Direct phone-scoped guardian permissions: no recipient invitation or
/// acceptance workflow. A phone grant never authenticates its holder.
/// Existing one-child player delegation remains independent.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110173000_MultipleGuardianPermissions")]
public sealed class MultipleGuardianPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE identity.family_guardian_permissions (
        id uuid NOT NULL PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        issuer_identity_id uuid NOT NULL REFERENCES identity.mobile_identities(id)
          ON DELETE RESTRICT,
        source_permission_id uuid REFERENCES identity.family_guardian_permissions(id)
          ON DELETE RESTRICT,
        target_phone varchar(15) NOT NULL
          CHECK(target_phone ~ '^[1-9][0-9]{7,14}$'),
        target_identity_id uuid REFERENCES identity.mobile_identities(id)
          ON DELETE RESTRICT,
        status varchar(16) NOT NULL DEFAULT 'granted'
          CHECK(status IN ('granted','revoked')),
        created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        expires_at timestamptz,
        accepted_at timestamptz,
        revoked_at timestamptz,
        CHECK(expires_at IS NULL),
        CHECK(accepted_at IS NULL),
        CONSTRAINT family_guardian_permission_player_fk
          FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      CREATE UNIQUE INDEX uq_family_guardian_live_recipient
        ON identity.family_guardian_permissions(
          organization_id,player_id,target_phone)
        WHERE status='granted';
      CREATE INDEX ix_family_guardian_issuer
        ON identity.family_guardian_permissions(issuer_identity_id,created_at DESC);
      CREATE INDEX ix_family_guardian_recipient
        ON identity.family_guardian_permissions(target_phone,status);
      ALTER TABLE identity.family_guardian_permissions ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON identity.family_guardian_permissions FROM PUBLIC;

      CREATE FUNCTION identity.issue_guardian_permission(
        _actor uuid,_phone text,_role text,_expires timestamptz,
        _device uuid,_player text,_target text,_request uuid)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $issue$
      DECLARE
        v_owner text; v_source uuid;
        v_existing identity.family_guardian_permissions%ROWTYPE;
        v_now timestamptz:=statement_timestamp();
      BEGIN
        IF _actor IS NULL OR _device IS NULL OR _request IS NULL
          OR _request='00000000-0000-0000-0000-000000000000'::uuid
          OR _role IS DISTINCT FROM 'parent' OR _expires IS NULL OR _expires<=v_now
          OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
          OR _target IS NULL OR _target !~ '^[1-9][0-9]{7,14}$'
          OR _target=_phone
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
        THEN RAISE EXCEPTION 'guardian_permission_denied' USING ERRCODE='42501'; END IF;

        -- Parent must currently own an independently verified canonical
        -- guardian binding for this exact active child.
        SELECT p.organization_id INTO v_owner
        FROM identity.mobile_identities i
        JOIN identity.mobile_record_links l ON l.identity_id=i.id
          AND l.role='parent' AND l.normalized_phone=i.normalized_phone
          AND l.collection='players' AND l.kind='guardian'
          AND l.record_id=_player
        JOIN identity.canonical_contact_bindings b
          ON b.collection=l.collection AND b.source_id=l.record_id
          AND b.kind=l.kind AND b.normalized_phone=i.normalized_phone
        JOIN club.players p ON p.id=l.record_id
        WHERE i.id=_actor AND i.normalized_phone=_phone
          AND i.role='parent' AND i.is_active
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
        FOR SHARE OF i,l,b,p;
        IF NOT FOUND THEN
          -- A previously phone-granted Parent can also share this same
          -- child. Store the ancestor permission for revocation cascades.
          SELECT granted.organization_id,granted.permission_id
            INTO v_owner,v_source
          FROM identity.current_phone_granted_children(
            _actor,_phone,_role,_expires) granted
          WHERE granted.player_id=_player
          ORDER BY granted.permission_id LIMIT 1;
          IF NOT FOUND THEN RAISE EXCEPTION 'guardian_permission_denied'
            USING ERRCODE='42501'; END IF;
        END IF;

        -- Multiple recipients for one player are allowed, but never
        -- duplicate direct grants to one phone for the same child.
        PERFORM pg_advisory_xact_lock(
          hashtextextended('lefteria:family:guardian:'||v_owner||':'||_player,0));
        -- A child/player phone must never be recruited into the guardian
        -- recipient path. Existing parent recipients remain eligible.
        IF EXISTS(
          SELECT 1 FROM identity.canonical_contact_bindings c
          WHERE c.kind='player_own' AND c.normalized_phone=_target
        ) OR EXISTS(
          SELECT 1 FROM identity.mobile_identities i
          WHERE i.normalized_phone=_target
            AND i.role IN ('player','coach','management')
        ) THEN RAISE EXCEPTION 'guardian_recipient_role_conflict'
          USING ERRCODE='23505'; END IF;

        SELECT * INTO v_existing FROM identity.family_guardian_permissions
          WHERE id=_request FOR UPDATE;
        IF FOUND THEN
          IF v_existing.issuer_identity_id<>_actor
            OR v_existing.player_id<>_player OR
            v_existing.organization_id<>v_owner OR
            v_existing.target_phone<>_target
          THEN RAISE EXCEPTION 'guardian_permission_replay_mismatch'
            USING ERRCODE='23505'; END IF;
          RETURN jsonb_build_object('id',v_existing.id,
            'player_id',_player,'organization_id',v_owner,
            'kind','guardian','status',v_existing.status,
            'expires_at',v_existing.expires_at,'replay',true);
        END IF;
        IF (SELECT count(*) FROM identity.family_guardian_permissions
          WHERE organization_id=v_owner AND player_id=_player
            AND status='granted')>=20 THEN
          RAISE EXCEPTION 'guardian_permission_limit' USING ERRCODE='54000'; END IF;
        IF EXISTS(SELECT 1 FROM identity.family_guardian_permissions
          WHERE organization_id=v_owner AND player_id=_player
            AND target_phone=_target AND status='granted')
        THEN RAISE EXCEPTION 'guardian_permission_already_exists'
          USING ERRCODE='23505'; END IF;

        INSERT INTO identity.family_guardian_permissions(
          id,organization_id,player_id,issuer_identity_id,
          source_permission_id,target_phone,status,created_at,expires_at)
        VALUES(_request,v_owner,_player,_actor,
          v_source,_target,'granted',v_now,NULL);
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,action,
          outcome,organization_id)
        VALUES(gen_random_uuid(),v_now,_actor,_device,
          'mobile.family.guardian.permission.request','success',v_owner);
        RETURN jsonb_build_object('id',_request,'player_id',_player,
          'organization_id',v_owner,'kind','guardian',
          'status','granted','expires_at',NULL,
          'replay',false);
      END $issue$;

      CREATE FUNCTION identity.read_issued_guardian_permissions(
        _actor uuid,_phone text,_role text,_expires timestamptz)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read$
      DECLARE v_ok boolean; v_items jsonb;
      BEGIN
        SELECT EXISTS(SELECT 1 FROM identity.mobile_identities i
          JOIN identity.mobile_record_links l ON l.identity_id=i.id
            AND l.role='parent' AND l.normalized_phone=i.normalized_phone
            AND l.collection='players' AND l.kind='guardian'
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.kind='guardian'
            AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
          JOIN club.players p ON p.id=l.record_id
          WHERE i.id=_actor AND i.normalized_phone=_phone
            AND i.role='parent' AND _role='parent' AND i.is_active
            AND _expires>statement_timestamp()
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred)
        INTO v_ok;
        IF NOT v_ok THEN
          SELECT EXISTS(
            SELECT 1 FROM identity.current_phone_granted_children(
              _actor,_phone,_role,_expires)
          ) INTO v_ok;
        END IF;
        IF NOT v_ok THEN RAISE EXCEPTION 'guardian_permission_denied'
          USING ERRCODE='42501'; END IF;

        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',g.id,'player_id',g.player_id,
          'organization_id',g.organization_id,'kind','guardian',
          'status',g.status,
          'expires_at',g.expires_at,
          'recipient_hint','••••'||right(g.target_phone,4))
          ORDER BY g.created_at DESC,g.id),'[]'::jsonb)
        INTO v_items
        FROM identity.family_guardian_permissions g
        WHERE g.issuer_identity_id=_actor
          AND g.status='granted'
          AND EXISTS(
            SELECT 1 FROM identity.valid_guardian_phone_grants() valid
            WHERE valid.permission_id=g.id);
        IF jsonb_array_length(v_items)>100
        THEN RAISE EXCEPTION 'guardian_permission_limit' USING ERRCODE='54000'; END IF;
        RETURN jsonb_build_object('items',v_items);
      END $read$;

      CREATE FUNCTION identity.revoke_issued_guardian_permission(
        _actor uuid,_phone text,_role text,_expires timestamptz,
        _device uuid,_permission uuid)
      RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $revoke$
      DECLARE v_item identity.family_guardian_permissions%ROWTYPE;
        v_now timestamptz:=statement_timestamp();
      BEGIN
        IF _actor IS NULL OR _device IS NULL OR _permission IS NULL
          OR _role<>'parent' OR _expires IS NULL OR _expires<=v_now
          OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
        THEN RAISE EXCEPTION 'guardian_permission_denied' USING ERRCODE='42501'; END IF;
        -- Read target identity first without holding its row lock;
        -- canonical parent/child rows are locked before the permission row
        -- in issue, accept and revoke, avoiding inverted lock ordering.
        SELECT * INTO v_item FROM identity.family_guardian_permissions
          WHERE id=_permission AND issuer_identity_id=_actor
            AND status='granted';
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_permission_missing'
          USING ERRCODE='42501'; END IF;

        -- Verify this permission is still in a live current-parent
        -- lineage. A revoked ancestor immediately blocks delegated grants.
        PERFORM 1 FROM identity.valid_guardian_phone_grants() valid
          WHERE valid.permission_id=_permission
            AND valid.issuer_identity_id=_actor
            AND valid.player_id=v_item.player_id
            AND valid.organization_id=v_item.organization_id;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_permission_denied'
          USING ERRCODE='42501'; END IF;

        PERFORM 1 FROM identity.family_guardian_permissions
          WHERE id=_permission AND issuer_identity_id=_actor
            AND player_id=v_item.player_id
            AND organization_id=v_item.organization_id
            AND status='granted'
          FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_permission_missing'
          USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM identity.valid_guardian_phone_grants() valid
          WHERE valid.permission_id=_permission
            AND valid.issuer_identity_id=_actor;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_permission_denied'
          USING ERRCODE='42501'; END IF;

        -- A guardian may have shared with additional guardians. Revoking
        -- a permission revokes its dependent permissions for this one
        -- child, but never touches siblings or unrelated source grants.
        WITH RECURSIVE affected(id,path,depth) AS (
          SELECT _permission,ARRAY[_permission],1
          UNION ALL
          SELECT g.id,affected.path||g.id,affected.depth+1
          FROM affected JOIN identity.family_guardian_permissions g
            ON g.source_permission_id=affected.id
          WHERE affected.depth<20 AND NOT g.id=ANY(affected.path)
        )
        UPDATE identity.family_guardian_permissions g SET
          status='revoked',revoked_at=v_now
        WHERE g.id IN (SELECT id FROM affected) AND g.status='granted';
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,action,
          outcome,organization_id)
        VALUES(gen_random_uuid(),v_now,_actor,_device,
          'mobile.family.guardian.permission.revoke','success',v_item.organization_id);
        RETURN true;
      END $revoke$;

      REVOKE ALL ON FUNCTION identity.issue_guardian_permission(
        uuid,text,text,timestamptz,uuid,text,text,uuid) FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.read_issued_guardian_permissions(
        uuid,text,text,timestamptz) FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.revoke_issued_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM identity.family_guardian_permissions)
        THEN RAISE EXCEPTION 'guardian_permission_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION identity.revoke_issued_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid);
      DROP FUNCTION identity.read_issued_guardian_permissions(
        uuid,text,text,timestamptz);
      DROP FUNCTION identity.issue_guardian_permission(
        uuid,text,text,timestamptz,uuid,text,text,uuid);
      DROP TABLE identity.family_guardian_permissions;
    """);
}
