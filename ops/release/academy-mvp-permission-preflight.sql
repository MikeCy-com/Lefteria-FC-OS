\set ON_ERROR_STOP on
-- Academy MVP STAGING ONLY: read-only privilege smoke. No account lookup,
-- children data, GRANT, REVOKE, migration, payment or provider call.
-- Run as an authorized database inspector AFTER reviewed grants in isolated
-- staging: psql -X -v ON_ERROR_STOP=1 -v admin_auth_role=... -v app_role=... -f ...
\if :{?admin_auth_role}
\else
  DO $missing$ BEGIN RAISE EXCEPTION 'mvp_admin_runtime_role_required'; END $missing$;
\endif
\if :{?app_role}
\else
  DO $missing$ BEGIN RAISE EXCEPTION 'mvp_mobile_runtime_role_required'; END $missing$;
\endif

BEGIN TRANSACTION READ ONLY;
SELECT set_config('lefteria.mvp.inspect_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.mvp.inspect_mobile_role',:'app_role',true);
DO $mvp_permissions$
DECLARE
  v_admin text:=current_setting('lefteria.mvp.inspect_admin_role');
  v_mobile text:=current_setting('lefteria.mvp.inspect_mobile_role');
  v_sig text;
  v_oid oid;
  v_admin_operations text[]:=ARRAY[
    'club.read_admin_football_directory(text,text)',
    'club.create_admin_academy_location_group_with_profile(text,text,text,uuid,jsonb,uuid)',
    'club.read_admin_academy_training_sessions(text,text,text,date,date)',
    'club.read_admin_academy_training_attendance(text,text,text)',
    'club.record_admin_academy_training_attendance(text,text,text,text,text,integer)',
    'club.read_admin_academy_training_plans(text,text,text,date,date)',
    'club.write_admin_academy_training_plan(text,text,uuid,text,date,time,integer,text,text,text,integer,boolean)',
    'marketing.read_admin_academy_leads(text,text,integer,integer,text)',
    'marketing.update_admin_academy_lead_status(text,text,uuid,text,text)'
  ];
  v_mobile_operations text[]:=ARRAY[
    'identity.issue_guardian_permission(uuid,text,text,timestamptz,uuid,text,text,uuid)',
    'identity.read_issued_guardian_permissions(uuid,text,text,timestamptz)',
    'identity.revoke_issued_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)'
  ];
BEGIN
  IF v_admin=v_mobile OR v_admin=current_user OR v_mobile=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_admin
      AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls) OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_mobile
      AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls)
  THEN RAISE EXCEPTION 'mvp_runtime_role_boundary_invalid'; END IF;

  FOREACH v_sig IN ARRAY v_admin_operations LOOP
    v_oid:=to_regprocedure(v_sig);
    IF v_oid IS NULL OR has_function_privilege(v_admin,v_oid,'EXECUTE') IS NOT TRUE
    THEN RAISE EXCEPTION 'mvp_admin_operation_grant_missing: %',v_sig; END IF;
    IF has_function_privilege(v_mobile,v_oid,'EXECUTE') IS TRUE
    THEN RAISE EXCEPTION 'mvp_admin_operation_leaked_to_mobile: %',v_sig; END IF;
  END LOOP;

  FOREACH v_sig IN ARRAY v_mobile_operations LOOP
    v_oid:=to_regprocedure(v_sig);
    IF v_oid IS NULL OR has_function_privilege(v_mobile,v_oid,'EXECUTE') IS NOT TRUE
    THEN RAISE EXCEPTION 'mvp_family_permission_grant_missing: %',v_sig; END IF;
  END LOOP;

  IF has_table_privilege(v_admin,'club.admin_group_create_requests',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'mvp_group_request_raw_table_exposure'; END IF;
  IF has_table_privilege(v_mobile,'club.admin_group_create_requests',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'mvp_mobile_group_request_table_exposure'; END IF;
  RAISE NOTICE 'Academy MVP runtime functions: scoped grants present; private table access denied. No data changed.';
END $mvp_permissions$;
ROLLBACK;
