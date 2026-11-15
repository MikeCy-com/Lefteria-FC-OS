-- Must be sourced as the isolated web-admin-auth runtime after the migration.
-- Probe only fake session digests: never read or mutate real profiles.
DO $admin_contact_boundary$
DECLARE
  v_digest text := repeat('0',64);
  v_hash text := '$2b$12$'||repeat('a',53);
  denied boolean;
BEGIN
  IF NOT has_function_privilege(current_user,
       'identity.read_admin_own_contact_email(text)'::regprocedure,'EXECUTE')
    OR NOT has_function_privilege(current_user,
       'identity.write_admin_own_contact_email(text,text,text)'::regprocedure,'EXECUTE')
    OR has_column_privilege(current_user,'identity.admin_accounts',
       'unverified_contact_email','SELECT')
    OR has_column_privilege(current_user,'identity.admin_accounts',
       'unverified_contact_email','UPDATE')
  THEN RAISE EXCEPTION 'Contact email grants expose raw identity records'; END IF;

  denied:=false;
  BEGIN
    PERFORM identity.read_admin_own_contact_email(v_digest);
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Unverified contact email read accepted non-existent session';
  END IF;

  denied:=false;
  BEGIN
    PERFORM identity.write_admin_own_contact_email(
      v_digest,v_hash,'nobody@example.com');
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Unverified contact email mutation accepted non-existent session';
  END IF;
END $admin_contact_boundary$;
