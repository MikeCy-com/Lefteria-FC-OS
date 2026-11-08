-- Allow one live roster GROUP COUNT projection and no raw private player,
-- guardian, contact or membership data. Use in operator grant/role probes.
DO $audience_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
  target record;
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='club.read_admin_audiences(text,text)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
      OR f.pronargs<>2 OR f.proargtypes<>'25 25'::oidvector
      OR f.provolatile<>'v' OR f.prokind<>'f'
      OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='club.players'::regclass)
      OR NOT EXISTS (
        SELECT 1 FROM unnest(COALESCE(f.proconfig,ARRAY[]::text[])) AS conf(entry)
        WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp'
      )
      OR EXISTS (SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner)))
                 WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_name,f.oid,'EXECUTE')
      OR has_function_privilege(mobile_name,f.oid,'EXECUTE')
      OR NOT has_schema_privilege(auth_name,'club','USAGE')
      OR EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='club' AND c.relname IN
          ('teams','academy_groups','players','player_academy_groups','roster_import_state')
        AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
          OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER')
          OR has_any_column_privilege(mobile_name,c.oid,'INSERT,UPDATE,REFERENCES')
          OR has_table_privilege(mobile_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
      )
  THEN
    RAISE EXCEPTION 'Private audience directory exceeds reviewed roster authority';
  END IF;
END $audience_boundary$;
