\set ON_ERROR_STOP on
-- Disposable PostgreSQL after EF migrations; no live records created.
BEGIN;
DO $checks$
DECLARE f oid;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='club' AND c.relname='first_team_profiles' AND c.relrowsecurity)
    OR NOT EXISTS(SELECT 1 FROM pg_constraint
      WHERE conrelid='club.first_team_profiles'::regclass AND contype='f')
  THEN RAISE EXCEPTION 'team_profile_rls_or_fk_missing'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'club.authorize_admin_first_team_profile(text)'::regprocedure::oid,
    'club.read_admin_first_team_profile(text,text)'::regprocedure::oid,
    'club.write_admin_first_team_profile(text,text,bigint,jsonb)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=f AND prosecdef)
      OR EXISTS(SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
    THEN RAISE EXCEPTION 'team_profile_public_execution_or_definer_missing'; END IF;
  END LOOP;
END $checks$;
DO $negative$
BEGIN
  BEGIN
    PERFORM club.read_admin_first_team_profile('invalid','unknown');
    RAISE EXCEPTION 'team_profile_invalid_digest_read_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_first_team_profile('invalid','unknown',0,'{}'::jsonb);
    RAISE EXCEPTION 'team_profile_invalid_digest_write_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $negative$;
ROLLBACK;