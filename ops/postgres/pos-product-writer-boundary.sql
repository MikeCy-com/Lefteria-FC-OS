-- The only POS product writer that admin-auth can call. Full variant
-- replacement and audit execute inside one security-definer transaction.
DO $pos_product_boundary$
DECLARE
  f pg_proc%ROWTYPE;
  auth_name text := current_setting('lefteria.admin_auth_role');
  mobile_name text := current_setting('lefteria.mobile_runtime_role');
BEGIN
  SELECT * INTO STRICT f FROM pg_proc
    WHERE oid='pos.write_admin_product(text,text,text,text,text,text,boolean,bigint,jsonb)'::regprocedure;
  IF NOT f.prosecdef OR f.prorettype<>'jsonb'::regtype
      OR f.pronargs<>9 OR f.prokind<>'f' OR f.provolatile<>'v'
      OR f.prolang<>(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      OR f.proowner<>(SELECT relowner FROM pg_class WHERE oid='pos.products'::regclass)
      OR NOT EXISTS (
        SELECT 1 FROM unnest(COALESCE(f.proconfig,ARRAY[]::text[])) AS conf(entry)
        WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp'
      )
      OR EXISTS (SELECT 1 FROM aclexplode(COALESCE(f.proacl,acldefault('f',f.proowner)))
                 WHERE grantee=0 AND privilege_type='EXECUTE')
      OR NOT has_function_privilege(auth_name,f.oid,'EXECUTE')
      OR has_function_privilege(mobile_name,f.oid,'EXECUTE')
      OR EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='pos' AND c.relname IN ('categories','products','product_variants')
          AND (has_any_column_privilege(auth_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(auth_name,c.oid,'DELETE,TRUNCATE,TRIGGER')
            OR has_any_column_privilege(mobile_name,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
            OR has_table_privilege(mobile_name,c.oid,'DELETE,TRUNCATE,TRIGGER'))
      )
  THEN
    RAISE EXCEPTION 'Private POS product writer exceeds its reviewed authority';
  END IF;
END $pos_product_boundary$;
