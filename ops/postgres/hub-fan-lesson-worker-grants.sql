\set ON_ERROR_STOP on
\set ECHO none
-- Explicit operator staging command. Provision an isolated role first.
\if :{?fan_lesson_worker_role}
\else
  \echo 'fan_lesson_worker_role is required'
  \quit 2
\endif
BEGIN;
REVOKE ALL ON FUNCTION hub.process_due_fan_lesson_schedules(integer)
  FROM PUBLIC, :"fan_lesson_worker_role";
GRANT EXECUTE ON FUNCTION hub.process_due_fan_lesson_schedules(integer)
  TO :"fan_lesson_worker_role";
SELECT set_config('lefteria.fan_lesson_worker_role',
  :'fan_lesson_worker_role',false);
DO $fan_worker_boundary$
DECLARE name text:=current_setting('lefteria.fan_lesson_worker_role');
BEGIN
  IF NOT has_function_privilege(name,
      'hub.process_due_fan_lesson_schedules(integer)','EXECUTE')
    OR has_function_privilege(name,
      'hub.current_mobile_fan_lesson(uuid,text,text,timestamptz,text,text)','EXECUTE')
    OR has_any_column_privilege(name,'hub.fan_lesson_schedules',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_any_column_privilege(name,'hub.fan_lesson_delivery_inbox',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_any_column_privilege(name,'hub.fan_subscriptions',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_any_column_privilege(name,'finance.payment_requests',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(name,'hub.fan_lesson_delivery_inbox','DELETE')
  THEN RAISE EXCEPTION 'Fan lesson worker role permissions unsafe'; END IF;
END $fan_worker_boundary$;
COMMIT;
