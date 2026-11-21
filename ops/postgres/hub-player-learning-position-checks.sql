\set ON_ERROR_STOP on
\set ECHO none
-- Only synthetic, disposable migrated PostgreSQL. No real player, receipt,
-- external sender, AI worker or production Hostinger connection.
BEGIN;
DO $learning_position_test$
DECLARE
  super_id uuid:=gen_random_uuid();
  manager_id uuid:=gen_random_uuid();
  other_id uuid:=gen_random_uuid();
  learner uuid:=gen_random_uuid();
  defender_identity uuid:=gen_random_uuid();
  forward_player text:='ci_hub_forward_'||substr(md5(gen_random_uuid()::text),1,16);
  defender_player text:='ci_hub_defender_'||substr(md5(gen_random_uuid()::text),1,16);
  super_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  manager_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  other_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_quiz uuid:=gen_random_uuid();
  result jsonb;quiz jsonb;denied boolean;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
  VALUES(super_id,'ci-learning-super-'||substr(md5(super_id::text),1,8),
           'Synthetic learning Super Admin','super_admin',true,'fixture_only'),
        (manager_id,'ci-learning-manager-'||substr(md5(manager_id::text),1,8),
           'Synthetic learning manager','manager',true,'fixture_only'),
        (other_id,'ci-learning-other-'||substr(md5(other_id::text),1,8),
           'Synthetic other-owner Super Admin','super_admin',true,'fixture_only');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES(super_id,'lefteria_fc_academy'),(manager_id,'lefteria_fc_academy'),
        (other_id,'lefteria_fc');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES(gen_random_uuid(),super_digest,super_id,repeat('a',64),
           clock_timestamp(),clock_timestamp()+interval '1 hour'),
        (gen_random_uuid(),manager_digest,manager_id,repeat('a',64),
           clock_timestamp(),clock_timestamp()+interval '1 hour'),
        (gen_random_uuid(),other_digest,other_id,repeat('a',64),
           clock_timestamp(),clock_timestamp()+interval '1 hour');

  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES(learner,'35799500451','player','Synthetic verified winger',true,'mobile',clock_timestamp()),
        (defender_identity,'35799500452','player','Synthetic verified fullback',true,'mobile',clock_timestamp());
  INSERT INTO club.roster_import_state
    (id,digest,source_repository,source_commit,environment,
     teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
         'development',0,0,2,0,clock_timestamp());
  INSERT INTO migration.staff_relationship_import_state
    (id,digest,roster_digest,source_commit,environment,staff_count,
     team_assignments_count,group_assignments_count,group_coaches_count,
     contacts_count,applied_at)
  VALUES('initial',repeat('b',64),repeat('a',64),repeat('a',40),
         'development',0,0,0,0,0,clock_timestamp());
  INSERT INTO migration.account_import_state
    (id,digest,roster_digest,staff_digest,source_commit,environment,
     exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',64),
         repeat('a',40),'development',clock_timestamp(),clock_timestamp(),
         '{}'::jsonb,decode('00','hex'));
  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,team_id,
    primary_academy_group_id,image_url,is_active,is_archived,
    is_transferred,has_explicit_picker_fields)
  VALUES(forward_player,'lefteria_fc_academy','Synthetic broad Forward',10,
         'Forward','Academy',NULL,NULL,NULL,true,false,false,true),
        (defender_player,'lefteria_fc_academy','Synthetic broad Defender',4,
         'Defender','Academy',NULL,NULL,NULL,true,false,false,true);
  INSERT INTO identity.canonical_contact_bindings(
    collection,source_id,kind,normalized_phone,player_id,import_digest)
  VALUES('players',forward_player,'player_own','35799500451',forward_player,repeat('c',64)),
        ('players',defender_player,'player_own','35799500452',defender_player,repeat('c',64));
  INSERT INTO identity.mobile_record_links(
    identity_id,record_id,kind,role,normalized_phone,collection,is_primary)
  VALUES(learner,forward_player,'player_own','player','35799500451','players',true),
        (defender_identity,defender_player,'player_own','player','35799500452','players',true);

  -- Broad "Forward" must never be silently interpreted as striker or winger.
  IF hub.assert_current_quiz_player(learner,'35799500451','player',
       clock_timestamp()+interval '1 hour','lefteria_fc_academy')<>'all'
  THEN RAISE EXCEPTION 'unverified_forward_was_guessed'; END IF;
  result:=hub.read_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',forward_player);
  IF result->>'effective_position'<>'all'
    OR (result->>'version')::bigint<>0
    OR result->>'specialization' IS NOT NULL
  THEN RAISE EXCEPTION 'unverified_forward_admin_read_invalid'; END IF;

  -- Manager, ungranted Super Admin, and foreign organization cannot approve.
  denied:=false;
  BEGIN
    PERFORM hub.set_admin_player_learning_position(
      manager_digest,'lefteria_fc_academy',forward_player,'Forward',0,'winger',
      'Coaching staff verified right winger');
  EXCEPTION WHEN insufficient_privilege OR raise_exception THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'manager_approved_player_specialization'; END IF;
  denied:=false;
  BEGIN
    PERFORM hub.read_admin_player_learning_position(
      other_digest,'lefteria_fc_academy',forward_player);
  EXCEPTION WHEN insufficient_privilege OR raise_exception THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'ungranted_super_admin_read_specialization'; END IF;
  denied:=false;
  BEGIN
    PERFORM hub.set_admin_player_learning_position(
      super_digest,'lefteria_fc',forward_player,'Forward',0,'striker',
      'Approved forward training specialty');
  EXCEPTION WHEN insufficient_privilege OR raise_exception OR no_data_found THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'cross_club_specialization_applied'; END IF;

  result:=hub.set_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',forward_player,'Forward',0,'winger',
    'Coaching staff verified right winger');
  IF result->>'specialization'<>'winger'
    OR result->>'roster_position'<>'Forward'
    OR (result->>'version')::bigint<>1
    OR hub.assert_current_quiz_player(learner,'35799500451','player',
      clock_timestamp()+interval '1 hour','lefteria_fc_academy')<>'winger'
  THEN RAISE EXCEPTION 'verified_winger_not_selected_by_player'; END IF;
  IF (SELECT count(*) FROM hub.player_learning_specialization_events
      WHERE player_id=forward_player AND actor_id=super_id)<>1
  THEN RAISE EXCEPTION 'specialization_audit_missing'; END IF;

  -- Week 1 is still the individual starting point; an independent approved
  -- specialist quiz beats the general bank without exposing other positions.
  INSERT INTO hub.player_quizzes(
    id,organization_id,week_number,topic,position,title,status,
    reviewed_by,reviewed_at,published_at,created_by)
  VALUES(v_quiz,'lefteria_fc_academy',1,'positioning','winger',
         'Synthetic Winger Week One','published',super_id,
         clock_timestamp()-interval '2 days',
         clock_timestamp()-interval '1 day',super_id);
  INSERT INTO hub.player_quiz_questions(
    id,quiz_id,sequence,prompt,choices,correct_index,explanation)
  VALUES(gen_random_uuid(),v_quiz,1,'When should a winger stay wide on attack?',
           '["Stay wide","Leave","Stop","None"]'::jsonb,0,'Fixture private answer'),
        (gen_random_uuid(),v_quiz,2,'How does a winger help a quick transition?',
           '["Press","Sprint","Wait","Rest"]'::jsonb,1,'Fixture private answer'),
        (gen_random_uuid(),v_quiz,3,'Which support angle helps the fullback?',
           '["Outside","Inside","None","Back"]'::jsonb,1,'Fixture private answer');
  PERFORM hub.start_mobile_player_quiz_track(
    learner,'35799500451','player',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  quiz:=hub.read_mobile_player_weekly_quiz(
    learner,'35799500451','player',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (quiz->'quiz'->>'id')::uuid IS DISTINCT FROM v_quiz
    OR (quiz->'quiz'->>'week_number')::integer<>1
    OR quiz::text LIKE '%correct_index%'
    OR quiz::text LIKE '%Fixture private answer%'
  THEN RAISE EXCEPTION 'verified_winger_week_one_quiz_invalid'; END IF;

  -- A Defender may be independently verified as fullback, not striker.
  result:=hub.set_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',defender_player,'Defender',0,'fullback',
    'Coach verified both fullback duties');
  IF hub.assert_current_quiz_player(defender_identity,'35799500452','player',
    clock_timestamp()+interval '1 hour','lefteria_fc_academy')<>'fullback'
  THEN RAISE EXCEPTION 'verified_fullback_not_selected'; END IF;
  denied:=false;
  BEGIN
    PERFORM hub.set_admin_player_learning_position(
      super_digest,'lefteria_fc_academy',defender_player,'Defender',1,'striker',
      'This is not a compatible role');
  EXCEPTION WHEN invalid_parameter_value THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'incompatible_defender_striker_accepted'; END IF;
  denied:=false;
  BEGIN
    PERFORM hub.set_admin_player_learning_position(
      super_digest,'lefteria_fc_academy',forward_player,'Forward',0,'striker',
      'Stale editor version should fail');
  EXCEPTION WHEN serialization_failure THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'stale_specialization_version_accepted'; END IF;

  -- Changing canonical position invalidates the historical specialization
  -- immediately, until a Super Admin verifies the NEW canonical role.
  UPDATE club.players SET position='Midfielder' WHERE id=forward_player;
  IF hub.assert_current_quiz_player(learner,'35799500451','player',
    clock_timestamp()+interval '1 hour','lefteria_fc_academy')<>'midfielder'
  THEN RAISE EXCEPTION 'stale_specialization_survived_roster_change'; END IF;
  result:=hub.read_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',forward_player);
  IF result->>'specialization' IS NOT NULL
    OR NOT (result->>'needs_reverification')::boolean
    OR (result->>'version')::bigint<>1
  THEN RAISE EXCEPTION 'roster_change_not_flagged_for_reverification'; END IF;
  denied:=false;
  BEGIN
    PERFORM hub.set_admin_player_learning_position(
      super_digest,'lefteria_fc_academy',forward_player,'Forward',1,'winger',
      'Expected roster should fail now');
  EXCEPTION WHEN serialization_failure THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'stale_canonical_roster_write_accepted'; END IF;
  result:=hub.set_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',forward_player,'Midfielder',1,'winger',
    'Coach reverified midfielder wing role');
  IF (result->>'version')::bigint<>2
    OR (result->>'needs_reverification')::boolean
    OR result->>'effective_position'<>'winger'
  THEN RAISE EXCEPTION 'position_reverification_invalid'; END IF;
  result:=hub.set_admin_player_learning_position(
    super_digest,'lefteria_fc_academy',forward_player,'Midfielder',2,'all',
    'Coach withdrew individual specialty');
  IF (result->>'version')::bigint<>3
    OR result->>'specialization' IS NOT NULL
    OR result->>'effective_position'<>'midfielder'
  THEN RAISE EXCEPTION 'specialty_revocation_did_not_fallback'; END IF;
  IF (SELECT count(*) FROM hub.player_learning_specialization_events
      WHERE player_id=forward_player)<>3
  THEN RAISE EXCEPTION 'audit_history_lost_on_revoke'; END IF;

  -- Session revocation cannot be bypassed using the previously valid digest.
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=super_digest;
  denied:=false;
  BEGIN
    PERFORM hub.read_admin_player_learning_position(
      super_digest,'lefteria_fc_academy',forward_player);
  EXCEPTION WHEN insufficient_privilege OR raise_exception THEN denied:=true; END;
  IF NOT denied THEN RAISE EXCEPTION 'revoked_admin_session_read_specialization'; END IF;
END
$learning_position_test$;
ROLLBACK;
