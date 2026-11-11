\set ON_ERROR_STOP on
-- Disposable PostgreSQL ONLY, after applying OS migrations. Never invokes
-- a real admin or updates any child, group, coach or finance record.
BEGIN;
DO $checks$
DECLARE f oid;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n
    ON n.oid=c.relnamespace WHERE n.nspname='club'
      AND c.relname='academy_group_profiles' AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'academy_profile_rls_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_constraint
    WHERE conrelid='club.academy_group_profiles'::regclass AND contype='f')
  THEN RAISE EXCEPTION 'academy_group_fk_missing'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'club.authorize_admin_academy_profile(text)'::regprocedure::oid,
    'club.read_admin_academy_group_profile(text,text)'::regprocedure::oid,
    'club.write_admin_academy_group_profile(text,text,bigint,jsonb)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc
      WHERE oid=f AND prosecdef)
      OR EXISTS(SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
    THEN RAISE EXCEPTION 'academy_profile_function_exposed'; END IF;
  END LOOP;
  IF EXISTS(SELECT 1 FROM club.academy_group_profiles
    WHERE revision<1 OR max_players NOT BETWEEN 1 AND 200)
  THEN RAISE EXCEPTION 'academy_profile_invalid_persisted'; END IF;
END $checks$;
DO $negative$
BEGIN
  BEGIN
    PERFORM club.read_admin_academy_group_profile('invalid','unknown');
    RAISE EXCEPTION 'academy_profile_invalid_digest_was_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_academy_group_profile('invalid','unknown',0,'{}'::jsonb);
    RAISE EXCEPTION 'academy_profile_unauthorized_write_was_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $negative$;
ROLLBACK;