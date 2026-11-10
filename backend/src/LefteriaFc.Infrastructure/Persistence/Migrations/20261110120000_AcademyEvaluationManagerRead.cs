using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Manager-only, player-scoped Academy evaluation timeline. Returns bounded
/// snapshots without granting direct access to sensitive base tables.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110120000_AcademyEvaluationManagerRead")]
public sealed class AcademyEvaluationManagerRead : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION academy_private.read_admin_player_evaluations(
        _digest text,_owner text,_player text
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_eval_read$
      DECLARE v_role text; v_count integer; v_rows jsonb;
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
           OR _owner<>'lefteria_fc_academy'
           OR _player IS NULL OR length(_player) NOT BETWEEN 1 AND 128
           OR _player ~ '[[:cntrl:]]' THEN
          RAISE EXCEPTION 'evaluation_scope_invalid' USING ERRCODE='22023';
        END IF;
        BEGIN
          PERFORM identity.lock_admin_club_authority(_digest,_owner);
        EXCEPTION WHEN raise_exception THEN
          RAISE EXCEPTION 'evaluation_authority_revoked' USING ERRCODE='42501';
        END;
        SELECT a.role INTO v_role
          FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g
            ON g.admin_account_id=a.id AND g.organization_id=_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL
            AND s.expires_at>clock_timestamp() AND a.is_active
            AND NOT a.invite_pending AND NOT a.otp2fa_enabled
            AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role NOT IN ('manager','super_admin') THEN
          RAISE EXCEPTION 'evaluation_read_denied' USING ERRCODE='42501';
        END IF;
        IF NOT EXISTS(
          SELECT 1 FROM club.players p WHERE p.organization_id=_owner
            AND p.id=_player AND p.team_type='Academy'
            AND p.is_active AND NOT p.is_archived AND NOT p.is_transferred
        ) THEN
          RAISE EXCEPTION 'evaluation_player_not_available' USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_count FROM academy_private.evaluations e
          WHERE e.organization_id=_owner AND e.player_id=_player
            AND e.withdrawn_at IS NULL;
        IF v_count>200 THEN
          RAISE EXCEPTION 'evaluation_timeline_too_large' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',e.id,'created_at',e.created_at,
          'edit_until',e.edit_until,
          'revision',e.current_revision,
          'summary',r.summary,'notes',r.notes,
          'technical',r.technical,'tactical',r.tactical,
          'physical',r.physical,'character',r.character,
          'overall',r.overall)
          ORDER BY e.created_at DESC,e.id),'[]'::jsonb)
        INTO v_rows FROM academy_private.evaluations e
        JOIN academy_private.evaluation_revisions r
          ON r.evaluation_id=e.id AND r.revision=e.current_revision
        WHERE e.organization_id=_owner AND e.player_id=_player
          AND e.withdrawn_at IS NULL;
        RETURN jsonb_build_object('organization_id',_owner,
          'player_id',_player,'total',v_count,'evaluations',v_rows);
      END $academy_eval_read$;
      REVOKE ALL ON FUNCTION academy_private.read_admin_player_evaluations(
        text,text,text) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION academy_private.read_admin_player_evaluations(text,text,text);
    """);
}
