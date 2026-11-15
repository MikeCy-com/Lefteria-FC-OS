-- Executed under the unprivileged admin-auth role in a disposable,
-- rolled-back permission audit. No real account IDs or tokens are used.
DO $managed_role_boundary$
DECLARE
  mobile text:=current_setting('lefteria.mobile_runtime_role');
  routine regprocedure:='identity.change_managed_admin_role(text,uuid,text,boolean,text)'::regprocedure;
  denied boolean:=false;
BEGIN
  IF NOT (SELECT p.prosecdef FROM pg_proc p WHERE p.oid=routine)
    OR NOT has_function_privilege(current_user,routine,'EXECUTE')
    OR has_function_privilege(mobile,routine,'EXECUTE')
    OR EXISTS(SELECT 1 FROM pg_proc p,
       LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
       WHERE p.oid=routine AND a.grantee=0 AND a.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'Delegated role writer EXECUTE boundary unsafe'; END IF;

  BEGIN
    UPDATE identity.admin_accounts SET role='coach' WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Raw account role UPDATE privilege detected'; END IF;

  denied:=false;
  BEGIN
    PERFORM identity.change_managed_admin_role(
      repeat('0',64),gen_random_uuid(),'manager',true,'coach');
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Forged admin session accepted by role writer'; END IF;
END $managed_role_boundary$;
