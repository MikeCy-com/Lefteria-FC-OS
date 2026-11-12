\set ON_ERROR_STOP on
\set ECHO none

-- Deliberate operator-only step on an isolated test database.
-- Usage: psql ... -v ai_worker_role=lefteria_ai_worker -f ops/postgres/hub-ai-worker-grants.sql
-- This is NOT included in production deployment or mobile/admin grants.
\if :{?ai_worker_role}
\else
\echo 'ai_worker_role is required; refusing worker grants'
\quit 1
\endif

BEGIN;
SELECT set_config('lefteria.ai_worker_role', :'ai_worker_role',true);
DO $guard$
DECLARE v_name text:=current_setting('lefteria.ai_worker_role');
DECLARE v_role pg_roles%ROWTYPE;
BEGIN
  SELECT * INTO v_role FROM pg_roles WHERE rolname=v_name;
  IF NOT FOUND OR NOT v_role.rolcanlogin OR v_role.rolsuper
    OR v_role.rolcreatedb OR v_role.rolcreaterole
    OR v_role.rolreplication OR v_role.rolbypassrls
    OR v_name=current_user
    OR has_database_privilege(v_name,current_database(),'CREATE')
    OR has_database_privilege(v_name,current_database(),'TEMP') THEN
    RAISE EXCEPTION 'AI worker requires a separate unprivileged LOGIN role';
  END IF;
END $guard$;

GRANT USAGE ON SCHEMA hub TO :"ai_worker_role";
REVOKE ALL ON ALL TABLES IN SCHEMA hub FROM :"ai_worker_role";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA hub FROM :"ai_worker_role";
GRANT EXECUTE ON FUNCTION hub.claim_ai_education_brief(uuid)
  TO :"ai_worker_role";
GRANT EXECUTE ON FUNCTION hub.complete_ai_education_brief(
  uuid,uuid,uuid,uuid,text,text,text,text) TO :"ai_worker_role";
GRANT EXECUTE ON FUNCTION hub.fail_ai_education_brief(uuid,uuid,text)
  TO :"ai_worker_role";

DO $verify$
DECLARE v_name text:=current_setting('lefteria.ai_worker_role');
DECLARE v_table record;
BEGIN
  FOR v_table IN SELECT c.oid FROM pg_class c JOIN pg_namespace n
    ON n.oid=c.relnamespace
    WHERE n.nspname='hub' AND c.relkind IN ('r','p','v','m')
  LOOP
    IF has_table_privilege(v_name,v_table.oid,
        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
      RAISE EXCEPTION 'AI worker has direct hub table privilege';
    END IF;
  END LOOP;
  IF EXISTS (
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='hub' AND c.relkind='S'
      AND has_sequence_privilege(v_name,c.oid,'USAGE,SELECT,UPDATE')
  ) THEN RAISE EXCEPTION 'AI worker has direct hub sequence privilege'; END IF;
  IF NOT has_function_privilege(v_name,
      'hub.claim_ai_education_brief(uuid)','EXECUTE')
    OR NOT has_function_privilege(v_name,
      'hub.complete_ai_education_brief(uuid,uuid,uuid,uuid,text,text,text,text)','EXECUTE')
    OR NOT has_function_privilege(v_name,
      'hub.fail_ai_education_brief(uuid,uuid,text)','EXECUTE')
    OR has_function_privilege(v_name,
      'hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)','EXECUTE')
    OR has_function_privilege(v_name,
      'hub.review_admin_text(text,text,uuid,bigint,text)','EXECUTE')
    OR has_function_privilege(v_name,
      'hub.reject_admin_hub_image(text,text,uuid,bigint,uuid)','EXECUTE')
  THEN RAISE EXCEPTION 'AI worker effective function permissions unsafe';
  END IF;
END $verify$;
COMMIT;
