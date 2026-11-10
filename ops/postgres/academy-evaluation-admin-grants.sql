\set ON_ERROR_STOP on
-- Operator-only grant on a separately verified admin database role.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_app_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.evaluation_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.evaluation_mobile_role',:'app_role',true);
DO $check$
DECLARE
 a text:=current_setting('lefteria.evaluation_admin_role');
 m text:=current_setting('lefteria.evaluation_mobile_role');
 f oid:='academy_private.read_admin_player_evaluations(text,text,text)'::regprocedure::oid;
 doc oid:='academy_private.read_admin_player_document_metadata(text,text,text)'::regprocedure::oid;
 create_fn oid:='academy_private.create_admin_player_evaluation(text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb)'::regprocedure::oid;
 upload_fn oid:='academy_private.quarantine_admin_player_document(text,text,text,uuid,text,text,date,date,text,bigint,text,text,uuid,bytea,bytea,bytea)'::regprocedure::oid;
BEGIN
 IF a=m OR a=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=a
      AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
    OR NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=upload_fn AND prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE'))
    OR NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=create_fn AND prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE'))
    OR NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=doc AND prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE'))
    OR NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=f AND prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE'))
 THEN RAISE EXCEPTION 'evaluation_grant_prerequisites_failed'; END IF;
END $check$;
GRANT USAGE ON SCHEMA academy_private TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION academy_private.read_admin_player_evaluations(text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION academy_private.read_admin_player_document_metadata(text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION academy_private.create_admin_player_evaluation(
  text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION academy_private.quarantine_admin_player_document(
  text,text,text,uuid,text,text,date,date,text,bigint,text,text,
  uuid,bytea,bytea,bytea) TO :"admin_auth_role";
DO $verify$
DECLARE
 a text:=current_setting('lefteria.evaluation_admin_role');
 m text:=current_setting('lefteria.evaluation_mobile_role');
 f oid:='academy_private.read_admin_player_evaluations(text,text,text)'::regprocedure::oid;
 doc oid:='academy_private.read_admin_player_document_metadata(text,text,text)'::regprocedure::oid;
 create_fn oid:='academy_private.create_admin_player_evaluation(text,text,text,uuid,text,text,jsonb,jsonb,jsonb,jsonb,jsonb)'::regprocedure::oid;
 upload_fn oid:='academy_private.quarantine_admin_player_document(text,text,text,uuid,text,text,date,date,text,bigint,text,text,uuid,bytea,bytea,bytea)'::regprocedure::oid;
 t text;
BEGIN
 IF NOT has_function_privilege(a,f,'EXECUTE')
    OR has_function_privilege(m,f,'EXECUTE')
    OR NOT has_function_privilege(a,doc,'EXECUTE')
    OR has_function_privilege(m,doc,'EXECUTE')
    OR NOT has_function_privilege(a,create_fn,'EXECUTE')
    OR has_function_privilege(m,create_fn,'EXECUTE')
    OR NOT has_function_privilege(a,upload_fn,'EXECUTE')
    OR has_function_privilege(m,upload_fn,'EXECUTE')
 THEN RAISE EXCEPTION 'evaluation_grant_boundary_failed'; END IF;
 FOREACH t IN ARRAY ARRAY['evaluations','evaluation_revisions','evaluation_create_requests','encrypted_document_objects',
                           'player_documents','document_audit_events'] LOOP
  IF has_table_privilege(a,format('academy_private.%I',t),'SELECT,INSERT,UPDATE,DELETE')
     OR has_table_privilege(m,format('academy_private.%I',t),'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'evaluation_private_table_exposed'; END IF;
 END LOOP;
END $verify$;
COMMIT;
