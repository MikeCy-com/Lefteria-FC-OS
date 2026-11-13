using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Serializes active training drafts by Academy group/day and blocks actual
/// time-range overlaps, including concurrent manager creates. No member
/// calendar writes or public/fan permissions are introduced.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113090000_AcademyTrainingPlanDraftCollisions")]
public sealed class AcademyTrainingPlanDraftCollisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.reject_academy_training_plan_overlap()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $plan_collision$
      DECLARE v_start timestamp without time zone;
              v_end timestamp without time zone;
      BEGIN
        -- Archived plans are historical and never occupy a training slot.
        IF NEW.status<>'draft' THEN RETURN NEW; END IF;
        v_start:=NEW.training_day+NEW.start_time;
        v_end:=v_start+make_interval(mins=>NEW.duration_minutes);
        IF v_end::date<>NEW.training_day THEN
          RAISE EXCEPTION 'training_plan_cannot_span_midnight'
            USING ERRCODE='22023';
        END IF;
        -- Identical org/group/day transactions serialize before range checks.
        -- A hash collision only reduces concurrency; never permits overlap.
        PERFORM pg_advisory_xact_lock(hashtextextended(
          NEW.organization_id||':'||NEW.group_id||':'||NEW.training_day::text,0));
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=NEW.organization_id
            AND p.group_id=NEW.group_id
            AND p.training_day=NEW.training_day
            AND p.status='draft' AND p.id<>NEW.id
            AND (p.training_day+p.start_time)<v_end
            AND (p.training_day+p.start_time+
              make_interval(mins=>p.duration_minutes))>v_start)
        THEN
          RAISE EXCEPTION 'training_plan_group_time_overlap'
            USING ERRCODE='23505';
        END IF;
        RETURN NEW;
      END $plan_collision$;
      REVOKE ALL ON FUNCTION club.reject_academy_training_plan_overlap() FROM PUBLIC;
      CREATE TRIGGER academy_training_plan_no_overlap
        BEFORE INSERT OR UPDATE OF
          group_id,training_day,start_time,duration_minutes,status
        ON club.academy_training_plan_drafts
        FOR EACH ROW EXECUTE FUNCTION club.reject_academy_training_plan_overlap();
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER academy_training_plan_no_overlap
        ON club.academy_training_plan_drafts;
      DROP FUNCTION club.reject_academy_training_plan_overlap();
      """);
}
