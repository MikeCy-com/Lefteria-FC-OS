-- Included only by the restricted admin grants/probe transaction. This is the
-- single reviewed exception to the default denial of SECURITY DEFINER calls.
DO $club_lock_boundary$
DECLARE f pg_proc%ROWTYPE;
BEGIN
  SELECT * INTO STRICT f FROM pg_proc WHERE oid='identity.lock_admin_club_authority(text,text)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'void'::regtype OR f.pronargs<>2 OR f.proargtypes<>'25 25'::oidvector
      OR f.provolatile<>'v' OR f.prokind<>'f' OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='identity.admin_accounts'::regclass)
      OR f.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog, pg_temp']::text[]
      OR encode(sha256(convert_to(replace(f.prosrc,E'\r\n',E'\n'),'UTF8')),'hex')<>'6fe115a52062bf6db10568666bc76900c7f11326d378dcfd14f4395c8984f9b7'
      OR EXISTS(SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner))) WHERE grantee=0 AND privilege_type='EXECUTE')
      OR has_function_privilege(current_setting('lefteria.mobile_runtime_role'),f.oid,'EXECUTE')
      OR NOT has_function_privilege(current_setting('lefteria.admin_auth_role'),f.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Admin club authority lock routine differs from its exact reviewed boundary';
  END IF;
END $club_lock_boundary$;
