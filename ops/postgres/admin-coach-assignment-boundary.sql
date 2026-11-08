-- An explicit, owner-locked, audited staff assignment function is the only
-- coach membership mutation right for web-admin. Mobile remains denied.
DO $admin_coach_assignment_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text:=current_setting('lefteria.admin_auth_role');
  mobile_name text:=current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='club.write_admin_coach_assignment(text,text,text,jsonb,jsonb)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
    OR f.pronargs<>5 OR f.provolatile<>'v' OR f.prokind<>'f'
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
      WHERE n.nspname='club' AND c.relname IN
        ('staff','staff_teams','staff_academy_groups','academy_group_coaches')
      AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
    )
  THEN
    RAISE EXCEPTION 'Admin coach writer exceeds exact reviewed authority';
  END IF;
END $admin_coach_assignment_boundary$;
