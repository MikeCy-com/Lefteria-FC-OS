using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Database-enforced evaluation revision integrity and immutable document access
/// events. No public or runtime grants; a separately reviewed API must later
/// establish the authenticated actor and scoped player relationship.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110110000_AcademyPrivateRevisionGuards")]
public sealed class AcademyPrivateRevisionGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      -- No client can silently edit or erase the historical score snapshots.
      CREATE FUNCTION academy_private.reject_private_audit_change()
      RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
      BEGIN
        RAISE EXCEPTION 'academy_private_history_immutable' USING ERRCODE='42501';
      END $$;

      CREATE TRIGGER evaluation_revisions_append_only
        BEFORE UPDATE OR DELETE ON academy_private.evaluation_revisions
        FOR EACH ROW EXECUTE FUNCTION academy_private.reject_private_audit_change();
      CREATE TRIGGER document_audit_append_only
        BEFORE UPDATE OR DELETE ON academy_private.document_audit_events
        FOR EACH ROW EXECUTE FUNCTION academy_private.reject_private_audit_change();

      -- Source-compatible Academy scoring keys only. Reject fake JSON values
      -- (null, strings, arrays, booleans), unexpected fields and nonintegral
      -- scores. No medical/body-fat First Team fields are admitted.
      CREATE FUNCTION academy_private.validate_academy_score_group(
        _scores jsonb, _category text
      ) RETURNS void LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $$
      DECLARE
        v_key text;
        v_score jsonb;
        v_allowed text[];
        v_num numeric;
      BEGIN
        v_allowed := CASE _category
          WHEN 'technical' THEN ARRAY[
            'passing','shooting','dribbling','first_touch','ball_control',
            'receiving','one_v_one_att','one_v_one_def']
          WHEN 'tactical' THEN ARRAY[
            'positioning','game_understanding','awareness','decision_making',
            'communication','transition_play']
          WHEN 'physical' THEN ARRAY[
            'speed','agility','balance','coordination','endurance']
          WHEN 'character' THEN ARRAY[
            'respect','discipline','teamwork','confidence','responsibility',
            'effort','coachability','leadership','resilience']
          ELSE NULL
        END;
        IF v_allowed IS NULL OR _scores IS NULL
           OR jsonb_typeof(_scores)<>'object' THEN
          RAISE EXCEPTION 'invalid_academy_score_group' USING ERRCODE='22023';
        END IF;
        FOR v_key,v_score IN SELECT key,value FROM jsonb_each(_scores) LOOP
          IF NOT (v_key=ANY(v_allowed)) OR jsonb_typeof(v_score)<>'number' THEN
            RAISE EXCEPTION 'invalid_academy_score_key_or_value'
              USING ERRCODE='22023';
          END IF;
          v_num := (v_score::text)::numeric;
          IF v_num<0 OR v_num>100 OR v_num<>trunc(v_num) THEN
            RAISE EXCEPTION 'invalid_academy_score_range' USING ERRCODE='22023';
          END IF;
        END LOOP;
      END $$;

      CREATE FUNCTION academy_private.before_academy_evaluation_revision()
      RETURNS trigger LANGUAGE plpgsql
      SET search_path=pg_catalog,pg_temp AS $$
      DECLARE
        v_parent academy_private.evaluations%ROWTYPE;
        v_exists boolean;
      BEGIN
        SELECT * INTO v_parent FROM academy_private.evaluations
          WHERE id=NEW.evaluation_id FOR UPDATE;
        IF NOT FOUND OR v_parent.withdrawn_at IS NOT NULL THEN
          RAISE EXCEPTION 'evaluation_not_available' USING ERRCODE='42501';
        END IF;
        -- Only the source author can revise within the 24h window.
        -- Privileged overrides require a separate, explicitly audited
        -- management operation; role claims in a payload are never trusted.
        IF NEW.editor_id<>v_parent.author_id THEN
          RAISE EXCEPTION 'evaluation_author_mismatch' USING ERRCODE='42501';
        END IF;
        IF clock_timestamp()>=v_parent.edit_until THEN
          RAISE EXCEPTION 'evaluation_edit_window_closed' USING ERRCODE='42501';
        END IF;
        SELECT EXISTS(SELECT 1 FROM academy_private.evaluation_revisions r
          WHERE r.evaluation_id=NEW.evaluation_id)
          INTO v_exists;
        IF (NOT v_exists AND NEW.revision<>1)
           OR (v_exists AND NEW.revision<>v_parent.current_revision+1) THEN
          RAISE EXCEPTION 'evaluation_revision_conflict' USING ERRCODE='23505';
        END IF;
        PERFORM academy_private.validate_academy_score_group(NEW.technical,'technical');
        PERFORM academy_private.validate_academy_score_group(NEW.tactical,'tactical');
        PERFORM academy_private.validate_academy_score_group(NEW.physical,'physical');
        PERFORM academy_private.validate_academy_score_group(NEW.character,'character');
        NEW.edited_at := clock_timestamp();
        IF v_exists THEN
          UPDATE academy_private.evaluations
            SET current_revision=NEW.revision WHERE id=NEW.evaluation_id;
        END IF;
        RETURN NEW;
      END $$;
      CREATE TRIGGER evaluation_revision_validate
        BEFORE INSERT ON academy_private.evaluation_revisions
        FOR EACH ROW EXECUTE FUNCTION
          academy_private.before_academy_evaluation_revision();

      REVOKE ALL ON FUNCTION academy_private.reject_private_audit_change() FROM PUBLIC;
      REVOKE ALL ON FUNCTION academy_private.validate_academy_score_group(jsonb,text) FROM PUBLIC;
      REVOKE ALL ON FUNCTION academy_private.before_academy_evaluation_revision() FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP TRIGGER evaluation_revision_validate
        ON academy_private.evaluation_revisions;
      DROP TRIGGER evaluation_revisions_append_only
        ON academy_private.evaluation_revisions;
      DROP TRIGGER document_audit_append_only
        ON academy_private.document_audit_events;
      DROP FUNCTION academy_private.before_academy_evaluation_revision();
      DROP FUNCTION academy_private.validate_academy_score_group(jsonb,text);
      DROP FUNCTION academy_private.reject_private_audit_change();
    """);
}