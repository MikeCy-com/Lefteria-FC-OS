-- The admin role can create a group through ONE scoped, audited
-- function. It cannot insert/edit/delete teams, groups, players or
-- memberships directly, and the mobile role gets no EXECUTE.
DO $admin_group_writer$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc WHERE
    oid='club.create_admin_group(text,text,text,text)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
      OR f.pronargs<>4 OR f.prokind<>'f' OR f.provolatile<>'v'
      OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='club.teams'::regclass)
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
          ('teams','academy_groups','players','player_academy_groups')
        AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER')
            OR has_any_column_privilege(mobile_name,c.oid,'INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(mobile_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
      )
  THEN
    RAISE EXCEPTION 'Admin group writer exceeds its exact reviewed boundary';
  END IF;
END $admin_group_writer$;
