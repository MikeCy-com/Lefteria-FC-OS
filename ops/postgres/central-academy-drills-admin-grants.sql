\set ON_ERROR_STOP on
\set ECHO none
-- Explicit operator gate: isolated staging only after migration/role checks.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_runtime_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.drill_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.drill_mobile_role',:'app_role',true);
DO $check$
DECLARE a text:=current_setting('lefteria.drill_admin_role');m text:=current_setting('lefteria.drill_mobile_role');p oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    (SELECT count(*) FROM pg_roles WHERE rolname IN(a,m) AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)<>2
  THEN RAISE EXCEPTION 'drill_role_preconditions'; END IF;
  FOREACH p IN ARRAY ARRAY[
    'club.read_central_academy_drills(text,text,text,text,text,integer)'::regprocedure::oid,
    'club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text)'::regprocedure::oid,
    'club.read_admin_training_drills(text,text,uuid,integer)'::regprocedure::oid,
    'club.write_admin_training_drills(text,text,uuid,integer,jsonb)'::regprocedure::oid,
    'club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE') FROM pg_proc WHERE oid=p)
    THEN RAISE EXCEPTION 'drill_definer_boundary'; END IF;
  END LOOP;
END $check$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
REVOKE ALL ON club.academy_coaching_drills,club.academy_coaching_drill_versions FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.require_central_academy_drills(text,boolean),club.central_academy_drill_view(club.academy_coaching_drills),club.require_training_drill_group(uuid,text),club.guard_training_drill_plan(),club.snapshot_training_drill_event(),club.require_training_drill_review_rollout(),club.training_drill_plan_view(club.academy_training_plan_drafts) FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.read_central_academy_drills(text,text,text,text,text,integer),club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text),club.read_admin_training_drills(text,text,uuid,integer),club.write_admin_training_drills(text,text,uuid,integer,jsonb),club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.read_central_academy_drills(text,text,text,text,text,integer),club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text),club.read_admin_training_drills(text,text,uuid,integer),club.write_admin_training_drills(text,text,uuid,integer,jsonb),club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean) TO :"admin_auth_role";
DO $verify$
DECLARE a text:=current_setting('lefteria.drill_admin_role');m text:=current_setting('lefteria.drill_mobile_role');p oid;t oid;
BEGIN
  FOREACH t IN ARRAY ARRAY['club.academy_coaching_drills'::regclass::oid,'club.academy_coaching_drill_versions'::regclass::oid] LOOP
    IF NOT (SELECT relrowsecurity FROM pg_class WHERE oid=t)
      OR has_table_privilege(a,t,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE') OR has_table_privilege(m,t,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
    THEN RAISE EXCEPTION 'drill_table_exposed'; END IF;
  END LOOP;
  FOREACH p IN ARRAY ARRAY[
    'club.read_central_academy_drills(text,text,text,text,text,integer)'::regprocedure::oid,
    'club.write_central_academy_drill(text,uuid,integer,text,text,text,text,integer,text,text)'::regprocedure::oid,
    'club.read_admin_training_drills(text,text,uuid,integer)'::regprocedure::oid,
    'club.write_admin_training_drills(text,text,uuid,integer,jsonb)'::regprocedure::oid,
    'club.approve_admin_training_drill_plan(text,text,uuid,integer,text,boolean)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(a,p,'EXECUTE') OR has_function_privilege(m,p,'EXECUTE') THEN RAISE EXCEPTION 'drill_execute_boundary'; END IF;
  END LOOP;
  FOREACH p IN ARRAY ARRAY['club.require_central_academy_drills(text,boolean)'::regprocedure::oid,
    'club.central_academy_drill_view(club.academy_coaching_drills)'::regprocedure::oid,
    'club.require_training_drill_group(uuid,text)'::regprocedure::oid,
    'club.guard_training_drill_plan()'::regprocedure::oid,
    'club.snapshot_training_drill_event()'::regprocedure::oid,
    'club.require_training_drill_review_rollout()'::regprocedure::oid,
    'club.training_drill_plan_view(club.academy_training_plan_drafts)'::regprocedure::oid] LOOP
    IF has_function_privilege(a,p,'EXECUTE') OR has_function_privilege(m,p,'EXECUTE') THEN RAISE EXCEPTION 'drill_private_helper_exposed'; END IF;
  END LOOP;
END $verify$;
COMMIT;
