-- Read/write must be available ONLY through authenticated self-scoped SQL
-- functions, with no raw profile-photo table rights for any API runtime.
-- All probes run against a fake digest and cannot modify a real account.
DO $admin_photo_boundary$
DECLARE digest text:=repeat('0',64);denied boolean;
BEGIN
  IF has_table_privilege(current_user,
        'identity.admin_profile_photos','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
    OR NOT has_function_privilege(current_user,
        'identity.read_admin_own_profile_photo(text)'::regprocedure,'EXECUTE')
    OR NOT has_function_privilege(current_user,
        'identity.write_admin_own_profile_photo(text,bytea)'::regprocedure,'EXECUTE')
  THEN RAISE EXCEPTION 'Private admin photo table/function privileges are unsafe'; END IF;
  denied:=false;
  BEGIN PERFORM identity.read_admin_own_profile_photo(digest);
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Anonymous admin photo read was accepted'; END IF;
  denied:=false;
  BEGIN PERFORM identity.write_admin_own_profile_photo(digest,NULL);
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Anonymous admin photo deletion was accepted'; END IF;
  denied:=false;
  BEGIN PERFORM identity.write_admin_own_profile_photo(
    digest,decode('ffd8ffe0ffd9','hex'));
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'Anonymous admin photo upload was accepted'; END IF;
END $admin_photo_boundary$;
