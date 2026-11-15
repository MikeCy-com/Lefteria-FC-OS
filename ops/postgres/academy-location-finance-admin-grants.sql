\set ON_ERROR_STOP on
\set ECHO none
-- OPERATOR-APPROVED. Run only after migration 20261115250000 and the
-- existing private web-admin grants on isolated Lefteria-FC-OS databases.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'academy_location_finance_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'academy_location_finance_app_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.academy_finance_admin_role',:'admin_auth_role',true),
       set_config('lefteria.academy_finance_mobile_role',:'app_role',true);
DO $guard$
DECLARE
  a text:=current_setting('lefteria.academy_finance_admin_role');
  m text:=current_setting('lefteria.academy_finance_mobile_role');
  f oid;
  relation_oid oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin
      AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'academy_location_finance_roles_invalid'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'finance.assign_admin_academy_issue_site(text,text,text,uuid,uuid,text)'::regprocedure::oid,
    'finance.read_admin_academy_location_finance(text,text,date)'::regprocedure::oid,
    'finance.read_admin_academy_unallocated_issues(text,text,date)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=f AND p.prosecdef
      AND p.proowner=(
        SELECT relowner FROM pg_class WHERE oid='finance.training_fee_issues'::regclass)
      AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[]))
        AS c(entry) WHERE regexp_replace(c.entry,'[[:space:]]','','g')
        ='search_path=pg_catalog,pg_temp'))
      OR EXISTS(SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
    THEN RAISE EXCEPTION 'academy_location_finance_unprotected_function'; END IF;
  END LOOP;
  FOREACH relation_oid IN ARRAY ARRAY[
    'finance.academy_training_fee_locations'::regclass::oid,
    'finance.academy_general_charge_locations'::regclass::oid,
    'finance.training_fee_issues'::regclass::oid,
    'finance.general_charge_issues'::regclass::oid,
    'finance.general_charge_events'::regclass::oid
  ] LOOP
    IF has_any_column_privilege(a,relation_oid,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,relation_oid,'DELETE,TRUNCATE,TRIGGER')
      OR (
        relation_oid IN ('finance.academy_training_fee_locations'::regclass::oid,
          'finance.academy_general_charge_locations'::regclass::oid)
        AND (has_any_column_privilege(m,relation_oid,'SELECT,INSERT,UPDATE,REFERENCES')
          OR has_table_privilege(m,relation_oid,'DELETE,TRUNCATE,TRIGGER'))
      )
    THEN RAISE EXCEPTION 'academy_location_finance_direct_table_access'; END IF;
  END LOOP;
END $guard$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
  finance.assign_admin_academy_issue_site(text,text,text,uuid,uuid,text),
  finance.read_admin_academy_location_finance(text,text,date),
  finance.read_admin_academy_unallocated_issues(text,text,date)
TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
  finance.assign_admin_academy_issue_site(text,text,text,uuid,uuid,text),
  finance.read_admin_academy_location_finance(text,text,date),
  finance.read_admin_academy_unallocated_issues(text,text,date)
FROM :"app_role";
COMMIT;
