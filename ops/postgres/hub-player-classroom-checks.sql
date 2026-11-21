\set ON_ERROR_STOP on
\set ECHO none
-- Executable classroom privacy/sequence acceptance: isolated migrated DB only.
-- No real player, payment, AI publication, message or external provider calls.
BEGIN;
DO $player_classroom$
DECLARE
  v_identity uuid:=gen_random_uuid();
  v_other uuid:=gen_random_uuid();
  v_parent uuid:=gen_random_uuid();
  v_admin uuid:=gen_random_uuid();
  v_player text:='ci_classroom_'||left(md5(gen_random_uuid()::text),22);
  v_content uuid:=gen_random_uuid();
  v_revision uuid:=gen_random_uuid();
  v_quiz uuid:=gen_random_uuid();
  v_result jsonb; v_room jsonb; v_denied boolean;
  v_now timestamptz:=clock_timestamp();
BEGIN
  INSERT INTO identity.mobile_identities
    (id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES (v_identity,'35799500231','player','CI Player Classroom',true,'mobile',v_now),
    (v_other,'35799500232','player','CI Unlinked Player',true,'mobile',v_now),
    (v_parent,'35799500233','parent','CI Parent Classroom',true,'mobile',v_now);
  INSERT INTO identity.admin_accounts
    (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES(v_admin,'ci-classroom-'||left(md5(v_admin::text),8),'CI Editorial Reviewer',
    'super_admin',true,'ci-fixture-password-not-a-login',false,false);

  INSERT INTO club.roster_import_state
    (id,digest,source_repository,source_commit,environment,
     teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
         'development',0,0,1,0,v_now);
  INSERT INTO migration.staff_relationship_import_state
    (id,digest,roster_digest,source_commit,environment,staff_count,
     team_assignments_count,group_assignments_count,group_coaches_count,
     contacts_count,applied_at)
  VALUES('initial',repeat('b',64),repeat('a',64),repeat('a',40),
         'development',0,0,0,0,0,v_now);
  INSERT INTO migration.account_import_state
    (id,digest,roster_digest,staff_digest,source_commit,environment,
     exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',64),
         repeat('a',40),'development',v_now,v_now,
         '{}'::jsonb,decode('00','hex'));
  INSERT INTO club.players
    (id,organization_id,name,number,position,team_type,team_id,
     primary_academy_group_id,image_url,is_active,is_archived,
     is_transferred,has_explicit_picker_fields)
  VALUES(v_player,'lefteria_fc_academy','CI Defender Learning',3,
         'Defender','Academy',NULL,NULL,NULL,true,false,false,true);
  INSERT INTO identity.canonical_contact_bindings
    (collection,source_id,kind,normalized_phone,player_id,import_digest)
  VALUES('players',v_player,'player_own','35799500231',v_player,repeat('c',64));
  INSERT INTO identity.mobile_record_links
    (identity_id,record_id,kind,role,normalized_phone,collection,is_primary)
  VALUES(v_identity,v_player,'player_own','player','35799500231','players',true);

  -- Approved course is accessible for this player only, but its full body
  -- must never appear in the aggregate classroom DTO.
  INSERT INTO hub.content
    (id,organization_id,kind,status,title,audience_parent,audience_player,
     provenance,version,approved_by,approved_at,published_at)
  VALUES(v_content,'lefteria_fc_academy','course','published',
    'CI How defenders hold a line',false,true,'human',1,v_admin,
    v_now-interval '10 days',v_now-interval '9 days');
  INSERT INTO hub.revisions
    (id,content_id,revision,body_text,text_review,visual_review)
  VALUES(v_revision,v_content,1,'CI private approved defensive module',
    'passed','passed');
  INSERT INTO hub.player_quizzes
    (id,organization_id,week_number,topic,position,title,status,
     reviewed_by,reviewed_at,published_at,created_by)
  VALUES(v_quiz,'lefteria_fc_academy',1,'defending','defender',
    'CI Week 1 Defense Quiz','published',v_admin,
    v_now-interval '8 days',v_now-interval '7 days',v_admin);
  INSERT INTO hub.player_quiz_questions
    (id,quiz_id,sequence,prompt,choices,correct_index,explanation)
  VALUES
    (gen_random_uuid(),v_quiz,1,'What is the correct starting position?',
      '["A","B","C","D"]'::jsonb,1,'CI private answer 1'),
    (gen_random_uuid(),v_quiz,2,'When should a defender track the runner?',
      '["A","B","C","D"]'::jsonb,2,'CI private answer 2'),
    (gen_random_uuid(),v_quiz,3,'How should a team defend a transition?',
      '["A","B","C","D"]'::jsonb,0,'CI private answer 3');

  v_result:=hub.read_mobile_player_classrooms(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_room FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF v_result->>'organization_id'<>'lefteria_fc_academy'
    OR v_result->>'audience'<>'player'
    OR v_result->>'verified_position'<>'defender'
    OR jsonb_array_length(v_result->'subjects')<>11
    OR (v_result->'lessons'->>'approved')::integer<>1
    OR (v_result->'lessons'->>'completed')::integer<>0
    OR v_result->'lessons'->>'next_title'<>'CI How defenders hold a line'
    OR (v_result->'quizzes'->>'started')::boolean
    OR (v_result->'quizzes'->>'current_week')::integer<>1
    OR (v_room->>'published_quizzes')::integer<>1
    OR (v_room->>'completed_quizzes')::integer<>0
  THEN RAISE EXCEPTION 'player_classroom_first_week_projection_failed'; END IF;
  IF v_result::text LIKE '%35799500231%'
    OR v_result::text LIKE '%CI private approved defensive module%'
    OR v_result::text LIKE '%correct_index%'
    OR v_result::text LIKE '%CI private answer%'
    OR v_result::text LIKE '%CI Player Classroom%'
  THEN RAISE EXCEPTION 'player_classroom_exposed_private_fields'; END IF;

  PERFORM hub.start_mobile_player_quiz_track(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  v_result:=hub.read_mobile_player_classrooms(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  IF NOT (v_result->'quizzes'->>'started')::boolean
    OR v_result->'quizzes'->>'next_quiz_title'<>'CI Week 1 Defense Quiz'
  THEN RAISE EXCEPTION 'player_classroom_week_one_quiz_not_ready'; END IF;

  INSERT INTO hub.player_quiz_attempts(
    identity_id,organization_id,quiz_id,score,total)
  VALUES(v_identity,'lefteria_fc_academy',v_quiz,2,3);
  PERFORM hub.complete_mobile_next_education(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy',v_content,v_revision);
  v_result:=hub.read_mobile_player_classrooms(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_room FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF (v_result->'lessons'->>'completed')::integer<>1
    OR (v_result->'lessons'->>'approved')::integer<>1
    OR v_result->'lessons'->>'next_title' IS NOT NULL
    OR (v_result->'quizzes'->>'completed_weeks')::integer<>1
    OR (v_result->'quizzes'->>'current_week')::integer<>2
    OR (v_room->>'completed_quizzes')::integer<>1
    OR (v_room->>'correct_answers')::integer<>2
    OR (v_room->>'total_answers')::integer<>3
  THEN RAISE EXCEPTION 'player_classroom_individual_progress_not_recorded'; END IF;

  -- Withdrawn content must disappear from the public curriculum view;
  -- the learner's historic private attempt is never deleted by GET.
  UPDATE hub.player_quizzes
    SET status='withdrawn',published_at=NULL WHERE id=v_quiz;
  v_result:=hub.read_mobile_player_classrooms(
    v_identity,'35799500231','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_room FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF (v_room->>'published_quizzes')::integer<>0
    OR (v_room->>'completed_quizzes')::integer<>0
    OR (v_result->'quizzes'->>'completed_weeks')::integer<>1
  THEN RAISE EXCEPTION 'withdrawn_quiz_was_advertised_or_history_deleted'; END IF;

  -- A parent/fan, unlinked player, foreign club, expired session, and a
  -- mismatched phone must never obtain even an aggregate.
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_parent,'35799500233','parent',v_now+interval '1 hour','lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'parent_read_player_classroom'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_other,'35799500232','player',v_now+interval '1 hour','lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'unlinked_player_read_classroom'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_identity,'35799500231','player',v_now+interval '1 hour','lefteria_fc');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'first_team_academy_classroom_leak'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_identity,'35799500231','player',v_now-interval '1 minute','lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'expired_session_read_classroom'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_identity,'35799500232','player',v_now+interval '1 hour','lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'forged_phone_read_classroom'; END IF;

  UPDATE identity.mobile_identities SET is_active=false WHERE id=v_identity;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_mobile_player_classrooms(
      v_identity,'35799500231','player',v_now+interval '1 hour','lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'deactivated_player_read_classroom'; END IF;
END $player_classroom$;
ROLLBACK;
