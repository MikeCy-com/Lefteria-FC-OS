using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Private per-player birthday card preferences. All publication and social
/// posting remain disabled until a separate consent/approval workflow exists.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113130000_PlayerBirthdayPrivatePreferences")]
public sealed class PlayerBirthdayPrivatePreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.player_birthday_card_preferences(
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        player_id varchar(128) COLLATE "C" NOT NULL,
        version integer NOT NULL DEFAULT 1 CHECK(version BETWEEN 1 AND 10000),
        disabled boolean NOT NULL DEFAULT true,
        wish_text varchar(500) NOT NULL DEFAULT ''
          CHECK(wish_text !~ '[[:cntrl:]]'),
        background_color varchar(7) NOT NULL DEFAULT '#F5A623'
          CHECK(background_color ~ '^#[0-9A-Fa-f]{6}$'),
        emoji varchar(16) NOT NULL DEFAULT '🎂'
          CHECK(emoji IN ('🎂','🎉','🎈','🎁','🌟','⚽','🏆','❤️')),
        updated_by uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        PRIMARY KEY(organization_id,player_id),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      REVOKE ALL ON club.player_birthday_card_preferences FROM PUBLIC;
      ALTER TABLE club.player_birthday_card_preferences ENABLE ROW LEVEL SECURITY;

      CREATE TABLE club.player_birthday_card_preference_events(
        id uuid PRIMARY KEY,
        organization_id varchar(32) NOT NULL,
        player_id varchar(128) COLLATE "C" NOT NULL,
        version integer NOT NULL,
        actor_id uuid NOT NULL REFERENCES identity.admin_accounts(id),
        recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
        UNIQUE(organization_id,player_id,version),
        FOREIGN KEY(organization_id,player_id)
          REFERENCES club.players(organization_id,id) ON DELETE RESTRICT
      );
      REVOKE ALL ON club.player_birthday_card_preference_events FROM PUBLIC;
      ALTER TABLE club.player_birthday_card_preference_events ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION club.require_player_birthday_manager(
        _digest text,_owner text,_player text
      ) RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $birthday_authority$
      DECLARE v_actor uuid;v_role text;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _owner NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'birthday_invalid_scope' USING ERRCODE='22023';
        END IF;
        PERFORM identity.lock_admin_club_authority(_digest,_owner);
        SELECT a.id,a.role INTO v_actor,v_role
        FROM identity.admin_sessions s
        JOIN identity.admin_accounts a ON a.id=s.admin_account_id
        JOIN identity.admin_organization_grants g
          ON g.admin_account_id=a.id AND g.organization_id=_owner
        WHERE s.token_digest=_digest AND s.revoked_at IS NULL
          AND s.expires_at>clock_timestamp() AND a.is_active
          AND NOT a.invite_pending AND NOT a.otp2fa_enabled
          AND a.password_hash IS NOT NULL;
        IF v_actor IS NULL OR v_role NOT IN ('manager','super_admin')
          OR NOT EXISTS(SELECT 1 FROM club.players p
            WHERE p.id=_player AND p.organization_id=_owner
              AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred)
        THEN RAISE EXCEPTION 'birthday_manager_denied' USING ERRCODE='42501';
        END IF;
        RETURN v_actor;
      END $birthday_authority$;
      REVOKE ALL ON FUNCTION club.require_player_birthday_manager(
        text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.read_admin_player_birthday_preferences(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $birthday_read$
      DECLARE v_row club.player_birthday_card_preferences%ROWTYPE;
      BEGIN
        PERFORM club.require_player_birthday_manager(_digest,_owner,_player);
        SELECT * INTO v_row FROM club.player_birthday_card_preferences
          WHERE organization_id=_owner AND player_id=_player;
        RETURN jsonb_build_object(
          'organization_id',_owner,'player_id',_player,
          'version',CASE WHEN FOUND THEN v_row.version ELSE 0 END,
          'disabled',CASE WHEN FOUND THEN v_row.disabled ELSE true END,
          'wish_text',CASE WHEN FOUND THEN v_row.wish_text ELSE '' END,
          'background_color',CASE WHEN FOUND THEN v_row.background_color ELSE '#F5A623' END,
          'emoji',CASE WHEN FOUND THEN v_row.emoji ELSE '🎂' END,
          'public_enabled',false,'auto_post_facebook',false,
          'auto_post_instagram',false);
      END $birthday_read$;
      REVOKE ALL ON FUNCTION club.read_admin_player_birthday_preferences(
        text,text,text) FROM PUBLIC;

      CREATE FUNCTION club.write_admin_player_birthday_preferences(
        _digest text,_owner text,_player text,_expected integer,
        _disabled boolean,_wish text,_color text,_emoji text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $birthday_write$
      DECLARE v_actor uuid;v_old integer;v_new integer;v_result jsonb;
      BEGIN
        IF _expected IS NULL OR _expected NOT BETWEEN 0 AND 9999
          OR _disabled IS NULL OR _wish IS NULL
          OR length(_wish)>500 OR _wish ~ '[[:cntrl:]]'
          OR _color IS NULL OR _color !~ '^#[0-9A-Fa-f]{6}$'
          OR _emoji IS NULL OR _emoji NOT IN
            ('🎂','🎉','🎈','🎁','🌟','⚽','🏆','❤️') THEN
          RAISE EXCEPTION 'birthday_invalid_preferences' USING ERRCODE='22023';
        END IF;
        v_actor:=club.require_player_birthday_manager(_digest,_owner,_player);
        SELECT version INTO v_old FROM club.player_birthday_card_preferences
          WHERE organization_id=_owner AND player_id=_player FOR UPDATE;
        IF coalesce(v_old,0)<>_expected THEN
          RAISE EXCEPTION 'birthday_version_conflict' USING ERRCODE='23505';
        END IF;
        IF _expected=0 THEN
          INSERT INTO club.player_birthday_card_preferences(
            organization_id,player_id,version,disabled,wish_text,
            background_color,emoji,updated_by)
          VALUES(_owner,_player,1,_disabled,_wish,_color,_emoji,v_actor);
          v_new:=1;
        ELSE
          UPDATE club.player_birthday_card_preferences SET
            version=version+1,disabled=_disabled,wish_text=_wish,
            background_color=_color,emoji=_emoji,updated_by=v_actor,
            updated_at=clock_timestamp()
          WHERE organization_id=_owner AND player_id=_player
            AND version=_expected RETURNING version INTO v_new;
        END IF;
        v_result:=club.read_admin_player_birthday_preferences(
          _digest,_owner,_player);
        INSERT INTO club.player_birthday_card_preference_events(
          id,organization_id,player_id,version,actor_id,snapshot)
        VALUES(gen_random_uuid(),_owner,_player,v_new,v_actor,v_result);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,action,outcome,
          organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,
          'admin.player.birthday.preferences','succeeded',_owner,
          'private_not_published');
        RETURN v_result;
      END $birthday_write$;
      REVOKE ALL ON FUNCTION club.write_admin_player_birthday_preferences(
        text,text,text,integer,boolean,text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
      migrationBuilder.Sql("""
        DO $birthday_rollback$ BEGIN
          IF EXISTS(SELECT 1 FROM club.player_birthday_card_preference_events)
          THEN RAISE EXCEPTION 'birthday_preferences_history_exists'; END IF;
        END $birthday_rollback$;
        DROP FUNCTION club.write_admin_player_birthday_preferences(
          text,text,text,integer,boolean,text,text,text);
        DROP FUNCTION club.read_admin_player_birthday_preferences(text,text,text);
        DROP FUNCTION club.require_player_birthday_manager(text,text,text);
        DROP TABLE club.player_birthday_card_preference_events;
        DROP TABLE club.player_birthday_card_preferences;
      """);
}
