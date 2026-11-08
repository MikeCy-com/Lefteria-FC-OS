-- Allowlisted, owner-locked, paginated player-name directory.
-- No raw youth contact, guardian, date of birth, legacy archive or other role access.
DO $admin_player_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  admin_role text:=current_setting('lefteria.admin_auth_role');
  mobile_role text:=current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='club.read_admin_roster_players(text,text,integer,integer)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
    OR f.pronargs<>4 OR f.proargtypes<>'25 25 23 23'::oidvector
    OR f.provolatile<>'v' OR f.prokind<>'f'
    OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
    OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='club.players'::regclass)
    OR NOT EXISTS (
      SELECT 1 FROM unnest(COALESCE(f.proconfig,ARRAY[]::text[])) AS conf(entry)
      WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp')
    OR EXISTS (
      SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
    OR NOT has_function_privilege(admin_role,f.oid,'EXECUTE')
    OR has_function_privilege(mobile_role,f.oid,'EXECUTE')
    OR NOT has_schema_privilege(admin_role,'club','USAGE')
    OR EXISTS (
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='club' AND c.relname IN (
        'teams','academy_groups','players','player_academy_groups','roster_import_state')
      AND (
        has_any_column_privilege(admin_role,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(admin_role,c.oid,'DELETE,TRUNCATE,TRIGGER')
        OR has_any_column_privilege(mobile_role,c.oid,'INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(mobile_role,c.oid,'DELETE,TRUNCATE,TRIGGER')
      ))
  THEN
    RAISE EXCEPTION 'Admin roster player picker exceeds narrow reviewed authority';
  END IF;
END $admin_player_boundary$;
