\set ON_ERROR_STOP on

-- Run ONLY on the isolated Lefteria FC OS database, after the finance EF
-- migration and base admin/mobile role grants. Never run on old production.
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role is required'
  \quit 2
\endif
\if :{?app_role}
\else
  \echo 'app_role is required'
  \quit 2
\endif

BEGIN;
DO $validate$
DECLARE
  admin_role pg_roles%ROWTYPE;
  runtime_role pg_roles%ROWTYPE;
BEGIN
  SELECT * INTO admin_role FROM pg_roles WHERE rolname=:'admin_auth_role';
  SELECT * INTO runtime_role FROM pg_roles WHERE rolname=:'app_role';
  IF NOT FOUND OR admin_role.rolname IS NULL
    OR admin_role.rolname=runtime_role.rolname
    OR admin_role.rolsuper OR runtime_role.rolsuper
    OR admin_role.rolinherit OR runtime_role.rolinherit
    OR admin_role.rolbypassrls OR runtime_role.rolbypassrls
    OR current_database() IN ('postgres','template0','template1')
    OR to_regclass('finance.payment_requests') IS NULL
    OR to_regprocedure('finance.apply_payabl_notification(uuid,text,text,text,integer,bigint)') IS NULL
  THEN
    RAISE EXCEPTION 'The finance role boundary is not safe or not migrated';
  END IF;
END $validate$;

REVOKE ALL ON SCHEMA finance FROM PUBLIC, :"admin_auth_role", :"app_role";
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role", :"app_role";
REVOKE ALL ON ALL TABLES IN SCHEMA finance FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA finance FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA finance FROM PUBLIC, :"admin_auth_role", :"app_role";

GRANT EXECUTE ON FUNCTION finance.create_admin_payment_request(
  text,text,uuid,uuid,text,text,bigint,text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION finance.ready_admin_payment_request(
  text,text,uuid,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION finance.read_admin_payment_requests(
  text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION finance.apply_payabl_notification(
  uuid,text,text,text,integer,bigint) TO :"app_role";

-- Neither login role may directly inspect private payer identities, mutate
-- statuses, run a payout, or bypass the transactionally audited functions.
DO $verify$
DECLARE r text;
BEGIN
  FOREACH r IN ARRAY ARRAY[:'admin_auth_role',:'app_role'] LOOP
    IF has_table_privilege(r,'finance.payment_requests','SELECT,INSERT,UPDATE,DELETE')
      OR has_table_privilege(r,'finance.payment_notifications','SELECT,INSERT,UPDATE,DELETE')
      OR has_schema_privilege(r,'finance','CREATE')
      OR has_function_privilege(r,'finance.payment_requests'::regclass::oid,'EXECUTE') THEN
      RAISE EXCEPTION 'Finance privilege boundary exceeded for %',r;
    END IF;
  END LOOP;
  IF has_function_privilege(:'app_role',
       'finance.create_admin_payment_request(text,text,uuid,uuid,text,text,bigint,text,text,text)','EXECUTE')
    OR has_function_privilege(:'admin_auth_role',
       'finance.apply_payabl_notification(uuid,text,text,text,integer,bigint)','EXECUTE')
  THEN
    RAISE EXCEPTION 'Finance cross-role function leakage';
  END IF;
END $verify$;
COMMIT;
