\set ON_ERROR_STOP on
\set ECHO none
-- Owner-only, after the PublicSections migration. Included by the application
-- grants transaction; never grants access to raw people/source/provenance rows.
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'public_sections_app_role_required'; END $required$;
\endif
SELECT set_config('lefteria.sections_runtime_role', :'app_role', true) AS sections_runtime_role
\gset
DO $guard$
DECLARE r pg_roles%ROWTYPE;
BEGIN
  SELECT * INTO STRICT r FROM pg_roles WHERE rolname=current_setting('lefteria.sections_runtime_role');
  IF current_database() IN ('postgres','template0','template1') OR r.rolname=current_user
      OR r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls
      OR EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
      OR EXISTS(SELECT 1 FROM pg_database WHERE datdba=r.oid)
      OR EXISTS(SELECT 1 FROM pg_namespace WHERE nspowner=r.oid)
      OR EXISTS(SELECT 1 FROM pg_class WHERE relowner=r.oid AND relpersistence<>'t')
      OR to_regclass('public_sections.snapshots') IS NULL OR to_regclass('public_sections.current') IS NULL
  THEN RAISE EXCEPTION 'public_sections_restricted_role_and_migration_required'; END IF;
END $guard$;
REVOKE ALL PRIVILEGES ON SCHEMA public_sections FROM PUBLIC, :"app_role";
GRANT USAGE ON SCHEMA public_sections TO :"app_role";
REVOKE ALL PRIVILEGES ON TABLE public_sections.snapshots,public_sections.current,public_sections.documents,
  migration.public_sections_source,migration.public_sections_evidence,migration.public_sections_issues FROM PUBLIC, :"app_role";
DO $columns$
DECLARE target record; names text;
BEGIN
  FOR target IN SELECT n.nspname,c.relname,c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE (n.nspname='public_sections' AND c.relname IN ('snapshots','current','documents'))
       OR (n.nspname='migration' AND c.relname IN ('public_sections_source','public_sections_evidence','public_sections_issues'))
  LOOP
    SELECT string_agg(format('%I',attname),',' ORDER BY attnum) INTO names FROM pg_attribute
      WHERE attrelid=target.oid AND attnum>0 AND NOT attisdropped;
    EXECUTE format('REVOKE ALL PRIVILEGES (%s) ON TABLE %I.%I FROM PUBLIC,%I',names,target.nspname,target.relname,current_setting('lefteria.sections_runtime_role'));
  END LOOP;
END $columns$;
GRANT SELECT(version,documents_count,base_public_digest,exported_at) ON public_sections.snapshots TO :"app_role";
GRANT SELECT ON public_sections.current,public_sections.documents TO :"app_role";
