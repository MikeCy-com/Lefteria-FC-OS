\set ON_ERROR_STOP on
-- EXPLICIT STAGING REVIEW ONLY. Never run automatically in migration or CI.
-- Grant narrow draft VAT proposal procedures to independently authorized web-admin
-- runtime; do not grant raw table access, receipt issuance, refunds or Payabl.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'vat_admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'vat_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.vat_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.vat_mobile_role',:'app_role',true);
DO $validate$
DECLARE a text:=current_setting('lefteria.vat_admin_role');
  m text:=current_setting('lefteria.vat_mobile_role');
  f oid;
BEGIN
  IF a=m OR a=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=a AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
  THEN RAISE EXCEPTION 'pos_vat_admin_role_unsafe'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_vat_proposals(text,text)'::regprocedure::oid,
    'pos_cashier.propose_admin_variant_vat(text,text,uuid,text,text,text,date,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_vat_reviews(text,text)'::regprocedure::oid,
    'pos_cashier.review_admin_variant_vat(text,text,uuid,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang
      WHERE p.oid=f AND p.prosecdef AND l.lanname='plpgsql' AND
        NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) x
        WHERE x.grantee=0 AND x.privilege_type='EXECUTE'))
    THEN RAISE EXCEPTION 'pos_vat_definer_unsafe'; END IF;
  END LOOP;
  IF has_table_privilege(a,'pos_cashier.vat_classification_proposals','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(m,'pos_cashier.vat_classification_proposals','SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'pos_vat_raw_table_exposed'; END IF;
END $validate$;
GRANT USAGE ON SCHEMA pos_cashier TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_vat_proposals(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.propose_admin_variant_vat(
  text,text,uuid,text,text,text,date,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_vat_reviews(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.review_admin_variant_vat(
  text,text,uuid,boolean,text) TO :"admin_auth_role";
DO $verify$
DECLARE a text:=current_setting('lefteria.vat_admin_role');
  m text:=current_setting('lefteria.vat_mobile_role');
  f oid;
BEGIN
  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_vat_proposals(text,text)'::regprocedure::oid,
    'pos_cashier.propose_admin_variant_vat(text,text,uuid,text,text,text,date,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_vat_reviews(text,text)'::regprocedure::oid,
    'pos_cashier.review_admin_variant_vat(text,text,uuid,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(a,f,'EXECUTE') OR
      has_function_privilege(m,f,'EXECUTE')
    THEN RAISE EXCEPTION 'pos_vat_grant_boundary_invalid'; END IF;
  END LOOP;
END $verify$;
COMMIT;
