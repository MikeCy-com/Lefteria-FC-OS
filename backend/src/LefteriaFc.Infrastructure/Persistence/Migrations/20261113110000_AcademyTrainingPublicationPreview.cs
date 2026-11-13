using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Read-only, manager-authorized publication scope preview for an EXACT
/// human-approved private training plan version. Never sends, publishes,
/// returns child identifiers or discloses internal planning notes.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113110000_AcademyTrainingPublicationPreview")]
public sealed class AcademyTrainingPublicationPreview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.preview_admin_academy_training_distribution(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $training_distribution_preview$
      DECLARE
        v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_eligible integer;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL
          OR _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'training_distribution_scope_invalid'
            USING ERRCODE='22023';
        END IF;
        PERFORM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR SHARE;
        IF NOT FOUND OR v_plan.version<>_expected OR
          v_plan.status<>'draft' OR NOT EXISTS(
            SELECT 1 FROM club.academy_training_plan_approvals a
            WHERE a.plan_id=v_plan.id AND a.plan_version=v_plan.version
          ) THEN
          RAISE EXCEPTION 'training_distribution_unapproved'
            USING ERRCODE='23505';
        END IF;
        IF v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_distribution_date_in_past'
            USING ERRCODE='22023';
        END IF;
        -- Current ACTIVE roster only, with exact organization and group
        -- scope. Counts are aggregates; no player IDs or contacts leave SQL.
        SELECT count(*) INTO v_eligible FROM club.players p
          WHERE p.organization_id=_owner AND p.is_active
            AND NOT p.is_archived AND NOT p.is_transferred
            AND p.team_type='Academy'
            AND (
              p.primary_academy_group_id=v_plan.group_id
              OR EXISTS(SELECT 1 FROM club.player_academy_groups m
                WHERE m.organization_id=_owner
                  AND m.player_id=p.id
                  AND m.academy_group_id=v_plan.group_id)
            );
        RETURN jsonb_build_object(
          'id',v_plan.id,'organization_id',v_plan.organization_id,
          'group_id',v_plan.group_id,'version',v_plan.version,
          'day',v_plan.training_day,
          'start_time',to_char(v_plan.start_time,'HH24:MI'),
          'duration_minutes',v_plan.duration_minutes,
          'title',v_plan.title,'location',v_plan.location,
          'eligible_player_count',v_eligible,
          'human_approved',true,'published',false
        );
      END $training_distribution_preview$;
      REVOKE ALL ON FUNCTION club.preview_admin_academy_training_distribution(
        text,text,uuid,integer) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
      migrationBuilder.Sql("""
        DROP FUNCTION club.preview_admin_academy_training_distribution(
          text,text,uuid,integer);
      """);
}
