using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Update the club-wide gold color without rewriting the historical birthday
/// preference migration. Preserve immutable preference-event history.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261114193000_BrandGoldAccent")]
public sealed class BrandGoldAccent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      ALTER TABLE club.player_birthday_card_preferences
        ALTER COLUMN background_color SET DEFAULT '#F6BB38';

      -- The director requested replacement of the former default on existing
      -- cards. Do not rewrite append-only preference event snapshots.
      UPDATE club.player_birthday_card_preferences
        SET background_color = '#F6BB38'
        WHERE upper(background_color) = '#F5A623';

      CREATE OR REPLACE FUNCTION club.read_admin_player_birthday_preferences(
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
          'background_color',CASE WHEN FOUND THEN v_row.background_color ELSE '#F6BB38' END,
          'emoji',CASE WHEN FOUND THEN v_row.emoji ELSE '🎂' END,
          'public_enabled',false,'auto_post_facebook',false,
          'auto_post_instagram',false);
      END $birthday_read$;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- Existing user data and audit snapshots remain unchanged on rollback.
      ALTER TABLE club.player_birthday_card_preferences
        ALTER COLUMN background_color SET DEFAULT '#F5A623';
      CREATE OR REPLACE FUNCTION club.read_admin_player_birthday_preferences(
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
      """);
}
