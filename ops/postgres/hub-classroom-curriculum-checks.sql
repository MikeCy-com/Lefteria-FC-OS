\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic, disposable-only academy curriculum and player progress acceptance.
-- All writes roll back; no real player, external sender, AI or billing provider.
BEGIN;
DO $classroom_curriculum_check$
DECLARE
  v_admin uuid:=gen_random_uuid();v_manager uuid:=gen_random_uuid();
  v_foreign uuid:=gen_random_uuid();v_player_identity uuid:=gen_random_uuid();
  v_player text:='ci_hub_subject_'||left(md5(gen_random_uuid()::text),20);
  v_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_foreign_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_content uuid:=gen_random_uuid();v_parent_only uuid:=gen_random_uuid();
  v_foreign_content uuid:=gen_random_uuid();
  v_revision uuid:=gen_random_uuid();v_revision2 uuid:=gen_random_uuid();
  v_parent_rev uuid:=gen_random_uuid();v_foreign_rev uuid:=gen_random_uuid();
  v_now timestamptz:=clock_timestamp();
  v_list jsonb;v_result jsonb;v_subject jsonb;v_change jsonb;v_denied boolean;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
  VALUES
    (v_admin,'ci-curriculum-super-'||left(md5(v_admin::text),8),
      'CI Academy Super Admin','super_admin',true,'fixture_only'),
    (v_manager,'ci-curriculum-manager-'||left(md5(v_manager::text),8),
      'CI Academy Manager','manager',true,'fixture_only'),
    (v_foreign,'ci-curriculum-foreign-'||left(md5(v_foreign::text),8),
      'CI First Team Super Admin','super_admin',true,'fixture_only');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES (v_admin,'lefteria_fc_academy'),(v_manager,'lefteria_fc_academy'),
    (v_foreign,'lefteria_fc');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES (gen_random_uuid(),v_digest,v_admin,repeat('b',64),
          v_now,v_now+interval '1 hour'),
    (gen_random_uuid(),v_manager_digest,v_manager,repeat('b',64),
          v_now,v_now+interval '1 hour'),
    (gen_random_uuid(),v_foreign_digest,v_foreign,repeat('b',64),
          v_now,v_now+interval '1 hour');
  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES(v_player_identity,'35799500621','player','CI Classroom Curriculum Player',
         true,'mobile',v_now);
  INSERT INTO club.roster_import_state(
    id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
    'development',0,0,1,0,v_now);
  INSERT INTO migration.staff_relationship_import_state(
    id,digest,roster_digest,source_commit,environment,staff_count,
    team_assignments_count,group_assignments_count,group_coaches_count,
    contacts_count,applied_at)
  VALUES('initial',repeat('b',64),repeat('a',64),repeat('a',40),
    'development',0,0,0,0,0,v_now);
  INSERT INTO migration.account_import_state(
    id,digest,roster_digest,staff_digest,source_commit,environment,
    exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',64),
    repeat('a',40),'development',v_now,v_now,'{}'::jsonb,decode('00','hex'));
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    team_id,primary_academy_group_id,image_url,is_active,is_archived,
    is_transferred,has_explicit_picker_fields)
  VALUES(v_player,'lefteria_fc_academy','CI Defender Student',4,'Defender',
    'Academy',NULL,NULL,NULL,true,false,false,true);
  INSERT INTO identity.canonical_contact_bindings(
    collection,source_id,kind,normalized_phone,player_id,import_digest)
  VALUES('players',v_player,'player_own','35799500621',v_player,repeat('c',64));
  INSERT INTO identity.mobile_record_links(
    identity_id,record_id,kind,role,normalized_phone,collection,is_primary)
  VALUES(v_player_identity,v_player,'player_own','player','35799500621','players',true);
  INSERT INTO hub.content(
    id,organization_id,kind,status,title,audience_parent,audience_player,
    provenance,version,approved_by,approved_at,published_at)
  VALUES
    (v_content,'lefteria_fc_academy','course','published',
      'CI defensive positioning lesson',false,true,'human',1,v_admin,
      v_now-interval '10 days',v_now-interval '9 days'),
    (v_parent_only,'lefteria_fc_academy','article','published',
      'CI parent-only lesson',true,false,'human',1,v_admin,
      v_now-interval '10 days',v_now-interval '9 days'),
    (v_foreign_content,'lefteria_fc','course','published',
      'CI first-team private lesson',false,true,'human',1,v_foreign,
      v_now-interval '10 days',v_now-interval '9 days');
  INSERT INTO hub.revisions(id,content_id,revision,body_text,text_review,visual_review)
  VALUES
    (v_revision,v_content,1,'CI private defender coursework','passed','passed'),
    (v_parent_rev,v_parent_only,1,'CI private parent advice','passed','passed'),
    (v_foreign_rev,v_foreign_content,1,'CI private first-team lesson','passed','passed');

  -- The admin list never contains a parent-only or foreign-club lesson.
  v_list:=hub.read_admin_classroom_curriculum(
    v_digest,'lefteria_fc_academy',0,20);
  IF v_list->>'organization_id'<>'lefteria_fc_academy'
    OR (v_list->>'total')::integer<>1
    OR jsonb_array_length(v_list->'items')<>1
    OR (v_list->'items'->0->>'id')::uuid<>v_content
    OR (v_list->'items'->0->>'version')::integer<>0
    OR v_list::text LIKE '%CI parent-only lesson%'
    OR v_list::text LIKE '%CI private defender coursework%'
    OR v_list::text LIKE '%CI first-team private lesson%'
  THEN RAISE EXCEPTION 'curriculum_private_admin_list_leaked'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_admin_classroom_curriculum(
      v_manager_digest,'lefteria_fc_academy',0,20);
  EXCEPTION WHEN insufficient_privilege THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'manager_classroom_curriculum_access'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.set_admin_classroom_lesson_subject(
      v_foreign_digest,'lefteria_fc_academy',v_content,v_revision,0,
      'defending','This club is not granted');
  EXCEPTION WHEN insufficient_privilege OR raise_exception THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'foreign_super_admin_curriculum_write'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.set_admin_classroom_lesson_subject(
      v_digest,'lefteria_fc_academy',v_parent_only,v_parent_rev,0,
      'defending','Not suitable player material');
  EXCEPTION WHEN no_data_found THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'parent_only_lesson_mapped_to_player'; END IF;

  -- First classification does not change the approved lesson or unlock it.
  v_change:=hub.set_admin_classroom_lesson_subject(
    v_digest,'lefteria_fc_academy',v_content,v_revision,0,
    'defending','Coach approved defensive topic');
  IF v_change->>'topic'<>'defending' OR (v_change->>'version')::integer<>1
  THEN RAISE EXCEPTION 'classroom_topic_review_failed'; END IF;
  v_result:=hub.read_mobile_player_classrooms(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_subject FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF (v_subject->>'approved_lessons')::integer<>1
    OR (v_subject->>'completed_lessons')::integer<>0
    OR (v_result->'lessons'->>'approved')::integer<>1
    OR v_result->'lessons'->>'next_title'<>'CI defensive positioning lesson'
    OR v_result::text LIKE '%CI private defender coursework%'
    OR v_result::text LIKE '%35799500621%'
  THEN RAISE EXCEPTION 'classroom_subject_pre_completion_wrong'; END IF;

  PERFORM hub.complete_mobile_next_education(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy',v_content,v_revision);
  v_result:=hub.read_mobile_player_classrooms(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_subject FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF (v_subject->>'approved_lessons')::integer<>1
    OR (v_subject->>'completed_lessons')::integer<>1
    OR (v_result->'lessons'->>'completed')::integer<>1
  THEN RAISE EXCEPTION 'classroom_subject_personal_completion_missing'; END IF;

  v_denied:=false;
  BEGIN
    PERFORM hub.set_admin_classroom_lesson_subject(
      v_digest,'lefteria_fc_academy',v_content,v_revision,0,
      'transition','Stale editor must fail');
  EXCEPTION WHEN serialization_failure THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'curriculum_stale_version_accepted'; END IF;

  -- The last reviewed revision changes: classification is now stale and
  -- disappears from the player's topic immediately, without deleting history.
  INSERT INTO hub.revisions(
    id,content_id,revision,body_text,text_review,visual_review)
  VALUES(v_revision2,v_content,2,'CI revised private defender lesson',
    'passed','passed');
  v_result:=hub.read_mobile_player_classrooms(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_subject FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='defending';
  IF (v_subject->>'approved_lessons')::integer<>0
    OR (v_subject->>'completed_lessons')::integer<>0
    OR (v_result->'lessons'->>'completed')::integer<>0
  THEN RAISE EXCEPTION 'stale_revision_advertised_in_classroom'; END IF;
  v_list:=hub.read_admin_classroom_curriculum(
    v_digest,'lefteria_fc_academy',0,20);
  IF NOT (v_list->'items'->0->>'needs_reverification')::boolean
    OR v_list->'items'->0->>'topic' IS NOT NULL
    OR (v_list->'items'->0->>'version')::integer<>1
  THEN RAISE EXCEPTION 'stale_revision_not_flagged_for_admin'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.set_admin_classroom_lesson_subject(
      v_digest,'lefteria_fc_academy',v_content,v_revision,1,
      'transition','Old revision must fail');
  EXCEPTION WHEN serialization_failure THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'stale_classroom_revision_accepted'; END IF;

  v_change:=hub.set_admin_classroom_lesson_subject(
    v_digest,'lefteria_fc_academy',v_content,v_revision2,1,
    'transition','Coach re-reviewed latest revision');
  IF v_change->>'topic'<>'transition' OR (v_change->>'version')::integer<>2
  THEN RAISE EXCEPTION 'classroom_revision_reverification_failed'; END IF;
  IF (SELECT count(*) FROM hub.classroom_lesson_subject_events
      WHERE content_id=v_content AND organization_id='lefteria_fc_academy')<>2
  THEN RAISE EXCEPTION 'classroom_immutable_event_history_lost'; END IF;
  v_result:=hub.read_mobile_player_classrooms(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_subject FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='transition';
  IF (v_subject->>'approved_lessons')::integer<>1
    OR (v_subject->>'completed_lessons')::integer<>0
  THEN RAISE EXCEPTION 'revised_classroom_completion_wrong'; END IF;

  -- Withdrawing a lesson removes it from both the normal lesson journey
  -- and the classroom. Super Admin cannot reclassify an unpublished item.
  UPDATE hub.content SET status='approved',published_at=NULL WHERE id=v_content;
  v_result:=hub.read_mobile_player_classrooms(
    v_player_identity,'35799500621','player',v_now+interval '1 hour',
    'lefteria_fc_academy');
  SELECT item INTO v_subject FROM jsonb_array_elements(v_result->'subjects') item
    WHERE item->>'topic'='transition';
  IF (v_subject->>'approved_lessons')::integer<>0
    OR (v_result->'lessons'->>'approved')::integer<>0
  THEN RAISE EXCEPTION 'withdrawn_classroom_content_advertised'; END IF;
  v_denied:=false;
  BEGIN
    PERFORM hub.set_admin_classroom_lesson_subject(
      v_digest,'lefteria_fc_academy',v_content,v_revision2,2,
      'defending','Cannot classify withdrawn lesson');
  EXCEPTION WHEN no_data_found THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'withdrawn_classroom_content_classified'; END IF;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_digest;
  v_denied:=false;
  BEGIN
    PERFORM hub.read_admin_classroom_curriculum(
      v_digest,'lefteria_fc_academy',0,20);
  EXCEPTION WHEN insufficient_privilege OR raise_exception THEN v_denied:=true; END;
  IF NOT v_denied THEN RAISE EXCEPTION 'revoked_curriculum_admin_read'; END IF;
END $classroom_curriculum_check$;
ROLLBACK;
