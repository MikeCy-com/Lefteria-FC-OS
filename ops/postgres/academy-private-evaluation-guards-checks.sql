-- Read-only safety assertions and score-validation probes.
-- Run ONLY in a disposable migrated OS database as migration owner.
\set ON_ERROR_STOP on
BEGIN;

DO $checks$
DECLARE
  v_table text;
  v_group text;
  v_failed boolean;
BEGIN
  FOREACH v_table IN ARRAY ARRAY[
    'evaluations','evaluation_revisions','player_documents','document_audit_events'
  ] LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='academy_private' AND c.relname=v_table
        AND c.relrowsecurity
    ) THEN
      RAISE EXCEPTION 'missing_private_evaluation_rls: %',v_table;
    END IF;
    IF EXISTS (
      SELECT 1 FROM pg_class c
      JOIN pg_namespace n ON n.oid=c.relnamespace
      CROSS JOIN LATERAL aclexplode(
        coalesce(c.relacl,acldefault('r',c.relowner))) a
      WHERE n.nspname='academy_private' AND c.relname=v_table
        AND a.grantee=0
        AND a.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')
    ) THEN
      RAISE EXCEPTION 'private_evaluations_publicly_granted: %',v_table;
    END IF;
  END LOOP;
  FOREACH v_group IN ARRAY ARRAY[
    'technical','tactical','physical','character'
  ] LOOP
    PERFORM academy_private.validate_academy_score_group('{}'::jsonb,v_group);
  END LOOP;
  PERFORM academy_private.validate_academy_score_group(
    '{"passing":0,"shooting":100}'::jsonb,'technical');
  v_failed:=false;
  BEGIN
    PERFORM academy_private.validate_academy_score_group(
      '{"passing":101}'::jsonb,'technical');
  EXCEPTION WHEN SQLSTATE '22023' THEN v_failed:=true;
  END;
  IF NOT v_failed THEN
    RAISE EXCEPTION 'invalid_score_was_accepted';
  END IF;
  v_failed:=false;
  BEGIN
    PERFORM academy_private.validate_academy_score_group(
      '{"speed":29.5}'::jsonb,'physical');
  EXCEPTION WHEN SQLSTATE '22023' THEN v_failed:=true;
  END;
  IF NOT v_failed THEN
    RAISE EXCEPTION 'fractional_score_was_accepted';
  END IF;
  v_failed:=false;
  BEGIN
    PERFORM academy_private.validate_academy_score_group(
      '{"unknown_metric":12}'::jsonb,'character');
  EXCEPTION WHEN SQLSTATE '22023' THEN v_failed:=true;
  END;
  IF NOT v_failed THEN
    RAISE EXCEPTION 'unknown_score_was_accepted';
  END IF;
  v_failed:=false;
  BEGIN
    PERFORM academy_private.validate_academy_score_group(
      '{"passing":"99"}'::jsonb,'technical');
  EXCEPTION WHEN SQLSTATE '22023' THEN v_failed:=true;
  END;
  IF NOT v_failed THEN
    RAISE EXCEPTION 'string_score_was_accepted';
  END IF;
END $checks$;
ROLLBACK;