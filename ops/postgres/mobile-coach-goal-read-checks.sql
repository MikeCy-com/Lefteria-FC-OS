\set ON_ERROR_STOP on
-- Disposable database: an invented mobile identity must never read player goals.
BEGIN;
DO $guard$
DECLARE
  denied boolean:=false;
BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=
    'club.read_mobile_coach_player_goals(uuid,text,text,timestamptz,text,text)'::regprocedure
    AND p.prosecdef AND NOT EXISTS(SELECT 1 FROM aclexplode(
      coalesce(p.proacl,acldefault('f',p.proowner))) a
      WHERE a.grantee=0 AND a.privilege_type='EXECUTE')) THEN
    RAISE EXCEPTION 'coach_goal_function_publicly_executable';
  END IF;
  BEGIN
    PERFORM club.read_mobile_coach_player_goals(gen_random_uuid(),
      '0000000000','coach',clock_timestamp()+interval '1 hour',
      'lefteria_fc_academy','invalid-player');
  EXCEPTION WHEN SQLSTATE '42501' THEN denied:=true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'forged_coach_read_player_goals'; END IF;
  denied:=false;
  BEGIN
    PERFORM club.read_mobile_coach_player_goals(gen_random_uuid(),
      '0000000000','parent',clock_timestamp()+interval '1 hour',
      'lefteria_fc_academy','invalid-player');
  EXCEPTION WHEN SQLSTATE '42501' THEN denied:=true;
  END;
  IF NOT denied THEN RAISE EXCEPTION 'noncoach_read_player_goals'; END IF;
END $guard$;
ROLLBACK;
