\set ON_ERROR_STOP on
\set ECHO none

-- Read-only pre-activation audit. Execute with an authorized migration/admin
-- role against ONLY an isolated OS database, never lefteriafc.cy production.
-- This script does not create, change, grant, migrate or insert anything.
\if :{?app_role}
\else
  \echo 'Pass -v app_role=<dedicated OS runtime role>.'
  \quit 2
\endif

BEGIN READ ONLY;
SELECT set_config('lefteria.marketing_check_role', :'app_role', true);
DO $verify$
DECLARE
    runtime_name text := current_setting('lefteria.marketing_check_role');
    runtime_oid oid;
    function_oid oid;
    f pg_proc%ROWTYPE;
    table_oid oid;
    rel record;
BEGIN
    IF current_database() IN ('postgres', 'template0', 'template1') THEN
        RAISE EXCEPTION 'Marketing readiness requires a dedicated OS database';
    END IF;
    SELECT oid INTO runtime_oid FROM pg_roles WHERE rolname=runtime_name;
    IF runtime_oid IS NULL THEN
        RAISE EXCEPTION 'Dedicated OS application role does not exist';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles
               WHERE oid=runtime_oid AND
                 (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
        RAISE EXCEPTION 'Privileged runtime role is not allowed';
    END IF;

    function_oid := to_regprocedure(
      'marketing.store_academy_lead(uuid,text,text,text,integer,text,text,text,boolean,boolean,text)');
    IF function_oid IS NULL THEN
        RAISE EXCEPTION 'Private Academy lead function not installed';
    END IF;
    SELECT * INTO f FROM pg_proc WHERE oid=function_oid;
    IF NOT f.prosecdef OR f.provolatile<>'v'
       OR NOT ('search_path=pg_catalog,pg_temp'=ANY(coalesce(f.proconfig,ARRAY[]::text[]))) THEN
        RAISE EXCEPTION 'Private Academy lead function security properties changed';
    END IF;
    IF EXISTS (
        SELECT 1 FROM aclexplode(coalesce(f.proacl,acldefault('f',f.proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE') THEN
        RAISE EXCEPTION 'PUBLIC still has private Academy function EXECUTE';
    END IF;

    -- This pre-activation audit must see the entire feature still closed.
    IF has_schema_privilege(runtime_name,'marketing','USAGE')
       OR has_function_privilege(runtime_name,function_oid,'EXECUTE') THEN
        RAISE EXCEPTION 'Marketing access already active; refuse pre-activation audit';
    END IF;
    FOR rel IN SELECT n.nspname,c.relname,c.oid,c.relrowsecurity
               FROM pg_class c
               JOIN pg_namespace n ON n.oid=c.relnamespace
               WHERE n.nspname='marketing'
                 AND c.relname IN ('public_leads','lead_events','lead_rate_buckets')
    LOOP
        IF NOT rel.relrowsecurity OR
          has_table_privilege(runtime_name,rel.oid,'SELECT') OR
          has_table_privilege(runtime_name,rel.oid,'INSERT') OR
          has_table_privilege(runtime_name,rel.oid,'UPDATE') OR
          has_table_privilege(runtime_name,rel.oid,'DELETE') THEN
            RAISE EXCEPTION 'Unsafe marketing RLS or DML privileges on %',rel.relname;
        END IF;
    END LOOP;
    SELECT count(*) INTO table_oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='marketing'
        AND c.relname IN ('public_leads','lead_events','lead_rate_buckets');
    IF table_oid<>3 THEN
        RAISE EXCEPTION 'Expected private marketing tables are missing';
    END IF;
    RAISE NOTICE 'Academy lead pre-activation security audit passed; no changes made';
END
$verify$;
ROLLBACK;
