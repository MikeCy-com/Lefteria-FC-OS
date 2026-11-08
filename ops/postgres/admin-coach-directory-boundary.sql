-- Only manager-scoped, non-contact active coach identities and group IDs.
-- Neither web-admin nor mobile runtime can read staff's underlying private
-- relationship tables via this permission.
DO $admin_coach_directory_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text:=current_setting('lefteria.admin_auth_role');
  mobile_name text:=current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='club.read_admin_coaches(text,text,integer,integer)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
    OR f.pronargs<>4 OR f.provolatile<>'v' OR f.prokind<>'f'
    OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
    OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='club.staff'::regclass)
    OR NOT EXISTS(
      SELECT 1 FROM unnest(COALESCE(f.proconfig,ARRAY[]::text[])) AS conf(entry)
      WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS(
      SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_name,f.oid,'EXECUTE')
    OR has_function_privilege(mobile_name,f.oid,'EXECUTE')
    OR EXISTS (
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE ((n.nspname='club' AND c.relname IN
          ('staff','staff_teams','staff_academy_groups','academy_group_coaches'))
        OR (n.nspname='migration' AND c.relname='staff_relationship_import_state'))
      AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
    )
  THEN
    RAISE EXCEPTION 'Admin coach directory exceeds exact reviewed authority';
  END IF;
END $admin_coach_directory_boundary$;
