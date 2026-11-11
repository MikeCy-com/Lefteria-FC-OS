\set ON_ERROR_STOP on
-- Disposable OS PostgreSQL database only: no files or private data created.
BEGIN;
DO $vault_checks$
DECLARE
  v_public boolean;
  v_denied boolean:=false;
BEGIN
  IF NOT EXISTS(
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='academy_private'
      AND c.relname='encrypted_document_objects' AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'encrypted_vault_rls_missing'; END IF;

  SELECT EXISTS(
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    CROSS JOIN LATERAL aclexplode(coalesce(
      c.relacl,acldefault('r',c.relowner))) a
    WHERE n.nspname='academy_private'
      AND c.relname='encrypted_document_objects'
      AND a.grantee=0
      AND a.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')
  ) INTO v_public;
  IF v_public THEN RAISE EXCEPTION 'encrypted_vault_publicly_granted'; END IF;

  IF NOT EXISTS(
    SELECT 1 FROM pg_proc p WHERE p.oid=
      'academy_private.quarantine_admin_player_document(text,text,text,uuid,text,text,date,date,text,bigint,text,text,uuid,bytea,bytea,bytea)'::regprocedure
      AND p.prosecdef AND NOT EXISTS(
        SELECT 1 FROM aclexplode(coalesce(
          p.proacl,acldefault('f',p.proowner))) a
        WHERE a.grantee=0 AND a.privilege_type='EXECUTE')
  ) THEN RAISE EXCEPTION 'private_upload_function_exposed'; END IF;

  BEGIN
    PERFORM academy_private.quarantine_admin_player_document(
      repeat('0',64),'lefteria_fc','invalid_test_player',
      gen_random_uuid(),'other','Disposable test',NULL,NULL,
      'application/pdf',32,repeat('0',64),'disposable-test-key',
      gen_random_uuid(),decode(repeat('00',32),'hex'),
      decode(repeat('00',12),'hex'),decode(repeat('00',16),'hex'));
  EXCEPTION WHEN SQLSTATE '42501' THEN v_denied:=true;
  END;
  IF NOT v_denied THEN RAISE EXCEPTION 'fake_vault_session_accepted'; END IF;
END $vault_checks$;
ROLLBACK;