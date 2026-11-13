\set ON_ERROR_STOP on
-- Execute ONLY against disposable staging fixtures after migration and
-- academy-training-plans-admin-grants.sql. No insert is committed.
BEGIN;
DO $training_plan_boundary$
DECLARE
  v_unknown text:=repeat('0',64);
  v_id uuid:=gen_random_uuid();
BEGIN
  IF to_regclass('club.academy_training_plan_drafts') IS NULL
     OR to_regclass('club.academy_training_plan_events') IS NULL
     OR to_regprocedure('club.read_admin_academy_training_plans(text,text,text,date,date)') IS NULL
     OR to_regprocedure('club.write_admin_academy_training_plan(text,text,uuid,text,date,time,integer,text,text,text,integer,boolean)') IS NULL
  THEN RAISE EXCEPTION 'Training planning migration missing'; END IF;
  IF NOT (SELECT relrowsecurity FROM pg_class
    WHERE oid='club.academy_training_plan_drafts'::regclass)
    OR NOT (SELECT relrowsecurity FROM pg_class
    WHERE oid='club.academy_training_plan_events'::regclass)
  THEN RAISE EXCEPTION 'Training planning RLS disabled'; END IF;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(
      v_unknown,'lefteria_fc','any',DATE '2026-11-01',DATE '2026-11-30');
    RAISE EXCEPTION 'Cross-org training plan read accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(
      v_unknown,'lefteria_fc_academy','any',
      DATE '2026-01-01',DATE '2026-11-30');
    RAISE EXCEPTION 'Oversized private plan reader accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(
      v_unknown,'lefteria_fc_academy','any',
      DATE '2026-11-01',DATE '2026-11-30');
    RAISE EXCEPTION 'Unknown admin may list private plans';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_unknown,'lefteria_fc_academy',v_id,'any',
      DATE '2026-11-17',TIME '16:30',60,'Training','Pitch','',0,false);
    RAISE EXCEPTION 'Unknown admin may create private plan';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_unknown,'lefteria_fc_academy',v_id,'any',
      DATE '2026-11-17',TIME '16:30',60,'Training','Pitch','',0,true);
    RAISE EXCEPTION 'Unknown admin may create an archived plan';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_unknown,'lefteria_fc_academy',v_id,'any',
      DATE '2026-11-17',TIME '16:30',12,'Training','Pitch','',0,false);
    RAISE EXCEPTION 'Invalid training duration allowed';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
END $training_plan_boundary$;
ROLLBACK;
