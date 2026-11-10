\set ON_ERROR_STOP on
-- Run on disposable PostgreSQL after migrations, with app's actual
-- nonprivileged database role; all probes roll back.
BEGIN;
DO $boundary$
DECLARE fn regprocedure;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='identity' AND c.relname='family_guardian_permissions'
      AND c.relrowsecurity
  ) THEN RAISE EXCEPTION 'guardian_permissions_rls_missing'; END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes WHERE schemaname='identity'
      AND indexname='uq_family_guardian_live_recipient'
  ) THEN RAISE EXCEPTION 'guardian_live_recipient_index_missing'; END IF;
  FOR fn IN SELECT unnest(ARRAY[
    'identity.issue_guardian_permission(uuid,text,text,timestamptz,uuid,text,text,uuid)'::regprocedure,
    'identity.read_issued_guardian_permissions(uuid,text,text,timestamptz)'::regprocedure,
    'identity.revoke_issued_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)'::regprocedure
  ]) LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid=fn AND prosecdef)
      OR EXISTS (
        SELECT 1 FROM pg_proc p,
          LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=fn AND acl.grantee=0 AND acl.privilege_type='EXECUTE'
      ) THEN RAISE EXCEPTION 'guardian_function_public_or_not_definer'; END IF;
  END LOOP;
END $boundary$;
DO $negative$
BEGIN
  BEGIN
    PERFORM identity.issue_guardian_permission(
      '00000000-0000-0000-0000-000000000001'::uuid,'35799123456',
      'parent',clock_timestamp()+interval '1 day',
      '00000000-0000-0000-0000-000000000002'::uuid,
      'unknown','35799123457',
      '00000000-0000-0000-0000-000000000003'::uuid);
    RAISE EXCEPTION 'guardian_unauthorized_issue_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $negative$;
ROLLBACK;
