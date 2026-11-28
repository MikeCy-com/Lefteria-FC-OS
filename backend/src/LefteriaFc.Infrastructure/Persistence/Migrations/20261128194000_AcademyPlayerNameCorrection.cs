using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Correct a current canonical roster name without changing the
/// original application, signed document, identity links or player ID.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128194000_AcademyPlayerNameCorrection")]
public sealed class AcademyPlayerNameCorrection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.rename_admin_academy_player(
        _digest text,_player text,_expected text,_name text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $rename$
      DECLARE v_actor uuid;v_session uuid;v_previous text;v_locked_name text;
      BEGIN
        IF _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
          OR _expected IS NULL OR length(_expected) NOT BETWEEN 1 AND 200
          OR _name IS NULL OR length(_name) NOT BETWEEN 1 AND 200
          OR _name<>btrim(_name) OR _name ~ '[[:cntrl:]]'
        THEN RAISE EXCEPTION 'player_name_invalid' USING ERRCODE='22023'; END IF;
        -- Serialize edits to this exact player before acquiring the profile
        -- shared row lock. Use the same name reservation as enrollment.
        PERFORM pg_advisory_xact_lock(hashtextextended('academy:player:rename:'||_player,0));
        SELECT actor_id,session_id,player_name INTO STRICT v_actor,v_session,v_previous
          FROM club.authorize_academy_private_profile(_digest,_player,true);
        PERFORM pg_advisory_xact_lock(hashtextextended('academy:enrol:'||lower(_name),0));
        SELECT p.name INTO STRICT v_locked_name FROM club.players p
          WHERE p.id=_player AND p.organization_id='lefteria_fc_academy' FOR UPDATE;
        IF v_locked_name IS DISTINCT FROM _expected THEN
          RAISE EXCEPTION 'player_name_revision_conflict' USING ERRCODE='23505'; END IF;
        IF v_locked_name IS DISTINCT FROM _name THEN
          IF EXISTS(SELECT 1 FROM club.players p
            WHERE p.organization_id='lefteria_fc_academy' AND p.id<>_player
              AND lower(btrim(p.name))=lower(_name))
          THEN RAISE EXCEPTION 'player_name_needs_identity_review' USING ERRCODE='23505'; END IF;
          UPDATE club.players SET name=_name
            WHERE organization_id='lefteria_fc_academy' AND id=_player;
          INSERT INTO audit.security_events(id,actor_realm,occurred_at,actor_identity_id,device_session_id,
            action,outcome,organization_id,reason_code)
          VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
            'admin.academy.player.name.correct','succeeded','lefteria_fc_academy','canonical_name_only');
        END IF;
        RETURN jsonb_build_object('organization_id','lefteria_fc_academy','player_id',_player,
          'official_name',_name,'updated',true);
      END $rename$;
      REVOKE ALL ON FUNCTION club.rename_admin_academy_player(text,text,text,text) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.rename_admin_academy_player(text,text,text,text);
      """);
}
