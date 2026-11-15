-- Included in the outer rolled-back admin-auth permissions probe on a
-- disposable PostgreSQL database. Target, sessions and audit are exercised
-- by the dedicated HTTP integration case, never by live operations here.
DO $managed_status_boundary$
DECLARE
  mobile_name text:=current_setting('lefteria.mobile_runtime_role');
  routine regprocedure:='identity.set_managed_admin_status(text,uuid,text,boolean,boolean)'::regprocedure;
  protected boolean;
  denied boolean;
BEGIN
  SELECT p.prosecdef INTO protected FROM pg_proc p WHERE p.oid=routine;
  IF NOT protected
     OR NOT has_function_privilege(current_user,routine,'EXECUTE')
     OR has_function_privilege(mobile_name,routine,'EXECUTE')
     OR has_function_privilege('public',routine,'EXECUTE')
  THEN RAISE EXCEPTION 'Managed admin status function EXECUTE boundary is unsafe'; END IF;

  denied:=false;
  BEGIN
    UPDATE identity.admin_accounts SET is_active=false WHERE false;
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Admin-auth runtime has raw account mutation permission'; END IF;

  denied:=false;
  BEGIN
    EXECUTE 'SELECT identity.set_managed_admin_status($1,$2,$3,$4,$5)'
      USING repeat('0',64),gen_random_uuid(),'manager',true,false;
  EXCEPTION WHEN insufficient_privilege THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'Forged admin session changed target authority'; END IF;
END $managed_status_boundary$;
