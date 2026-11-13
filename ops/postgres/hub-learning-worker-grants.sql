\set ON_ERROR_STOP on
\set ECHO none
-- OPERATOR ONLY, isolated staging. The role must be independently created
-- with CONNECT and USAGE on hub, no raw table/other function permissions.
\if :{?learning_worker_role}
\else
  \echo 'learning_worker_role is required'
  \quit 2
\endif
BEGIN;
REVOKE ALL ON FUNCTION hub.process_due_mobile_lesson_schedules(integer)
  FROM PUBLIC, :"learning_worker_role";
GRANT EXECUTE ON FUNCTION hub.process_due_mobile_lesson_schedules(integer)
  TO :"learning_worker_role";
SELECT set_config('lefteria.learning_worker_role', :'learning_worker_role', false);
DO $worker_least_privilege$
DECLARE role_name text:=current_setting('lefteria.learning_worker_role');
BEGIN
  IF has_any_column_privilege(role_name,'hub.learning_schedules',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_any_column_privilege(role_name,'hub.learning_delivery_inbox',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(role_name,'hub.learning_schedules','DELETE')
    OR has_table_privilege(role_name,'hub.learning_delivery_inbox','DELETE')
    OR NOT has_function_privilege(role_name,
      'hub.process_due_mobile_lesson_schedules(integer)','EXECUTE')
    OR has_function_privilege(role_name,
      'hub.current_mobile_education_lesson(uuid,text,text,timestamptz,text)','EXECUTE')
  THEN
    RAISE EXCEPTION 'Learning worker role is too powerful';
  END IF;
END $worker_least_privilege$;
COMMIT;
