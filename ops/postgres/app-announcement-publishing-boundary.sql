-- App announcements remain an admin-only publish/remove surface. The
-- mobile role can read only scoped published projections, never drafts.
DO $notice_permissions$
DECLARE
  auth_role text:=current_setting('lefteria.admin_auth_role');
  mobile_role text:=current_setting('lefteria.mobile_runtime_role');
  signature text;
  procedure pg_proc%ROWTYPE;
BEGIN
  FOREACH signature IN ARRAY ARRAY[
    'club.publish_admin_app_announcement(text,text,text,bigint)',
    'club.read_admin_app_announcements(text,text)',
    'club.remove_admin_app_announcement(text,text,text,bigint)']
  LOOP
    SELECT * INTO STRICT procedure FROM pg_proc
      WHERE oid=signature::regprocedure;
    IF NOT procedure.prosecdef OR procedure.prorettype<>'jsonb'::regtype
      OR procedure.provolatile<>'v' OR procedure.prokind<>'f'
      OR procedure.proowner<>(SELECT relowner FROM pg_class
          WHERE oid='club.app_announcements'::regclass)
      OR NOT EXISTS(SELECT 1 FROM unnest(coalesce(procedure.proconfig,ARRAY[]::text[])) AS config(value)
          WHERE regexp_replace(config.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
      OR EXISTS(SELECT 1 FROM aclexplode(coalesce(
          procedure.proacl,acldefault('f',procedure.proowner)))
          WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_role,procedure.oid,'EXECUTE')
      OR has_function_privilege(mobile_role,procedure.oid,'EXECUTE') THEN
      RAISE EXCEPTION 'App announcement publish boundary grants are unsafe';
    END IF;
  END LOOP;
  IF has_function_privilege(auth_role,
       'club.app_announcement_admin_actor(text,text)','EXECUTE')
    OR has_function_privilege(mobile_role,
       'club.app_announcement_admin_actor(text,text)','EXECUTE')
    OR has_function_privilege(auth_role,
       'club.read_mobile_app_announcements(uuid,text,text,timestamptz,text,text,integer)','EXECUTE')
    OR NOT has_function_privilege(mobile_role,
       'club.read_mobile_app_announcements(uuid,text,text,timestamptz,text,text,integer)','EXECUTE')
    OR has_any_column_privilege(auth_role,'club.app_announcements','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(auth_role,'club.app_announcements','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(mobile_role,'club.app_announcements','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(mobile_role,'club.app_announcements','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'App announcement raw-table/helper privilege leaked';
  END IF;
END $notice_permissions$;
