-- Review this function independently of the read-only POS catalog grant.
-- The dedicated admin role may EXECUTE this one audited category writer,
-- but must never SELECT/INSERT/UPDATE raw POS tables or assume owner roles.
DO $pos_writer_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='pos.write_admin_category(text,text,text,text,boolean,bigint)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
      OR f.pronargs<>6 OR f.prokind<>'f' OR f.provolatile<>'v'
      OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='pos.categories'::regclass)
      OR NOT EXISTS (
        SELECT 1 FROM unnest(COALESCE(f.proconfig,ARRAY[]::text[])) AS conf(entry)
        WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp'
      )
      OR EXISTS(SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner)))
                WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_name,f.oid,'EXECUTE')
      OR has_function_privilege(mobile_name,f.oid,'EXECUTE')
      OR NOT has_schema_privilege(auth_name,'pos','USAGE')
      OR EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='pos' AND c.relname IN ('categories','products','product_variants')
          AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER')
            OR has_any_column_privilege(mobile_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(mobile_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
      )
  THEN
    RAISE EXCEPTION 'Private POS category writer exceeds its exact reviewed authority';
  END IF;
END $pos_writer_boundary$;
