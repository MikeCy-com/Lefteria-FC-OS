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
  v_player text:='ci_pub_player_'||left(replace(gen_random_uuid()::text,'-',''),16);
  v_published jsonb;
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
  INSERT INTO club.players(id,organization_id,name,number,position,
    team_type,team_id,primary_academy_group_id,image_url,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES(v_player,v_owner,'Synthetic CI member',17,'Midfielder',
    'Academy',NULL,v_group,NULL,true,false,false,true);
  INSERT INTO club.calendar_import_state(
    id,digest,roster_digest,source_commit,environment,time_zone,
    is_complete,exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',40),
    'development','Asia/Nicosia',true,clock_timestamp(),clock_timestamp(),
    '{"quarantined_rows":0}'::jsonb,decode('7b7d','hex'));
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
  -- Human manager approval is pinned to the exact version and never publishes.
  v_result:=club.approve_admin_academy_training_plan(
    v_digest,v_owner,v_id,1);
  IF v_result->>'approval_status'<>'approved'
     OR (v_result->>'approved_version')::integer<>1
     OR v_result->>'published'<>'false' THEN
    RAISE EXCEPTION 'Training approval did not confirm an unpublished exact revision';
  END IF;
  -- Preview is bounded to a current human-approved version and never
  -- includes planning notes or child identity information.
  v_result:=club.preview_admin_academy_training_distribution(
    v_digest,v_owner,v_id,1);
  IF v_result->>'human_approved'<>'true'
    OR v_result->>'published'<>'false'
    OR (v_result->>'eligible_player_count')::integer<>1
    OR v_result ? 'notes'
    OR v_result ? 'players' OR v_result ? 'player_ids' THEN
    RAISE EXCEPTION 'Human-approved distribution preview exposed private youth data';
  END IF;
  v_result:=club.read_admin_academy_training_plan_approvals(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
  IF jsonb_array_length(v_result->'approvals')<>1
    OR (v_result->'approvals'->0->>'approved_version')::integer<>1 THEN
    RAISE EXCEPTION 'Approval list missed manager review';
  END IF;
  BEGIN
    PERFORM club.approve_admin_academy_training_plan(
      v_digest,v_owner,v_id,1);
    RAISE EXCEPTION 'Repeated approval should not create duplicate review';
  EXCEPTION WHEN unique_violation THEN NULL;
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
  v_result:=club.read_admin_academy_training_plan_approvals(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
  IF jsonb_array_length(v_result->'approvals')<>0 THEN
    RAISE EXCEPTION 'Editing an approved draft preserved stale approval';
  END IF;
  BEGIN
    PERFORM club.preview_admin_academy_training_distribution(
      v_digest,v_owner,v_id,1);
    RAISE EXCEPTION 'Edited plan still previewable using stale approval';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  BEGIN
    PERFORM club.approve_admin_academy_training_plan(
      v_digest,v_owner,v_id,1);
    RAISE EXCEPTION 'Stale draft revision was approved';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  v_result:=club.approve_admin_academy_training_plan(
    v_digest,v_owner,v_id,2);
  IF (v_result->>'approved_version')::integer<>2 THEN
    RAISE EXCEPTION 'Updated draft could not receive fresh human signoff';
  END IF;
  v_result:=club.preview_admin_academy_training_distribution(
    v_digest,v_owner,v_id,2);
  IF (v_result->>'version')::integer<>2
     OR v_result->>'published'<>'false' THEN
    RAISE EXCEPTION 'Approved updated private plan preview missing';
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
  -- Only the exact approved new draft may become a canonical OS training.
  BEGIN
    PERFORM club.publish_admin_academy_training_plan(
      v_digest,v_owner,v_other,1,true);
    RAISE EXCEPTION 'Unapproved native training was published';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  PERFORM club.approve_admin_academy_training_plan(
    v_digest,v_owner,v_other,1);
  BEGIN
    PERFORM club.publish_admin_academy_training_plan(
      v_digest,v_owner,v_other,1,false);
    RAISE EXCEPTION 'Publish without explicit confirmation accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  v_published:=club.publish_admin_academy_training_plan(
    v_digest,v_owner,v_other,1,true);
  IF v_published->>'published'<>'true'
    OR v_published->>'notifications_sent'<>'false'
    OR v_published->>'event_id'<>'os-training-'||v_other::text
    OR NOT EXISTS(SELECT 1 FROM club.calendar_items c
      JOIN club.calendar_item_scopes sc
        ON sc.organization_id=c.organization_id
        AND sc.kind=c.kind AND sc.event_id=c.id
      WHERE c.id=v_published->>'event_id' AND c.kind='training'
        AND c.source_collection='os_training'
        AND c.member_projection_json IS NOT NULL
        AND sc.scope_kind='group' AND sc.record_id=v_group)
    OR NOT EXISTS(SELECT 1 FROM migration.calendar_source_documents src
      WHERE src.collection='os_training'
        AND src.target_id=v_published->>'event_id')
    OR NOT club.academy_training_player_eligible(
      v_owner,v_published->>'event_id',v_player)
    OR NOT club.os_native_training_primary_member(
      v_owner,v_published->>'event_id',v_player)
    OR club.os_native_training_primary_member(
      'lefteria_fc',v_published->>'event_id',v_player)
    OR club.os_native_training_primary_member(
      v_owner,'unknown-event',v_player) THEN
    RAISE EXCEPTION 'Native Academy training publication was not group-scoped';
  END IF;
  v_result:=club.read_admin_academy_training_publications(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
  IF jsonb_array_length(v_result->'publications')<>1
     OR v_result->'publications'->0->>'event_id'<>
        v_published->>'event_id' THEN
    RAISE EXCEPTION 'Native training publication status not retrievable';
  END IF;
  v_result:=club.read_admin_academy_training_sessions(
    v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
  IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_result->'sessions') it
    WHERE it->>'id'=v_published->>'event_id') THEN
    RAISE EXCEPTION 'Canonical published training missing from Academy register';
  END IF;
  v_result:=club.read_admin_academy_training_attendance(
    v_digest,v_owner,v_published->>'event_id');
  IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_result->'players') p
    WHERE p->>'player_id'=v_player) THEN
    RAISE EXCEPTION 'Published group athlete missing from authorized attendance';
  END IF;
  INSERT INTO club.calendar_responses(
    collection,kind,event_id,player_id,organization_id,status,
    updated_at,actor_identity_id,source_ordinal)
  VALUES('availability','training',v_published->>'event_id',
    v_player,v_owner,'going',clock_timestamp(),NULL,NULL);
  IF NOT EXISTS(SELECT 1 FROM club.calendar_responses
    WHERE collection='availability' AND kind='training'
      AND event_id=v_published->>'event_id'
      AND player_id=v_player AND status='going') THEN
    RAISE EXCEPTION 'Canonical published training does not accept RSVP reference';
  END IF;
  BEGIN
    PERFORM club.publish_admin_academy_training_plan(
      v_digest,v_owner,v_other,1,true);
    RAISE EXCEPTION 'Duplicate canonical event publication accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(
      v_digest,v_owner,v_other,v_group,DATE '2026-11-21',
      TIME '18:00',60,'Edited after publication',
      'Disposable pitch','',1,false);
    RAISE EXCEPTION 'Published plan could still be edited';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
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
    PERFORM club.publish_admin_academy_training_plan(
      v_digest,v_owner,v_other,1,true);
    RAISE EXCEPTION 'Revoked manager could publish';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.preview_admin_academy_training_distribution(
      v_digest,v_owner,v_id,2);
    RAISE EXCEPTION 'Revoked manager could preview private training recipients';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.approve_admin_academy_training_plan(
      v_digest,v_owner,v_other,1);
    RAISE EXCEPTION 'Revoked manager could approve a training plan';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(
      v_digest,v_owner,v_group,DATE '2026-11-01',DATE '2026-11-30');
    RAISE EXCEPTION 'Revoked manager could read drafts';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
END $plan_lifecycle$;
ROLLBACK;
