\set ON_ERROR_STOP on
\set ECHO none

-- ISOLATED STAGING ONLY. Apply after both weekly HUB migrations.
-- Example:
-- psql "$STAGING_MIGRATION_DATABASE_URL" -v ON_ERROR_STOP=1
--   -v scheduler_role=lefteria_hub_scheduler
--   -f ops/postgres/hub-weekly-scheduler-grants.sql
\if :{?scheduler_role}
\else
  \echo 'scheduler_role is required'
  \quit 2
\endif

BEGIN;
SELECT set_config('lefteria.hub_scheduler_role', :'scheduler_role', true);
DO $verify$
DECLARE r pg_roles%ROWTYPE;
BEGIN
  SELECT * INTO r FROM pg_roles
    WHERE rolname=current_setting('lefteria.hub_scheduler_role');
  IF NOT FOUND OR r.rolname<>'lefteria_hub_scheduler'
    OR r.rolsuper OR r.rolcreatedb OR r.rolcreaterole
    OR r.rolreplication OR r.rolbypassrls OR r.rolinherit
    OR EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
    OR current_database() !~* '(_staging|_preview|_test)$'
    OR to_regclass('hub.learning_enrollments') IS NULL
    OR to_regclass('hub.learning_delivery_outbox') IS NULL
    OR to_regprocedure('hub.dispatch_due_weekly_learning(integer)') IS NULL
    OR to_regprocedure('hub.authorize_mobile_learning_enrollment(uuid,text,text,timestamptz,text,text)') IS NULL
    OR to_regprocedure('hub.read_mobile_weekly_learning(uuid,text,text,timestamptz,text,text)') IS NULL
  THEN
    RAISE EXCEPTION 'Refusing unsafe weekly scheduler grant target';
  END IF;
END $verify$;

SELECT current_database() AS hub_scheduler_database
\gset
GRANT CONNECT ON DATABASE :"hub_scheduler_database" TO :"scheduler_role";

REVOKE ALL ON SCHEMA hub FROM PUBLIC, :"scheduler_role";
GRANT USAGE ON SCHEMA hub TO :"scheduler_role";
REVOKE ALL ON ALL TABLES IN SCHEMA hub FROM :"scheduler_role";
REVOKE ALL ON ALL SEQUENCES IN SCHEMA hub FROM :"scheduler_role";
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA hub FROM :"scheduler_role";
GRANT EXECUTE ON FUNCTION hub.dispatch_due_weekly_learning(integer)
  TO :"scheduler_role";

DO $check$
DECLARE r text:=current_setting('lefteria.hub_scheduler_role');
BEGIN
  IF NOT has_function_privilege(r,
     'hub.dispatch_due_weekly_learning(integer)','EXECUTE')
    OR has_function_privilege(r,
     'hub.fan_delivery_entitled(uuid,text,text,timestamptz)','EXECUTE')
    OR has_function_privilege(r,
     'hub.enroll_mobile_weekly_learning(uuid,text,text,timestamptz,text,text)','EXECUTE')
    OR has_function_privilege(r,
     'hub.read_mobile_weekly_learning(uuid,text,text,timestamptz,text,text)','EXECUTE')
    OR has_table_privilege(r,'hub.learning_enrollments','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(r,'hub.learning_delivery_outbox','SELECT,INSERT,UPDATE,DELETE')
  THEN
    RAISE EXCEPTION 'Weekly scheduler privileges are broader than permitted';
  END IF;
END $check$;
COMMIT;
