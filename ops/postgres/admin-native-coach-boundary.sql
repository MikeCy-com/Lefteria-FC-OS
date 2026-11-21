-- Exact staff-provisioning function is the only write surface. No
-- private staff/contact table rights are granted to admin-auth or mobile.
DO $admin_native_coach_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text:=current_setting('lefteria.admin_auth_role');
  mobile_name text:=current_setting('lefteria.mobile_runtime_role');
  denied boolean:=false;
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='club.create_admin_native_coach(text,text,uuid,text,text,text,uuid)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
    OR f.pronargs<>7 OR f.provolatile<>'v' OR f.prokind<>'f'
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
      WHERE (n.nspname='club' AND c.relname IN
        ('staff','staff_teams','staff_academy_groups','admin_native_coach_requests'))
        OR (n.nspname='identity' AND c.relname='admin_staff_links')
      AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
    )
  THEN RAISE EXCEPTION 'Admin native coach writer exceeds reviewed authority'; END IF;

  BEGIN
    PERFORM club.create_admin_native_coach(
      repeat('0',64),'lefteria_fc',gen_random_uuid(),
      'Fictional coach','Head Coach','fake_team',NULL);
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Forged session was accepted for staff provisioning';
  END IF;
END $admin_native_coach_boundary$;
