using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private per-child Academy biography, position preference and sanitized JPEG
/// with exact all-membership city/location administration authority.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261122235900_AcademyPrivatePlayerProfiles")]
public sealed class AcademyPrivatePlayerProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      -- Private staff-only biography and sanitized portrait. Canonical player
      -- identity, registration, guardian links and invoices stay unchanged.
      CREATE TABLE club.academy_player_private_profiles (
        player_id varchar(128) COLLATE "C" PRIMARY KEY
          REFERENCES club.players(id) ON DELETE RESTRICT,
        organization_id varchar(32) NOT NULL DEFAULT 'lefteria_fc_academy'
          CHECK(organization_id='lefteria_fc_academy'),
        biography varchar(1000) NOT NULL DEFAULT '',
        preferred_position varchar(80) NOT NULL DEFAULT '',
        preferred_number smallint CHECK(preferred_number BETWEEN 1 AND 99),
        portrait_jpeg bytea,
        revision integer NOT NULL DEFAULT 1 CHECK(revision BETWEEN 1 AND 2000000000),
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id) ON DELETE RESTRICT,
        CONSTRAINT academy_portrait_sanitized_jpeg CHECK (
          portrait_jpeg IS NULL OR (
            octet_length(portrait_jpeg) BETWEEN 4 AND 1048576 AND
            get_byte(portrait_jpeg,0)=255 AND get_byte(portrait_jpeg,1)=216 AND
            get_byte(portrait_jpeg,2)=255 AND
            get_byte(portrait_jpeg,octet_length(portrait_jpeg)-2)=255 AND
            get_byte(portrait_jpeg,octet_length(portrait_jpeg)-1)=217
          ))
      );
      ALTER TABLE club.academy_player_private_profiles ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON club.academy_player_private_profiles FROM PUBLIC;

      -- Never expose this helper to a runtime role. It is used solely inside
      -- audited definer operations and enforces ALL group memberships, not one
      -- matching group at a time. City and location grant locks are preserved.
      CREATE FUNCTION club.authorize_academy_private_profile(
        _digest text,_player text,_write boolean
      ) RETURNS TABLE(actor_id uuid,session_id uuid,player_name text)
      LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $authorize_profile$
      DECLARE v_actor uuid;v_role text;v_session uuid;v_name text;
        v_team text;v_primary text;v_count integer;v_ok boolean;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[a-f0-9]{64}$'
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _write IS NULL
        THEN RAISE EXCEPTION 'player_profile_invalid_scope' USING ERRCODE='22023'; END IF;
        SELECT a.actor_id,a.account_role INTO STRICT v_actor,v_role
          FROM club.authorize_admin_football(_digest,'lefteria_fc_academy') a;
        SELECT s.id INTO v_session FROM identity.admin_sessions s
          WHERE s.token_digest=_digest AND s.admin_account_id=v_actor
            AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp();
        SELECT p.name,p.team_id,p.primary_academy_group_id
          INTO v_name,v_team,v_primary
        FROM club.players p
        WHERE p.id=_player AND p.organization_id='lefteria_fc_academy'
          AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
          AND p.team_type='Academy'
        FOR SHARE;
        IF v_session IS NULL OR v_name IS NULL
          OR NOT club.admin_roster_player_visible(v_actor,v_role,
            'lefteria_fc_academy',_player,v_team,v_primary)
        THEN RAISE EXCEPTION 'player_profile_access_denied' USING ERRCODE='42501'; END IF;

        IF _write AND v_role<>'super_admin' THEN
          WITH memberships AS (
            SELECT v_primary AS group_id WHERE v_primary IS NOT NULL
            UNION
            SELECT m.academy_group_id FROM club.player_academy_groups m
              WHERE m.player_id=_player AND m.organization_id='lefteria_fc_academy'
          )
          SELECT count(*)::int,coalesce(bool_and(
            g.scope_id IS NOT NULL AND g.location_id IS NOT NULL AND
            club.admin_football_allowed(v_actor,v_role,'lefteria_fc_academy',
              g.scope_id,g.location_id,true)),false)
          INTO v_count,v_ok
          FROM memberships m
          LEFT JOIN club.academy_group_location_assignments g
            ON g.group_id=m.group_id
              AND g.organization_id='lefteria_fc_academy';
          IF v_count=0 OR NOT v_ok THEN
            RAISE EXCEPTION 'player_profile_write_scope_denied' USING ERRCODE='42501';
          END IF;
        END IF;
        RETURN QUERY SELECT v_actor,v_session,v_name;
      END $authorize_profile$;

      CREATE FUNCTION club.read_admin_academy_player_profile(
        _digest text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_profile$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_details jsonb;
      BEGIN
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,false);
        SELECT jsonb_build_object(
          'organization_id','lefteria_fc_academy','player_id',p.id,
          'official_name',v_name,'official_number',p.number,
          'official_position',p.position,
          'biography',coalesce(a.biography,''),
          'preferred_position',coalesce(a.preferred_position,''),
          'preferred_number',a.preferred_number,
          'has_portrait',a.portrait_jpeg IS NOT NULL,
          'revision',coalesce(a.revision,0),
          'updated_at',a.updated_at)
        INTO v_details
        FROM club.players p
        LEFT JOIN club.academy_player_private_profiles a ON a.player_id=p.id
        WHERE p.id=_player AND p.organization_id='lefteria_fc_academy';
        RETURN v_details;
      END $read_profile$;

      CREATE FUNCTION club.write_admin_academy_player_profile(
        _digest text,_player text,_expected_revision integer,
        _bio text,_position text,_number integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $write_profile$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_next integer;
      BEGIN
        IF _expected_revision IS NULL OR _expected_revision NOT BETWEEN 0 AND 1999999999
          OR _bio IS NULL OR length(_bio)>1000
          OR _position IS NULL OR length(_position)>80
          OR _bio<>btrim(_bio) OR _position<>btrim(_position)
          OR _bio LIKE '%'||chr(13)||'%' OR _position ~ '[[:cntrl:]]'
          OR (_number IS NOT NULL AND _number NOT BETWEEN 1 AND 99)
        THEN RAISE EXCEPTION 'player_profile_invalid_fields' USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,true);
        IF _expected_revision=0 THEN
          INSERT INTO club.academy_player_private_profiles(
            player_id,biography,preferred_position,preferred_number,
            updated_by,revision)
          VALUES(_player,_bio,_position,_number,v_actor,1)
          ON CONFLICT(player_id) DO NOTHING
          RETURNING revision INTO v_next;
        ELSE
          UPDATE club.academy_player_private_profiles
            SET biography=_bio,preferred_position=_position,
              preferred_number=_number,updated_by=v_actor,
              updated_at=clock_timestamp(),revision=revision+1
          WHERE player_id=_player AND revision=_expected_revision
          RETURNING revision INTO v_next;
        END IF;
        IF v_next IS NULL THEN
          RAISE EXCEPTION 'player_profile_revision_conflict' USING ERRCODE='23505';
        END IF;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.academy.player.profile.update','succeeded',
          'lefteria_fc_academy','scoped_private_edit');
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy',
          'player_id',_player,'revision',v_next,'updated',true);
      END $write_profile$;

      CREATE FUNCTION club.read_admin_academy_player_portrait(
        _digest text,_player text
      ) RETURNS bytea LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $read_portrait$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_photo bytea;
      BEGIN
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,false);
        SELECT a.portrait_jpeg INTO v_photo
          FROM club.academy_player_private_profiles a WHERE a.player_id=_player;
        RETURN v_photo;
      END $read_portrait$;

      CREATE FUNCTION club.write_admin_academy_player_portrait(
        _digest text,_player text,_jpeg bytea
      ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $write_portrait$
      DECLARE v_actor uuid;v_session uuid;v_name text;v_next integer;
      BEGIN
        IF _jpeg IS NOT NULL AND
          (octet_length(_jpeg) NOT BETWEEN 4 AND 1048576
           OR get_byte(_jpeg,0)<>255 OR get_byte(_jpeg,1)<>216
           OR get_byte(_jpeg,2)<>255
           OR get_byte(_jpeg,octet_length(_jpeg)-2)<>255
           OR get_byte(_jpeg,octet_length(_jpeg)-1)<>217)
        THEN RAISE EXCEPTION 'player_portrait_not_sanitized' USING ERRCODE='22023'; END IF;
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_name
          FROM club.authorize_academy_private_profile(_digest,_player,true);
        INSERT INTO club.academy_player_private_profiles(
          player_id,portrait_jpeg,updated_by,revision)
        VALUES(_player,_jpeg,v_actor,1)
        ON CONFLICT(player_id) DO UPDATE SET
          portrait_jpeg=EXCLUDED.portrait_jpeg,updated_by=v_actor,
          updated_at=clock_timestamp(),
          revision=club.academy_player_private_profiles.revision+1
        RETURNING revision INTO v_next;
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,device_session_id,
          action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          CASE WHEN _jpeg IS NULL THEN 'admin.academy.player.portrait.remove'
             ELSE 'admin.academy.player.portrait.update' END,
          'succeeded','lefteria_fc_academy','scoped_private_photo');
        RETURN true;
      END $write_portrait$;

      REVOKE ALL ON FUNCTION club.authorize_academy_private_profile(text,text,boolean),
        club.read_admin_academy_player_profile(text,text),
        club.write_admin_academy_player_profile(text,text,integer,text,text,integer),
        club.read_admin_academy_player_portrait(text,text),
        club.write_admin_academy_player_portrait(text,text,bytea)
        FROM PUBLIC;

    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""

      DO $profile_recovery$ BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_player_private_profiles LIMIT 1) THEN
          RAISE EXCEPTION 'academy_private_profile_requires_forward_recovery'; END IF;
      END $profile_recovery$;
      DROP FUNCTION club.write_admin_academy_player_portrait(text,text,bytea);
      DROP FUNCTION club.read_admin_academy_player_portrait(text,text);
      DROP FUNCTION club.write_admin_academy_player_profile(text,text,integer,text,text,integer);
      DROP FUNCTION club.read_admin_academy_player_profile(text,text);
      DROP FUNCTION club.authorize_academy_private_profile(text,text,boolean);
      DROP TABLE club.academy_player_private_profiles;
    """);
}
