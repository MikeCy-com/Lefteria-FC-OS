\set ON_ERROR_STOP on
-- Disposable Postgres after all OS migrations. Source checks don't replace
-- positive/negative tests with seeded authorized manager sessions.
BEGIN;
DO $checks$
DECLARE f oid;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n
    ON n.oid=c.relnamespace
    WHERE n.nspname='club' AND c.relname='admin_group_create_requests'
      AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'profiled_group_request_rls_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_constraint
    WHERE conrelid='club.admin_group_create_requests'::regclass
      AND contype='p')
  THEN RAISE EXCEPTION 'profiled_group_request_uniqueness_missing'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'club.create_admin_group_with_profile(text,text,text,text,uuid,jsonb)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=f AND prosecdef)
       OR EXISTS(
         SELECT 1 FROM pg_proc p,
         LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
         WHERE p.oid=f AND a.grantee=0 AND a.privilege_type='EXECUTE'
       )
    THEN RAISE EXCEPTION 'profiled_group_unrestricted'; END IF;
  END LOOP;
END $checks$;
DO $negative$
BEGIN
  BEGIN
    PERFORM club.create_admin_group_with_profile(
      repeat('0',64),'lefteria_fc','grp_00000000000000000000000000000001',
      'First Team','11111111-1111-1111-1111-111111111111'::uuid,
      '{"level":"Α Ομάδα","description":"","banner_url":""}'::jsonb);
    RAISE EXCEPTION 'profiled_group_unauthenticated_write_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  IF EXISTS(SELECT 1 FROM club.admin_group_create_requests)
  THEN RAISE EXCEPTION 'profiled_group_negative_test_created_request'; END IF;
END $negative$;
ROLLBACK;
