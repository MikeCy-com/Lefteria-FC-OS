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
  IF NOT has_function_privilege(auth_role,
       'club.write_admin_app_announcement_media(text,text,text,bigint,uuid,text,text,text,bytea)','EXECUTE')
    OR NOT has_function_privilege(auth_role,
       'club.remove_admin_app_announcement_media(text,text,text,bigint,uuid)','EXECUTE')
    OR NOT has_function_privilege(mobile_role,
       'club.read_mobile_app_announcement_media(uuid,text,text,timestamptz,text,text,text,uuid)','EXECUTE')
    OR has_function_privilege(mobile_role,
       'club.write_admin_app_announcement_media(text,text,text,bigint,uuid,text,text,text,bytea)','EXECUTE')
    OR has_function_privilege(mobile_role,
       'club.remove_admin_app_announcement_media(text,text,text,bigint,uuid)','EXECUTE')
    OR has_function_privilege(auth_role,
       'club.read_mobile_app_announcement_media(uuid,text,text,timestamptz,text,text,text,uuid)','EXECUTE')
    OR has_any_column_privilege(auth_role,'club.app_announcement_media','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(auth_role,'club.app_announcement_media','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(mobile_role,'club.app_announcement_media','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(mobile_role,'club.app_announcement_media','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'Private announcement media boundary leaked';
  END IF;
  IF NOT has_function_privilege(mobile_role,
       'club.ack_mobile_app_announcement_read(uuid,text,text,timestamptz,text,text,text)','EXECUTE')
    OR has_function_privilege(auth_role,
       'club.ack_mobile_app_announcement_read(uuid,text,text,timestamptz,text,text,text)','EXECUTE')
    OR NOT has_function_privilege(auth_role,
       'club.read_admin_app_announcement_readers(text,text,text)','EXECUTE')
    OR has_function_privilege(mobile_role,
       'club.read_admin_app_announcement_readers(text,text,text)','EXECUTE')
    OR has_any_column_privilege(auth_role,'club.app_announcement_read_receipts','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(auth_role,'club.app_announcement_read_receipts','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(mobile_role,'club.app_announcement_read_receipts','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(mobile_role,'club.app_announcement_read_receipts','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'Raw announcement readership privileges leaked';
  END IF;
  IF NOT has_function_privilege(mobile_role,
       'club.set_mobile_app_announcement_reaction(uuid,text,text,timestamptz,text,text,text,text,boolean)','EXECUTE')
    OR has_function_privilege(auth_role,
       'club.set_mobile_app_announcement_reaction(uuid,text,text,timestamptz,text,text,text,text,boolean)','EXECUTE')
    OR has_any_column_privilege(auth_role,'club.app_announcement_reactions','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(auth_role,'club.app_announcement_reactions','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(mobile_role,'club.app_announcement_reactions','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(mobile_role,'club.app_announcement_reactions','DELETE,TRUNCATE,TRIGGER') THEN
    RAISE EXCEPTION 'Raw announcement reaction participants leaked';
  END IF;
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
