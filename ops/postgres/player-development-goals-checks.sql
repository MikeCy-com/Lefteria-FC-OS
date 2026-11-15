\set ON_ERROR_STOP on
-- Disposable CI PostgreSQL only: checks the migrated private goal boundary
-- without creating users/players or modifying application data.
BEGIN;
DO $goal_boundary$
DECLARE
  fn oid;
  expected text;
  txt text;
  rel text;
BEGIN
  FOREACH rel IN ARRAY ARRAY[
    'club.player_development_goals','club.player_development_goal_events'
  ] LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_catalog.pg_class c
      WHERE c.oid=rel::regclass AND c.relrowsecurity
        AND c.relkind='r'
    ) THEN RAISE EXCEPTION 'development_goal_rls_missing'; END IF;
    IF EXISTS (
      SELECT 1 FROM pg_catalog.pg_class c,
      LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) acl
      WHERE c.oid=rel::regclass AND acl.grantee=0
        AND acl.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')
    ) THEN RAISE EXCEPTION 'development_goal_public_table_access'; END IF;
  END LOOP;
  IF NOT EXISTS (
    SELECT 1 FROM pg_catalog.pg_trigger t
    WHERE t.tgrelid='club.player_development_goal_events'::regclass
      AND t.tgname='tr_goal_events_immutable' AND NOT t.tgisinternal
  ) THEN RAISE EXCEPTION 'development_goal_immutable_history_missing'; END IF;
  FOREACH fn IN ARRAY ARRAY[
    'club.read_admin_player_development_goals(text,text,text)'::regprocedure::oid,
    'club.create_admin_player_development_goal(text,text,text,uuid,text,text,text,date,uuid)'::regprocedure::oid,
    'club.update_admin_player_development_goal(text,text,text,uuid,uuid,integer,integer,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS (
      SELECT 1 FROM pg_catalog.pg_proc p WHERE p.oid=fn AND p.prosecdef
        AND NOT EXISTS(SELECT 1 FROM aclexplode(
          coalesce(p.proacl,acldefault('f',p.proowner)))
          WHERE grantee=0 AND privilege_type='EXECUTE')
    ) THEN RAISE EXCEPTION 'development_goal_private_function_exposed'; END IF;
  END LOOP;
  IF NOT EXISTS (
    SELECT 1 FROM pg_catalog.pg_constraint
    WHERE conrelid='club.player_development_goal_events'::regclass
      AND contype='u' AND pg_get_constraintdef(oid) LIKE '%request_id%'
  ) THEN RAISE EXCEPTION 'development_goal_idempotency_index_missing'; END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_catalog.pg_constraint
    WHERE conrelid='club.player_development_goals'::regclass
      AND contype='f' AND confrelid='club.players'::regclass
  ) THEN RAISE EXCEPTION 'development_goal_player_link_missing'; END IF;
  IF NOT EXISTS (
    SELECT 1 FROM pg_catalog.pg_constraint
    WHERE conrelid='club.player_development_goals'::regclass
      AND contype='f' AND confrelid='academy_private.evaluations'::regclass
  ) THEN RAISE EXCEPTION 'development_goal_assessment_link_missing'; END IF;
  BEGIN
    PERFORM club.read_admin_player_development_goals(
      repeat('0',64),'lefteria_fc_academy','unassigned-player');
    RAISE EXCEPTION 'development_goal_fake_session_accepted';
  EXCEPTION WHEN OTHERS THEN
    IF SQLERRM='development_goal_fake_session_accepted' THEN RAISE; END IF;
  END;
END $goal_boundary$;
ROLLBACK;