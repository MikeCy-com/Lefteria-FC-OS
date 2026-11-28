using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Serialize private Academy match and training planning on the existing
/// exact-group/day lock. A match kickoff inside an active training interval is
/// rejected in both directions. Match duration and free-text venues are not inferred.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128204000_AcademyTrainingMatchCollision")]
public sealed class AcademyTrainingMatchCollision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.reject_academy_training_match_kickoff()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $training_match$
      BEGIN
        IF NEW.status<>'draft' THEN RETURN NEW; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended(
          NEW.organization_id||':'||NEW.group_id||':'||NEW.training_day::text,0));
        IF EXISTS(SELECT 1 FROM club.football_match_drafts m
          WHERE m.organization_id=NEW.organization_id AND m.academy_group_id=NEW.group_id
            AND m.match_date=NEW.training_day AND m.status='draft'
            AND (m.match_date+m.kickoff_local)>=(NEW.training_day+NEW.start_time)
            AND (m.match_date+m.kickoff_local)<(NEW.training_day+NEW.start_time+
              make_interval(mins=>NEW.duration_minutes)))
        THEN RAISE EXCEPTION 'academy_training_match_kickoff_overlap' USING ERRCODE='23505'; END IF;
        RETURN NEW;
      END $training_match$;
      CREATE FUNCTION club.reject_academy_match_training_kickoff()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $match_training$
      BEGIN
        IF NEW.organization_id<>'lefteria_fc_academy' OR NEW.academy_group_id IS NULL
          OR NEW.status<>'draft' THEN RETURN NEW; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended(
          NEW.organization_id||':'||NEW.academy_group_id||':'||NEW.match_date::text,0));
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=NEW.organization_id AND p.group_id=NEW.academy_group_id
            AND p.training_day=NEW.match_date AND p.status='draft'
            AND (NEW.match_date+NEW.kickoff_local)>=(p.training_day+p.start_time)
            AND (NEW.match_date+NEW.kickoff_local)<(p.training_day+p.start_time+
              make_interval(mins=>p.duration_minutes)))
        THEN RAISE EXCEPTION 'academy_training_match_kickoff_overlap' USING ERRCODE='23505'; END IF;
        RETURN NEW;
      END $match_training$;
      REVOKE ALL ON FUNCTION club.reject_academy_training_match_kickoff() FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.reject_academy_match_training_kickoff() FROM PUBLIC;
      CREATE TRIGGER academy_training_plan_reject_match_kickoff
        BEFORE INSERT OR UPDATE OF group_id,training_day,start_time,duration_minutes,status
        ON club.academy_training_plan_drafts
        FOR EACH ROW EXECUTE FUNCTION club.reject_academy_training_match_kickoff();
      CREATE TRIGGER academy_match_draft_reject_training_kickoff
        BEFORE INSERT OR UPDATE OF organization_id,academy_group_id,match_date,kickoff_local,status
        ON club.football_match_drafts
        FOR EACH ROW EXECUTE FUNCTION club.reject_academy_match_training_kickoff();
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER academy_match_draft_reject_training_kickoff ON club.football_match_drafts;
      DROP TRIGGER academy_training_plan_reject_match_kickoff ON club.academy_training_plan_drafts;
      DROP FUNCTION club.reject_academy_match_training_kickoff();
      DROP FUNCTION club.reject_academy_training_match_kickoff();
      """);
}
