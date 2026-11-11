\set ON_ERROR_STOP on
-- Safe disposable PostgreSQL contract checks. Never creates or changes
-- player attendance or sends messages. Must run after the new grant script.
BEGIN;
DO $attendance_isolation$
DECLARE v_missing text:=repeat('0',64);
        v_admin text:=current_setting('lefteria.attendance_admin_role',true);
        v_mobile text:=current_setting('lefteria.attendance_mobile_role',true);
BEGIN
  IF to_regclass('club.academy_training_attendance') IS NULL
    OR to_regclass('club.academy_training_attendance_events') IS NULL
    OR to_regprocedure('club.read_admin_academy_training_sessions(text,text,text,date,date)') IS NULL
    OR to_regprocedure('club.record_admin_academy_training_attendance(text,text,text,text,text,integer)') IS NULL
  THEN RAISE EXCEPTION 'Academy attendance migration missing'; END IF;

  BEGIN
    PERFORM club.read_admin_academy_training_sessions(
      v_missing,'lefteria_fc','g1',DATE '2026-11-01',DATE '2026-11-09');
    RAISE EXCEPTION 'First-team data accessible through Academy training';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_academy_training_sessions(
      v_missing,'lefteria_fc_academy','g1',DATE '2026-01-01',DATE '2026-11-01');
    RAISE EXCEPTION 'Oversized Academy training date range accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_academy_training_attendance(
      v_missing,'lefteria_fc_academy','t1');
    RAISE EXCEPTION 'Unverified Academy manager could read youth attendance';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.record_admin_academy_training_attendance(
      v_missing,'lefteria_fc_academy','t1','p1','going',0);
    RAISE EXCEPTION 'Parent RSVP could be recorded as physical presence';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM club.record_admin_academy_training_attendance(
      v_missing,'lefteria_fc_academy','t1','p1','present',0);
    RAISE EXCEPTION 'Unverified manager could record child attendance';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  IF NOT EXISTS (SELECT 1 FROM pg_class c WHERE
      c.oid='club.academy_training_attendance'::regclass AND c.relrowsecurity)
    OR NOT EXISTS (SELECT 1 FROM pg_class c WHERE
      c.oid='club.academy_training_attendance_events'::regclass AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'Attendance history RLS is absent'; END IF;
END $attendance_isolation$;
ROLLBACK;
