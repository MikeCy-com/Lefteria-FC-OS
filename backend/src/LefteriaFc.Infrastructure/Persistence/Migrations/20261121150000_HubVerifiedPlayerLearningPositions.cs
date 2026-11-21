using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Super Admin-verified specialization of the four immutable roster positions.
/// General roster values remain canonical. Mobile never chooses its position.
/// Every change is version-checked, organization-scoped and audited.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121150000_HubVerifiedPlayerLearningPositions")]
public sealed class HubVerifiedPlayerLearningPositions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE hub.player_learning_specializations (
        organization_id varchar(32) NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        roster_position varchar(32) NOT NULL
          CHECK (roster_position IN ('Goalkeeper','Defender','Midfielder','Forward')),
        specialization varchar(24) NULL
          CHECK (specialization IN (
            'goalkeeper','defender','fullback','midfielder','winger','striker')),
        version bigint NOT NULL CHECK (version BETWEEN 1 AND 9223372036854775806),
        verified_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        verified_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        reason varchar(250) NOT NULL CHECK (length(btrim(reason)) BETWEEN 8 AND 250),
        PRIMARY KEY(organization_id,player_id),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      CREATE TABLE hub.player_learning_specialization_events (
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        roster_position varchar(32) NOT NULL,
        old_specialization varchar(24),
        new_specialization varchar(24),
        version bigint NOT NULL CHECK(version>=1),
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        reason varchar(250) NOT NULL,
        occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      CREATE INDEX ix_hub_player_learning_specialization_events
        ON hub.player_learning_specialization_events(
          organization_id,player_id,occurred_at DESC,id);
      ALTER TABLE hub.player_learning_specializations ENABLE ROW LEVEL SECURITY;
      ALTER TABLE hub.player_learning_specialization_events ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON TABLE hub.player_learning_specializations,
        hub.player_learning_specialization_events FROM PUBLIC;

      CREATE FUNCTION hub.assert_admin_learning_position_authority(
        _digest text,_owner text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $hub_learning_auth$
      DECLARE v_actor uuid;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'learning_admin_scope_denied' USING ERRCODE='42501'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id INTO v_actor
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp()
          AND a.is_active AND a.role='super_admin'
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF v_actor IS NULL THEN
          RAISE EXCEPTION 'learning_admin_super_admin_required' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $hub_learning_auth$;
      REVOKE ALL ON FUNCTION hub.assert_admin_learning_position_authority(
        text,text) FROM PUBLIC;

      CREATE FUNCTION hub.read_admin_player_learning_position(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $hub_learning_read$
      DECLARE v_roster text;v_override text;v_old_source text;
        v_version bigint;v_effective text;
      BEGIN
        PERFORM hub.assert_admin_learning_position_authority(_digest,_owner);
        IF _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'learning_player_invalid' USING ERRCODE='22023';
        END IF;
        SELECT p.position,s.specialization,s.roster_position,s.version
          INTO v_roster,v_override,v_old_source,v_version
          FROM club.players p
          LEFT JOIN hub.player_learning_specializations s
            ON s.organization_id=p.organization_id AND s.player_id=p.id
          WHERE p.id=_player AND p.organization_id=_owner
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          FOR SHARE OF p;
        IF NOT FOUND THEN
          RAISE EXCEPTION 'learning_player_not_found' USING ERRCODE='P0002';
        END IF;
        IF v_old_source IS DISTINCT FROM v_roster THEN v_override:=NULL; END IF;
        v_effective:=coalesce(v_override, CASE v_roster
          WHEN 'Goalkeeper' THEN 'goalkeeper'
          WHEN 'Defender' THEN 'defender'
          WHEN 'Midfielder' THEN 'midfielder'
          ELSE 'all' END);
        RETURN jsonb_build_object(
          'organization_id',_owner,'player_id',_player,
          'roster_position',v_roster,
          'specialization',v_override,
          'effective_position',v_effective,
          'needs_reverification',v_old_source IS NOT NULL
            AND v_old_source<>v_roster,
          'version',coalesce(v_version,0));
      END $hub_learning_read$;
      REVOKE ALL ON FUNCTION hub.read_admin_player_learning_position(
        text,text,text) FROM PUBLIC;

      CREATE FUNCTION hub.set_admin_player_learning_position(
        _digest text,_owner text,_player text,_expected_roster text,
        _expected_version bigint,_specialization text,_reason text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $hub_learning_set$
      DECLARE v_actor uuid;v_roster text;v_old_source text;
        v_old text;v_new text;v_version bigint;v_session uuid;
      BEGIN
        v_actor:=hub.assert_admin_learning_position_authority(_digest,_owner);
        IF _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]'
          OR _expected_roster NOT IN ('Goalkeeper','Defender','Midfielder','Forward')
          OR _expected_version IS NULL OR _expected_version<0
          OR _expected_version>=9223372036854775806
          OR _specialization NOT IN (
            'all','goalkeeper','defender','fullback','midfielder','winger','striker')
          OR _reason IS NULL OR length(_reason) NOT BETWEEN 8 AND 250
          OR _reason<>btrim(_reason) OR _reason ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'learning_position_invalid' USING ERRCODE='22023'; END IF;
        SELECT p.position INTO v_roster FROM club.players p
          WHERE p.id=_player AND p.organization_id=_owner
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'learning_player_not_found' USING ERRCODE='P0002'; END IF;
        IF v_roster<>_expected_roster
        THEN RAISE EXCEPTION 'learning_roster_changed' USING ERRCODE='40001'; END IF;
        v_new:=NULLIF(_specialization,'all');
        IF v_new IS NOT NULL AND NOT (
           (v_roster='Goalkeeper' AND v_new='goalkeeper') OR
           (v_roster='Defender' AND v_new IN ('defender','fullback')) OR
           (v_roster='Midfielder' AND v_new IN ('midfielder','winger')) OR
           (v_roster='Forward' AND v_new IN ('striker','winger'))
        ) THEN
          RAISE EXCEPTION 'learning_position_incompatible' USING ERRCODE='22023';
        END IF;
        SELECT s.specialization,s.roster_position,s.version
          INTO v_old,v_old_source,v_version
          FROM hub.player_learning_specializations s
          WHERE s.player_id=_player AND s.organization_id=_owner FOR UPDATE;
        v_version:=coalesce(v_version,0);
        IF _expected_version<>v_version
        THEN RAISE EXCEPTION 'learning_position_conflict' USING ERRCODE='40001'; END IF;
        IF v_version>0 AND v_old IS NOT DISTINCT FROM v_new
          AND v_old_source=v_roster
        THEN
          RETURN hub.read_admin_player_learning_position(_digest,_owner,_player);
        END IF;
        INSERT INTO hub.player_learning_specializations(
          organization_id,player_id,roster_position,specialization,
          version,verified_by,verified_at,reason)
        VALUES(_owner,_player,v_roster,v_new,v_version+1,
          v_actor,clock_timestamp(),_reason)
        ON CONFLICT(organization_id,player_id) DO UPDATE
        SET roster_position=EXCLUDED.roster_position,
          specialization=EXCLUDED.specialization,version=EXCLUDED.version,
          verified_by=EXCLUDED.verified_by,verified_at=EXCLUDED.verified_at,
          reason=EXCLUDED.reason;
        INSERT INTO hub.player_learning_specialization_events(
          id,organization_id,player_id,roster_position,
          old_specialization,new_specialization,version,actor_id,reason)
        VALUES(gen_random_uuid(),_owner,_player,v_roster,
          v_old,v_new,v_version+1,v_actor,_reason);
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor;
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,subject_digest,reason_code,actor_realm)
        VALUES(gen_random_uuid(),clock_timestamp(),v_actor,v_session,
          'admin.hub.player_learning_position','succeeded',_owner,
          md5(_player), 'position_'||coalesce(v_new,'all'),'admin');
        RETURN hub.read_admin_player_learning_position(_digest,_owner,_player);
      END $hub_learning_set$;
      REVOKE ALL ON FUNCTION hub.set_admin_player_learning_position(
        text,text,text,text,bigint,text,text) FROM PUBLIC;

      -- Existing mobile reader keeps the same signature, grants and strict
      -- canonical player proof. A stale specialization is NEVER accepted.
      CREATE OR REPLACE FUNCTION hub.assert_current_quiz_player(
        _identity uuid,_phone text,_role text,_expires timestamptz,_owner text
      ) RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp
      AS $quiz_auth$
      DECLARE v_position text;v_player_id text;v_specialization text;
      BEGIN
        IF _identity IS NULL OR _phone IS NULL
          OR _phone !~ '^[1-9][0-9]{7,14}$'
          OR _role IS DISTINCT FROM 'player'
          OR _expires IS NULL OR _expires<=clock_timestamp()
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
        THEN RAISE EXCEPTION 'quiz_scope_denied' USING ERRCODE='42501'; END IF;
        SELECT p.id,p.position INTO STRICT v_player_id,v_position
          FROM identity.mobile_identities i
          JOIN identity.mobile_record_links l
            ON l.identity_id=i.id AND l.role='player'
              AND l.normalized_phone=i.normalized_phone
              AND l.collection='players' AND l.kind='player_own'
          JOIN identity.canonical_contact_bindings b
            ON b.collection=l.collection AND b.kind=l.kind
              AND b.source_id=l.record_id
              AND b.normalized_phone=i.normalized_phone
          JOIN club.players p ON p.id=l.record_id
            AND p.organization_id=_owner
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          WHERE i.id=_identity AND i.normalized_phone=_phone
            AND i.role='player' AND i.is_active
          FOR SHARE OF i,l,b,p;
        SELECT s.specialization INTO v_specialization
          FROM hub.player_learning_specializations s
          WHERE s.organization_id=_owner AND s.player_id=v_player_id
            AND s.roster_position=v_position;
        RETURN coalesce(v_specialization, CASE v_position
          WHEN 'Goalkeeper' THEN 'goalkeeper'
          WHEN 'Defender' THEN 'defender'
          WHEN 'Midfielder' THEN 'midfielder'
          ELSE 'all' END);
      EXCEPTION WHEN no_data_found OR too_many_rows THEN
        RAISE EXCEPTION 'quiz_identity_denied' USING ERRCODE='42501';
      END $quiz_auth$;
      REVOKE ALL ON FUNCTION hub.assert_current_quiz_player(
        uuid,text,text,timestamptz,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
          -- Restore the original verified roster reader before dropping
          -- the specialization table; it never accepted a client position.
          CREATE OR REPLACE FUNCTION hub.assert_current_quiz_player(
            _identity uuid,_phone text,_role text,_expires timestamptz,_owner text)
          RETURNS text LANGUAGE plpgsql VOLATILE SECURITY DEFINER
          SET search_path=pg_catalog,pg_temp
          AS $quiz_auth$
          DECLARE v_position text;
          BEGIN
            IF _identity IS NULL OR _phone IS NULL
              OR _phone !~ '^[1-9][0-9]{7,14}$'
              OR _role IS DISTINCT FROM 'player'
              OR _expires IS NULL OR _expires<=clock_timestamp()
              OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
            THEN
              RAISE EXCEPTION 'quiz_scope_denied' USING ERRCODE='42501';
            END IF;
            -- Recheck the live canonical player ownership and exact
            -- organization. An active role alone cannot prove a roster link,
            -- selected position or cross-club membership.
            SELECT p.position INTO STRICT v_position
              FROM identity.mobile_identities i
              JOIN identity.mobile_record_links l
                ON l.identity_id=i.id AND l.role='player'
                  AND l.normalized_phone=i.normalized_phone
                  AND l.collection='players' AND l.kind='player_own'
              JOIN identity.canonical_contact_bindings b
                ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id
                  AND b.normalized_phone=i.normalized_phone
              JOIN club.players p ON p.id=l.record_id
                AND p.organization_id=_owner
                AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
              WHERE i.id=_identity AND i.normalized_phone=_phone
                AND i.role='player' AND i.is_active
              FOR SHARE OF i,l,b,p;
            RETURN CASE v_position
              WHEN 'Defender' THEN 'defender'
              WHEN 'Midfielder' THEN 'midfielder'
              WHEN 'Goalkeeper' THEN 'goalkeeper'
              ELSE 'all'
            END;
          EXCEPTION WHEN no_data_found OR too_many_rows THEN
            RAISE EXCEPTION 'quiz_identity_denied' USING ERRCODE='42501';
          END $quiz_auth$;
          REVOKE ALL ON FUNCTION hub.assert_current_quiz_player(
            uuid,text,text,timestamptz,text) FROM PUBLIC;


          DROP FUNCTION hub.set_admin_player_learning_position(
            text,text,text,text,bigint,text,text);
          DROP FUNCTION hub.read_admin_player_learning_position(text,text,text);
          DROP FUNCTION hub.assert_admin_learning_position_authority(text,text);
          DROP TABLE hub.player_learning_specialization_events;
          DROP TABLE hub.player_learning_specializations;
          """);
}
