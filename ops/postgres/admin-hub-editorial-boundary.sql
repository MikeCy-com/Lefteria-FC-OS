-- Verify the only HUB editorial reader is a locked-down SECURITY DEFINER
-- routine with no raw HUB table grants for either runtime role.
DO $hub_editorial_boundary$
DECLARE
  auth_role text := current_setting('lefteria.admin_auth_role');
  mobile_role text := current_setting('lefteria.mobile_runtime_role');
  routine pg_proc%ROWTYPE;
  target record;
BEGIN
  SELECT * INTO STRICT routine FROM pg_proc
    WHERE oid='hub.read_admin_editorial(text,text)'::regprocedure;
  IF NOT routine.prosecdef OR routine.prorettype<>'jsonb'::regtype
    OR routine.provolatile<>'v' OR routine.prokind<>'f'
    OR routine.proowner<>(SELECT relowner FROM pg_class WHERE oid='hub.content'::regclass)
    OR NOT EXISTS(
      SELECT 1 FROM unnest(coalesce(routine.proconfig,ARRAY[]::text[])) AS conf(value)
      WHERE regexp_replace(conf.value,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(coalesce(routine.proacl,acldefault('f',routine.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(auth_role,routine.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,routine.oid,'EXECUTE') THEN
    RAISE EXCEPTION 'Hub editorial function permissions unsafe';
  END IF;
  FOR target IN SELECT table_name
    FROM information_schema.tables
    WHERE table_schema='hub' AND table_type='BASE TABLE'
  LOOP
    IF has_any_column_privilege(auth_role,format('hub.%I',target.table_name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(auth_role,format('hub.%I',target.table_name),'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(mobile_role,format('hub.%I',target.table_name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(mobile_role,format('hub.%I',target.table_name),'DELETE,TRUNCATE,TRIGGER') THEN
      RAISE EXCEPTION 'Direct HUB table access is forbidden';
    END IF;
  END LOOP;
END $hub_editorial_boundary$;