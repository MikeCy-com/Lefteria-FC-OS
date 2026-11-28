\set ON_ERROR_STOP on
\set ECHO none

-- Synthetic, rollback-only checks on the disposable CI database. Never
-- import live voters, open a real election or alter an existing snapshot.
BEGIN;
DO $potm_checks$
DECLARE
  v_fan uuid:=gen_random_uuid(); v_parent uuid:=gen_random_uuid();
  v_other uuid:=gen_random_uuid(); v_disabled uuid:=gen_random_uuid();
  v_admin uuid:=gen_random_uuid();
  v_phone text:='35799673101'; v_other_phone text:='35799673102';
  v_expiry timestamptz:=clock_timestamp()+interval '1 hour';
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_key text:=to_char(v_month,'YYYY-MM');
  v_version integer:=99001;
  v_one text:='ci-potm-player-one'; v_two text:='ci-potm-player-two';
  v_inactive text:='ci-potm-inactive'; v_youth text:='ci-potm-youth';
  v_result jsonb; v_bad text;
BEGIN
  IF current_database() IN ('postgres','template0','template1')
    OR EXISTS(SELECT 1 FROM public_sections.current)
    OR EXISTS(SELECT 1 FROM public_content.import_state)
    OR EXISTS(SELECT 1 FROM club.potm_periods)
  THEN RAISE EXCEPTION 'POTM check requires a fresh disposable fixture'; END IF;

  INSERT INTO identity.mobile_identities
    (id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES
    (v_fan,v_phone,'fan','CI Fan',true,'mobile',clock_timestamp()),
    (v_parent,v_phone,'parent','CI Parent',true,'mobile',clock_timestamp()),
    (v_other,v_other_phone,'fan','CI Other Fan',true,'mobile',clock_timestamp()),
    (v_disabled,'35799673103','fan','CI Disabled Fan',false,'mobile',clock_timestamp());
  INSERT INTO identity.admin_accounts
    (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES(v_admin,'ci-potm-super-'||left(md5(v_admin::text),8),
    'CI POTM Approver','super_admin',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_admin,'lefteria_fc');

  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES
    (v_one,'lefteria_fc','CI First One',7,'Forward','First Team',true,false,false,true),
    (v_two,'lefteria_fc','CI First Two',8,'Midfielder','First Team',true,false,false,true),
    (v_inactive,'lefteria_fc','CI Inactive',9,'Defender','First Team',false,false,false,true),
    (v_youth,'lefteria_fc_academy','CI Youth',10,'Forward','Academy',true,false,false,true);
  INSERT INTO public_content.import_state(id,digest,source_repository,source_commit,
    environment,documents_count,sources_count,applied_at)
  VALUES('initial',repeat('d',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
    'development',0,0,clock_timestamp());
  INSERT INTO public_sections.snapshots(version,digest,base_public_digest,source_repository,
    source_commit,environment,exported_at,applied_at,documents_count,sources_count,issues_count)
  VALUES(v_version,repeat('e',64),repeat('d',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
    'development',clock_timestamp(),clock_timestamp(),5,0,0),
    (v_version+1,repeat('f',64),repeat('d',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
    'development',clock_timestamp(),clock_timestamp(),0,0,0);
  INSERT INTO public_sections.current(id,snapshot_version) VALUES('current',v_version);
  INSERT INTO public_sections.documents(snapshot_version,kind,organization_id,id,data_json,display_order)
  SELECT v_version,'player',p.organization_id,p.id,
    jsonb_build_object('id',p.id,'organization_id',p.organization_id,'team_type',p.team_type,'name',p.name),p.number
  FROM club.players p WHERE p.id IN (v_one,v_two,v_inactive,v_youth);
  INSERT INTO public_sections.documents(snapshot_version,kind,organization_id,id,data_json,display_order)
    VALUES(v_version,'module','lefteria_fc','potm',
      '{"id":"potm","organization_id":"lefteria_fc","status":"empty","quarantined_count":0}'::jsonb,0);

  v_result:=club.read_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry);
  IF v_result IS DISTINCT FROM jsonb_build_object('month_key',v_key,'voting_open',false,
      'my_vote_player_id',NULL,'revision',0)
  THEN RAISE EXCEPTION 'Default voting gate not closed'; END IF;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,0,v_one);
    RAISE EXCEPTION 'Unapproved period accepted a vote';
  EXCEPTION WHEN object_not_in_prerequisite_state THEN NULL; END;

  INSERT INTO club.potm_periods(month_start,snapshot_version,approved_by,approval_reference,legacy_stopped_at)
    VALUES(v_month,v_version,v_admin,'CI synthetic cutover only',
      (v_month::timestamp AT TIME ZONE 'Asia/Nicosia')-interval '1 second');
  v_result:=club.read_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry);
  IF (v_result->>'voting_open')::boolean IS DISTINCT FROM true
  THEN RAISE EXCEPTION 'Reviewed fixture did not open'; END IF;

  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_parent,v_phone,'parent',v_expiry,v_key,0,v_one);
    RAISE EXCEPTION 'Parent role accepted a second ballot';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_parent,v_phone,'fan',v_expiry,v_key,0,v_one);
    RAISE EXCEPTION 'Forged Fan role accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.read_mobile_potm_ballot(v_fan,v_other_phone,'fan',v_expiry);
    RAISE EXCEPTION 'Cross-account read accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.read_mobile_potm_ballot(v_disabled,'35799673103','fan',v_expiry);
    RAISE EXCEPTION 'Inactive Fan accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',clock_timestamp()-interval '1 second',v_key,0,v_one);
    RAISE EXCEPTION 'Expired session accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,
      to_char(v_month-interval '1 month','YYYY-MM'),0,v_one);
    RAISE EXCEPTION 'Previous month accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  FOREACH v_bad IN ARRAY ARRAY[v_youth,v_inactive,'missing-player',' ']
  LOOP
    BEGIN
      PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,0,v_bad);
      RAISE EXCEPTION 'Ineligible candidate accepted';
    EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  END LOOP;

  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,0,v_one);
  IF v_result IS DISTINCT FROM jsonb_build_object('month_key',v_key,'voting_open',true,
    'my_vote_player_id',v_one,'revision',1)
  THEN RAISE EXCEPTION 'Initial ballot not persisted'; END IF;
  v_result:=club.read_mobile_potm_ballot(v_other,v_other_phone,'fan',v_expiry);
  IF (v_result->>'revision')::int<>0 OR v_result->>'my_vote_player_id' IS NOT NULL
  THEN RAISE EXCEPTION 'Another voter ballot leaked'; END IF;

  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,1,v_one);
  IF (v_result->>'revision')::int<>1 THEN RAISE EXCEPTION 'Unchanged vote added a revision'; END IF;
  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,1,v_two);
  IF (v_result->>'revision')::int<>2 OR v_result->>'my_vote_player_id'<>v_two
  THEN RAISE EXCEPTION 'Change not persisted'; END IF;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,1,v_one);
    RAISE EXCEPTION 'Stale request overwrote new vote';
  EXCEPTION WHEN serialization_failure THEN NULL; END;
  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,2,NULL);
  IF (v_result->>'revision')::int<>3 OR v_result->>'my_vote_player_id' IS NOT NULL
  THEN RAISE EXCEPTION 'Withdrawal not persisted'; END IF;
  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,3,NULL);
  IF (v_result->>'revision')::int<>3 THEN RAISE EXCEPTION 'Repeat withdrawal added history'; END IF;
  v_result:=club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,3,v_one);
  IF (v_result->>'revision')::int<>4 THEN RAISE EXCEPTION 'Re-vote not persisted'; END IF;
  IF (SELECT count(*) FROM club.potm_ballots WHERE month_start=v_month AND identity_id=v_fan)<>1
    OR (SELECT count(*) FROM club.potm_ballot_events WHERE month_start=v_month AND identity_id=v_fan)<>4
    OR NOT EXISTS(SELECT 1 FROM club.potm_ballot_events WHERE month_start=v_month AND identity_id=v_fan
      AND revision=3 AND previous_player_id=v_two AND player_id IS NULL)
  THEN RAISE EXCEPTION 'One-vote or private history invariant failed'; END IF;

  UPDATE club.players SET is_active=false WHERE id=v_two;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,4,v_two);
    RAISE EXCEPTION 'Stale public roster overrode canonical inactive flag';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  UPDATE identity.mobile_identities SET is_active=false WHERE id=v_fan;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,4,NULL);
    RAISE EXCEPTION 'Revoked Fan mutated vote';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.mobile_identities SET is_active=true WHERE id=v_fan;

  UPDATE public_sections.current SET snapshot_version=v_version+1 WHERE id='current';
  v_result:=club.read_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry);
  IF (v_result->>'voting_open')::boolean IS DISTINCT FROM false
    OR v_result->>'my_vote_player_id'<>v_one
  THEN RAISE EXCEPTION 'New snapshot did not close gate while preserving own ballot'; END IF;
  UPDATE public_sections.current SET snapshot_version=v_version WHERE id='current';
  -- A retained vote from the current month invalidates native cutover.
  INSERT INTO migration.public_sections_source(snapshot_version,collection,ordinal,source_json)
    VALUES(v_version,'potm_votes',0,jsonb_build_object('month_key',v_key,'player_id',v_one)::text);
  UPDATE public_sections.snapshots SET sources_count=1 WHERE version=v_version;
  BEGIN
    PERFORM club.write_mobile_potm_ballot(v_fan,v_phone,'fan',v_expiry,v_key,4,NULL);
    RAISE EXCEPTION 'Native write accepted over retained legacy vote';
  EXCEPTION WHEN object_not_in_prerequisite_state THEN NULL; END;
  IF (SELECT revision FROM club.potm_ballots WHERE month_start=v_month AND identity_id=v_fan)<>4
  THEN RAISE EXCEPTION 'Denied write changed history'; END IF;
  RAISE NOTICE 'POTM private ballot lifecycle and legacy cutover checks passed';
END $potm_checks$;
ROLLBACK;
