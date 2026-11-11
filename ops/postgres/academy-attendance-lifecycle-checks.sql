\set ON_ERROR_STOP on
-- Synthetic, isolated, rollback-only database lifecycle probe. No source data
-- or live user is modified; never run against the production DB.
BEGIN;
DO $attendance_lifecycle$
DECLARE
  v_owner constant text:='lefteria_fc_academy';
  v_actor uuid:=gen_random_uuid();
  v_digest text:=repeat('d',64);
  v_roster_digest text:=repeat('a',64);
  v_calendar_digest text:=repeat('c',64);
  v_group text:='ci_group_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_event text:='ci_training_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_player text:='ci_player_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_outsider text:='ci_outsider_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_result jsonb;
  v_row jsonb;
BEGIN
  INSERT INTO club.roster_import_state(
    id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',v_roster_digest,'MikeCy-com/Lefteria-FC',
    repeat('a',40),'development',0,1,2,0,clock_timestamp());
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(v_group,v_owner,'Disposable U10');
  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,team_id,
    primary_academy_group_id,image_url,is_active,is_archived,
    is_transferred,has_explicit_picker_fields)
  VALUES
    (v_player,v_owner,'Test Academy Player',11,'Forward','Academy',NULL,
      v_group,NULL,true,false,false,true),
    (v_outsider,v_owner,'Unassigned Test Player',12,'Forward','Academy',NULL,
      NULL,NULL,true,false,false,true);

  INSERT INTO club.calendar_import_state(
    id,digest,roster_digest,source_commit,environment,time_zone,is_complete,
    exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',v_calendar_digest,v_roster_digest,
    repeat('b',40),'development','Asia/Nicosia',true,
    clock_timestamp(),clock_timestamp(),
    '{"quarantined_rows":0}'::jsonb,decode('7b7d','hex'));
  INSERT INTO migration.calendar_source_documents(
    collection,ordinal,import_digest,disposition,target_kind,target_id,source_json)
  VALUES
    ('training_sessions',0,v_calendar_digest,'item','training',
      v_event,'{"title":"CI training"}'),
    ('attendance',0,v_calendar_digest,'response','training',
      v_event,'{"status":"present"}'),
    ('availability',0,v_calendar_digest,'response','training',
      v_event,'{"status":"going"}');
  INSERT INTO club.calendar_items(
    kind,id,organization_id,source_collection,source_ordinal,
    event_type,status,is_cancelled,is_archived,cancel_reason,
    content_status,staff_day,staff_end_day,
    member_day,member_end_day,staff_projection_json,member_projection_json)
  VALUES('training',v_event,v_owner,'training_sessions',0,
    'training',NULL,false,false,NULL,NULL,
    DATE '2026-11-08',DATE '2026-11-08',
    NULL,NULL,'{"title":"Disposable Academy training"}'::jsonb,NULL);
  INSERT INTO club.calendar_item_scopes(
    kind,event_id,scope_kind,record_id,organization_id,team_id,academy_group_id)
  VALUES('training',v_event,'group',v_group,v_owner,NULL,v_group);
  INSERT INTO club.calendar_responses(
    collection,kind,event_id,player_id,organization_id,status,updated_at,
    actor_identity_id,source_ordinal)
  VALUES
    ('attendance','training',v_event,v_player,v_owner,
      'present',clock_timestamp(),NULL,0),
    ('availability','training',v_event,v_player,v_owner,
      'going',clock_timestamp(),NULL,0);
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash)
  VALUES(v_actor,'ci_attendance_'||left(replace(v_actor::text,'-',''),15),
    'CI Academy manager','manager',true,'ci_not_a_real_password_hash');
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES(v_actor,v_owner);
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,
    created_at,expires_at,revoked_at)
  VALUES(gen_random_uuid(),v_digest,v_actor,repeat('b',64),
    clock_timestamp(),clock_timestamp()+interval '1 hour',NULL);

  v_result:=club.read_admin_academy_training_sessions(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-09');
  IF (v_result->>'total')::integer<>1
    OR v_result->'sessions'->0->>'id'<>v_event THEN
    RAISE EXCEPTION 'Training session calendar did not match the Academy group';
  END IF;
  v_result:=club.read_admin_academy_training_attendance(
    v_digest,v_owner,v_event);
  v_row:=v_result->'players'->0;
  IF (v_result->>'total')::integer<>1
    OR v_row->>'player_id'<>v_player
    OR v_row->>'rsvp'<>'going'
    OR v_row->>'historical_attendance'<>'present'
    OR v_row->>'attendance_source'<>'legacy_import'
    OR (v_row->>'version')::integer<>0 THEN
    RAISE EXCEPTION 'Parent RSVP and actual historical attendance were conflated';
  END IF;
  BEGIN
    PERFORM club.record_admin_academy_training_attendance(
      v_digest,v_owner,v_event,v_outsider,'present',0);
    RAISE EXCEPTION 'Unassigned athlete was incorrectly marked present'
      USING ERRCODE='ZX001';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  v_result:=club.record_admin_academy_training_attendance(
    v_digest,v_owner,v_event,v_player,'absent',0);
  IF (v_result->>'changed')::boolean IS DISTINCT FROM true
     OR (v_result->>'version')::integer<>1 THEN
    RAISE EXCEPTION 'First actual attendance override not recorded';
  END IF;
  v_result:=club.record_admin_academy_training_attendance(
    v_digest,v_owner,v_event,v_player,'absent',0);
  IF (v_result->>'replay')::boolean IS DISTINCT FROM true THEN
    RAISE EXCEPTION 'Repeated physical absence incorrectly created an event';
  END IF;
  BEGIN
    PERFORM club.record_admin_academy_training_attendance(
      v_digest,v_owner,v_event,v_player,'present',0);
    RAISE EXCEPTION 'Stale attendance version silently overwrote another manager'
      USING ERRCODE='ZX002';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  v_result:=club.record_admin_academy_training_attendance(
    v_digest,v_owner,v_event,v_player,'present',1);
  IF (v_result->>'changed')::boolean IS DISTINCT FROM true
     OR (v_result->>'version')::integer<>2 THEN
    RAISE EXCEPTION 'Versioned attendance correction did not commit';
  END IF;
  v_result:=club.read_admin_academy_training_attendance(
    v_digest,v_owner,v_event);
  v_row:=v_result->'players'->0;
  IF v_row->>'historical_attendance'<>'present'
    OR v_row->>'rsvp'<>'going'
    OR v_row->>'attendance'<>'present'
    OR v_row->>'attendance_source'<>'os_manager'
    OR (v_row->>'version')::integer<>2
    OR (SELECT count(*) FROM club.academy_training_attendance_events
        WHERE event_id=v_event AND player_id=v_player)<>2
    OR (SELECT status FROM club.calendar_responses
        WHERE collection='attendance' AND event_id=v_event
          AND player_id=v_player)<>'present' THEN
    RAISE EXCEPTION 'Corrected presence did not preserve source data and audit';
  END IF;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_digest;
  BEGIN
    PERFORM club.read_admin_academy_training_attendance(v_digest,v_owner,v_event);
    RAISE EXCEPTION 'Revoked manager could still view Academy attendance'
      USING ERRCODE='ZX003';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $attendance_lifecycle$;
ROLLBACK;
