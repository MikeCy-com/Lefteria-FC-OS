using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Human approval is an immutable manager action tied to the EXACT current
/// private plan version. It is not publication or recipient delivery.
/// Subsequent plan updates naturally invalidate approval by version mismatch.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261113100000_AcademyTrainingHumanApproval")]
public sealed class AcademyTrainingHumanApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE club.academy_training_plan_approvals(
        id uuid PRIMARY KEY,
        plan_id uuid NOT NULL REFERENCES club.academy_training_plan_drafts(id)
          ON DELETE RESTRICT,
        plan_version integer NOT NULL CHECK(plan_version BETWEEN 1 AND 10000),
        approved_by uuid NOT NULL REFERENCES identity.admin_accounts(id)
          ON DELETE RESTRICT,
        approved_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        plan_snapshot jsonb NOT NULL CHECK(jsonb_typeof(plan_snapshot)='object'),
        UNIQUE(plan_id,plan_version)
      );
      REVOKE ALL ON club.academy_training_plan_approvals FROM PUBLIC;
      ALTER TABLE club.academy_training_plan_approvals ENABLE ROW LEVEL SECURITY;

      CREATE FUNCTION club.approve_admin_academy_training_plan(
        _digest text,_owner text,_plan uuid,_expected integer
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_plan_approve$
      DECLARE
        v_actor uuid; v_session uuid;
        v_plan club.academy_training_plan_drafts%ROWTYPE;
        v_snapshot jsonb;
      BEGIN
        IF _plan IS NULL OR _expected IS NULL OR
          _expected NOT BETWEEN 1 AND 10000 THEN
          RAISE EXCEPTION 'invalid_training_plan_approval' USING ERRCODE='22023';
        END IF;
        SELECT actor_id,session_id INTO v_actor,v_session
          FROM club.require_academy_training_planner(_digest,_owner);
        SELECT * INTO v_plan FROM club.academy_training_plan_drafts
          WHERE id=_plan AND organization_id=_owner FOR UPDATE;
        IF NOT FOUND OR v_plan.status<>'draft'
          OR v_plan.version<>_expected THEN
          RAISE EXCEPTION 'training_plan_approval_stale'
            USING ERRCODE='23505';
        END IF;
        IF v_plan.training_day <
          (clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date THEN
          RAISE EXCEPTION 'training_plan_approval_in_past'
            USING ERRCODE='22023';
        END IF;
        SELECT to_jsonb(p) - 'created_by' - 'updated_by' - 'created_at' - 'updated_at'
          INTO v_snapshot FROM club.academy_training_plan_drafts p
          WHERE p.id=_plan;
        INSERT INTO club.academy_training_plan_approvals(
          id,plan_id,plan_version,approved_by,plan_snapshot)
        VALUES(gen_random_uuid(),_plan,_expected,v_actor,v_snapshot);
        INSERT INTO audit.security_events(
          id,actor_realm,occurred_at,actor_identity_id,
          device_session_id,action,outcome,organization_id,reason_code)
        VALUES(gen_random_uuid(),'admin',clock_timestamp(),v_actor,v_session,
          'admin.training.plan.approved','succeeded',_owner,
          'human_approval_not_published');
        RETURN jsonb_build_object('id',_plan,'organization_id',_owner,
          'approved_version',_expected,'approval_status','approved',
          'published',false);
      END $academy_plan_approve$;

      CREATE FUNCTION club.read_admin_academy_training_plan_approvals(
        _digest text,_owner text,_group text,_from date,_to date
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $academy_plan_approval_read$
      DECLARE v_result jsonb;v_total integer;
      BEGIN
        IF _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
          OR _group ~ '[[:cntrl:]]' OR _from IS NULL OR _to IS NULL
          OR _to<_from OR _to>_from+93 THEN
          RAISE EXCEPTION 'training_plan_approval_filters_invalid'
            USING ERRCODE='22023';
        END IF;
        PERFORM club.require_academy_training_planner(_digest,_owner);
        IF NOT EXISTS(SELECT 1 FROM club.academy_groups
          WHERE organization_id=_owner AND id=_group) THEN
          RAISE EXCEPTION 'training_plan_approval_group_denied'
            USING ERRCODE='42501';
        END IF;
        SELECT count(*) INTO v_total FROM club.academy_training_plan_drafts p
          WHERE p.organization_id=_owner AND p.group_id=_group
            AND p.training_day BETWEEN _from AND _to;
        IF v_total>500 THEN
          RAISE EXCEPTION 'training_plan_approval_limit' USING ERRCODE='54000';
        END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',p.id,'approved_version',a.plan_version)
          ORDER BY p.training_day,p.id),'[]'::jsonb)
        INTO v_result FROM club.academy_training_plan_drafts p
        JOIN club.academy_training_plan_approvals a
          ON a.plan_id=p.id AND a.plan_version=p.version
        WHERE p.organization_id=_owner AND p.group_id=_group
          AND p.training_day BETWEEN _from AND _to AND p.status='draft';
        RETURN jsonb_build_object('organization_id',_owner,
          'group_id',_group,'from',_from,'to',_to,
          'approvals',v_result,'published',false);
      END $academy_plan_approval_read$;

      REVOKE ALL ON FUNCTION club.approve_admin_academy_training_plan(
        text,text,uuid,integer) FROM PUBLIC;
      REVOKE ALL ON FUNCTION club.read_admin_academy_training_plan_approvals(
        text,text,text,date,date) FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $approval_protect$
      BEGIN
        IF EXISTS(SELECT 1 FROM club.academy_training_plan_approvals LIMIT 1)
        THEN RAISE EXCEPTION 'training_plan_approval_history_exists'; END IF;
      END $approval_protect$;
      DROP FUNCTION club.read_admin_academy_training_plan_approvals(
        text,text,text,date,date);
      DROP FUNCTION club.approve_admin_academy_training_plan(
        text,text,uuid,integer);
      DROP TABLE club.academy_training_plan_approvals;
      """);
}
