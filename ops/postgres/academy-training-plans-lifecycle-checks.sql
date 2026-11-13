\set ON_ERROR_STOP on
-- Disposable synthetic manager-only training draft lifecycle. All facts are
-- rolled back. Never execute against production data or provider systems.
BEGIN;
DO $plan_lifecycle$
DECLARE
  v_owner constant text:='lefteria_fc_academy';
  v_actor uuid:=gen_random_uuid();
  v_session uuid:=gen_random_uuid();
  v_id uuid:=gen_random_uuid();
  v_other uuid:=gen_random_uuid();
  v_digest text:=repeat('d',64);
  v_group text:='ci_plan_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_result jsonb;
BEGIN
  INSERT INTO club.roster_import_state(
    id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',
    repeat('a',40),'development',0,1,0,0,clock_timestamp());
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(v_group,v_owner,'Disposable CI training group');
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash)
  VALUES(v_actor,'ci_planner_'||left(replace(v_actor::text,'-',''),14),
    'CI Training Manager','manager',true,'ci_not_a_real_password_hash');
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES(v_actor,v_owner);
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,
    created_at,expires_at,revoked_at)
  VALUES(v_session,v_digest,v_actor,repeat('b',64),
    clock_timestamp(),clock_timestamp()+interval '1 hour',NULL);

  v_result:=club.write_admin_academy_training_plan(
    v_digest,v_owner,v_id,v_group,
    DATE '2026-11-19',TIME '17:00',75,'Original private plan',
    'Disposable pitch','No child names',0,false);
  IF v_result->>'status'<>'draft' OR (v_result->>'version')::integer<>1 THEN
    RAISE EXCEPTION 'Training plan draft create failed';
  END IF;
  -- Two drafts for the same group may not overlap, even with different IDs.
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_digest,v_owner,v_other,v_group,
      DATE '2026-11-19',TIME '17:30',60,'Conflicting group plan',
      'Disposable pitch','',0,false);
    RAISE EXCEPTION 'Overlapping Academy draft was accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  -- A training session cannot quietly wrap into the next calendar day.
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_digest,v_owner,v_other,v_group,
      DATE '2026-11-20',TIME '23:30',90,'Overnight plan',
      'Disposable pitch','',0,false);
    RAISE EXCEPTION 'Overnight training draft was accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  v_result:=club.read_admin_academy_training_plans(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
  IF (v_result->>'total')::integer<>1
    OR v_result->'plans'->0->>'title'<>'Original private plan' THEN
    RAISE EXCEPTION 'Private training plan read failed';
  END IF;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_digest,v_owner,v_id,v_group,
      DATE '2026-11-21',TIME '17:30',90,'Stale plan',
      'Disposable pitch','',2,false);
    RAISE EXCEPTION 'Stale plan version accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  v_result:=club.write_admin_academy_training_plan(
    v_digest,v_owner,v_id,v_group,
    DATE '2026-11-21',TIME '17:30',90,'Updated private plan',
    'Disposable pitch','Coach planning note',1,false);
  IF (v_result->>'version')::integer<>2
    OR v_result->>'title'<>'Updated private plan' THEN
    RAISE EXCEPTION 'Private training plan update failed';
  END IF;
  v_result:=club.write_admin_academy_training_plan(
    v_digest,v_owner,v_id,v_group,
    DATE '2026-11-21',TIME '17:30',90,'Updated private plan',
    'Disposable pitch','Coach planning note',2,true);
  IF (v_result->>'version')::integer<>3
    OR v_result->>'status'<>'archived' THEN
    RAISE EXCEPTION 'Private training plan archive failed';
  END IF;
  -- After archiving, the group/day/time slot is no longer occupied.
  v_result:=club.write_admin_academy_training_plan(
    v_digest,v_owner,v_other,v_group,
    DATE '2026-11-21',TIME '18:00',60,'Available after archive',
    'Disposable pitch','',0,false);
  IF v_result->>'status'<>'draft'
    OR (v_result->>'version')::integer<>1 THEN
    RAISE EXCEPTION 'Archived plan still blocks unrelated new draft';
  END IF;
  IF (SELECT count(*) FROM club.academy_training_plan_events
      WHERE plan_id=v_id)<>3
    OR EXISTS(SELECT 1 FROM club.calendar_items WHERE id=v_id::text)
    OR EXISTS(SELECT 1 FROM club.calendar_responses WHERE event_id=v_id::text) THEN
    RAISE EXCEPTION 'Private training history missing or published into imported calendar';
  END IF;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_digest,v_owner,v_id,v_group,
      DATE '2026-11-21',TIME '17:30',90,'Edited archived plan',
      'Disposable pitch','',3,false);
    RAISE EXCEPTION 'Archived plan was editable';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE id=v_session;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(
      v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
    RAISE EXCEPTION 'Revoked manager could read drafts';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $plan_lifecycle$;
ROLLBACK;
