\set ON_ERROR_STOP on
\set ECHO none
-- Operator-only: grant EXECUTE on tightly scoped training plan functions
-- to the isolated web-admin runtime. Never grant raw youth/roster tables.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_runtime_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.training_plans_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.training_plans_mobile_role',:'app_role',true);
DO $preflight$
DECLARE
  v_admin text:=current_setting('lefteria.training_plans_admin_role');
  v_mobile text:=current_setting('lefteria.training_plans_mobile_role');
  v_proc oid;
BEGIN
  IF v_admin=v_mobile OR v_admin=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_admin
      AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
    OR to_regclass('club.academy_training_plan_drafts') IS NULL
    OR to_regclass('club.academy_training_plan_events') IS NULL
  THEN RAISE EXCEPTION 'training_plans_role_preconditions_failed'; END IF;
  FOREACH v_proc IN ARRAY ARRAY[
    'club.read_admin_academy_training_plans(text,text,text,date,date)'::regprocedure::oid,
    'club.write_admin_academy_training_plan(text,text,uuid,text,date,time,integer,text,text,text,integer,boolean)'::regprocedure::oid,
    'club.read_admin_academy_training_plan_approvals(text,text,text,date,date)'::regprocedure::oid,
    'club.approve_admin_academy_training_plan(text,text,uuid,integer)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND NOT EXISTS(
      SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
      FROM pg_proc WHERE oid=v_proc)
    THEN RAISE EXCEPTION 'training_plans_definer_not_hardened'; END IF;
  END LOOP;
END $preflight$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_training_plans(
  text,text,text,date,date) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.write_admin_academy_training_plan(
  text,text,uuid,text,date,time,integer,text,text,text,integer,boolean)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_training_plan_approvals(
  text,text,text,date,date) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.approve_admin_academy_training_plan(
  text,text,uuid,integer) TO :"admin_auth_role";
DO $postflight$
DECLARE
  v_admin text:=current_setting('lefteria.training_plans_admin_role');
  v_mobile text:=current_setting('lefteria.training_plans_mobile_role');
  v_proc oid;
  v_table oid;
BEGIN
  FOREACH v_proc IN ARRAY ARRAY[
    'club.read_admin_academy_training_plans(text,text,text,date,date)'::regprocedure::oid,
    'club.write_admin_academy_training_plan(text,text,uuid,text,date,time,integer,text,text,text,integer,boolean)'::regprocedure::oid,
    'club.read_admin_academy_training_plan_approvals(text,text,text,date,date)'::regprocedure::oid,
    'club.approve_admin_academy_training_plan(text,text,uuid,integer)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(v_admin,v_proc,'EXECUTE')
      OR has_function_privilege(v_mobile,v_proc,'EXECUTE')
    THEN RAISE EXCEPTION 'training_plans_execute_scope_failure'; END IF;
  END LOOP;
  IF has_function_privilege(v_admin,
    'club.require_academy_training_planner(text,text)','EXECUTE')
    OR has_function_privilege(v_mobile,
    'club.require_academy_training_planner(text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'training_plans_private_helper_exposed'; END IF;
  IF has_function_privilege(v_admin,
       'club.reject_academy_training_plan_overlap()','EXECUTE')
    OR has_function_privilege(v_mobile,
       'club.reject_academy_training_plan_overlap()','EXECUTE')
  THEN RAISE EXCEPTION 'training_plans_collision_trigger_exposed'; END IF;
  FOREACH v_table IN ARRAY ARRAY[
    'club.academy_training_plan_drafts'::regclass::oid,
    'club.academy_training_plan_events'::regclass::oid,
    'club.academy_training_plan_approvals'::regclass::oid
  ] LOOP
    IF NOT (SELECT relrowsecurity FROM pg_class WHERE oid=v_table)
      OR has_table_privilege(v_admin,v_table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(v_mobile,v_table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
    THEN RAISE EXCEPTION 'training_plans_table_exposure'; END IF;
  END LOOP;
END $postflight$;
COMMIT;
