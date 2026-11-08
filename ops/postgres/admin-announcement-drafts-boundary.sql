-- Drafts are private. Exactly two audited, scoped routines available to
-- admin-auth; no raw table grants and no mobile publishing endpoint.
DO $draft_permissions$
DECLARE
  auth_role text:=current_setting('lefteria.admin_auth_role');
  mobile_role text:=current_setting('lefteria.mobile_runtime_role');
  signature text;
  proc pg_proc%ROWTYPE;
BEGIN
  FOREACH signature IN ARRAY ARRAY[
    'club.create_admin_announcement_draft(text,text,text,text,text,text)',
    'club.read_admin_announcement_drafts(text,text)']
  LOOP
    SELECT * INTO STRICT proc FROM pg_proc WHERE oid=signature::regprocedure;
    IF NOT proc.prosecdef OR proc.prorettype<>'jsonb'::regtype
      OR proc.provolatile<>'v' OR proc.prokind<>'f'
      OR proc.proowner<>(SELECT relowner FROM pg_class
                          WHERE oid='club.admin_announcement_drafts'::regclass)
      OR NOT EXISTS(
        SELECT 1 FROM unnest(coalesce(proc.proconfig,ARRAY[]::text[])) AS conf(value)
        WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
      OR EXISTS (
        SELECT 1 FROM aclexplode(coalesce(proc.proacl,acldefault('f',proc.proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_role,proc.oid,'EXECUTE')
      OR has_function_privilege(mobile_role,proc.oid,'EXECUTE') THEN
      RAISE EXCEPTION 'Announcement draft function permissions unsafe';
    END IF;
  END LOOP;
  IF has_any_column_privilege(auth_role,'club.admin_announcement_drafts','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(auth_role,'club.admin_announcement_drafts','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(mobile_role,'club.admin_announcement_drafts','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(mobile_role,'club.admin_announcement_drafts','DELETE,TRUNCATE,TRIGGER')
  THEN
    RAISE EXCEPTION 'Announcement drafts have unsafe raw-table access';
  END IF;
END $draft_permissions$;
