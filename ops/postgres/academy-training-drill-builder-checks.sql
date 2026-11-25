\set ON_ERROR_STOP on
-- Rollback-only synthetic group and exercise sequence lifecycle.
BEGIN;
DO $verify$
DECLARE
  owner constant text:='lefteria_fc_academy';
  a uuid:=gen_random_uuid();b uuid:=gen_random_uuid();
  da text:=repeat('7',64);db text:=repeat('8',64);
  ca uuid:=gen_random_uuid();cb uuid:=gen_random_uuid();
  la uuid:=gen_random_uuid();lb uuid:=gen_random_uuid();
  sa uuid:=gen_random_uuid();sb uuid:=gen_random_uuid();
  g text:='ci_drill_'||left(replace(gen_random_uuid()::text,'-',''),14);
  p uuid:=gen_random_uuid();d uuid:=gen_random_uuid();e uuid:=gen_random_uuid();
  day date:=(clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date+3;
  r jsonb;items jsonb;event_id text;
  player text:='ci_review_'||left(replace(gen_random_uuid()::text,'-',''),14);
  summary constant text:=E'Passing and warm-up\nBring water';
BEGIN
  INSERT INTO club.football_cities(id,name,slug) VALUES
    (ca,'CI Builder A','ci-builder-a-'||left(ca::text,8)),(cb,'CI Builder B','ci-builder-b-'||left(cb::text,8));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES
    (la,ca,'CI Site A','ci-site-a-'||left(la::text,8)),(lb,cb,'CI Site B','ci-site-b-'||left(lb::text,8));
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id) VALUES(sa,owner,ca),(sb,owner,cb);
  INSERT INTO club.academy_groups(id,organization_id,name) VALUES(g,owner,'CI private exercise group');
  INSERT INTO club.academy_group_location_assignments(organization_id,group_id,city_id,location_id,scope_id) VALUES(owner,g,ca,la,sa);
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash) VALUES
    (a,'ci_builder_a_'||left(a::text,8),'CI Manager A','manager',true,'ci_not_real'),
    (b,'ci_builder_b_'||left(b::text,8),'CI Manager B','manager',true,'ci_not_real');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) VALUES(a,owner),(b,owner);
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),da,a,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 hour'),
    (gen_random_uuid(),db,b,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role) VALUES(a,la,'location_manager'),(b,lb,'location_manager');
  INSERT INTO club.academy_coaching_drills(id,organization_id,title,category,age_band,duration_minutes,objective,instructions,status,version) VALUES
    (d,owner,'CI Passing','technical','all',15,'Passing','Pass in pairs','approved',1),
    (e,owner,'CI Warm up','warmup','all',10,'Prepare','Jog safely','approved',1);
  PERFORM club.write_admin_academy_training_plan(da,owner,p,g,day,TIME '17:00',60,'CI plan','CI pitch','',0,false);
  PERFORM club.approve_admin_academy_training_plan(da,owner,p,1);
  items:=jsonb_build_array(jsonb_build_object('drill_id',d,'drill_version',1,'duration_minutes',20),
    jsonb_build_object('drill_id',e,'drill_version',1,'duration_minutes',10));
  BEGIN
    PERFORM club.write_admin_training_drills(db,g,p,1,items);
    RAISE EXCEPTION 'foreign_site_write_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  r:=club.write_admin_training_drills(da,g,p,1,items);
  IF (r->>'version')::int<>2 OR jsonb_array_length(r->'drills')<>2 OR r->'drills'->0->>'title'<>'CI Passing'
    THEN RAISE EXCEPTION 'exercise_save_invalid'; END IF;
  IF jsonb_array_length(club.read_admin_academy_training_plan_approvals(da,owner,g,day,day)->'approvals')<>0
    THEN RAISE EXCEPTION 'exercise_edit_preserved_stale_approval'; END IF;
  BEGIN
    PERFORM club.read_admin_training_drills(db,g,p,2);
    RAISE EXCEPTION 'foreign_site_read_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_drills(da,g,p,1,items);
    RAISE EXCEPTION 'stale_write_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_drills(da,g,p,2,jsonb_build_array(jsonb_build_object('drill_id',d,'drill_version',1,'duration_minutes',61)));
    RAISE EXCEPTION 'duration_overflow_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_drills(da,g,p,2,items||jsonb_build_array(items->0));
    RAISE EXCEPTION 'duplicate_drill_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM club.approve_admin_academy_training_plan(da,owner,p,2);
    RAISE EXCEPTION 'schedule_only_approval_accepted';
  EXCEPTION WHEN object_not_in_prerequisite_state THEN NULL; END;
  UPDATE club.academy_coaching_drills SET title='CI changed',status='archived',version=2 WHERE id=d;
  r:=club.write_admin_training_drills(da,g,p,2,jsonb_build_array(items->1,items->0));
  IF (r->>'version')::int<>3 OR r->'drills'->1->>'title'<>'CI Passing'
    THEN RAISE EXCEPTION 'frozen_snapshot_lost'; END IF;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(da,owner,p,g,day,TIME '17:00',15,'CI plan','CI pitch','',3,false);
    RAISE EXCEPTION 'schedule_shrink_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(db,owner,p,g,day,TIME '17:00',60,'CI plan','CI pitch','',3,false);
    RAISE EXCEPTION 'legacy_foreign_site_edit_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  r:=club.read_admin_training_drills(da,g,p,3);
  IF jsonb_array_length(r->'drills')<>2 OR NOT EXISTS(
    SELECT 1 FROM club.academy_training_plan_events WHERE plan_id=p AND version=2 AND snapshot->'drill_snapshots'->0->>'title'='CI Passing')
    THEN RAISE EXCEPTION 'immutable_sequence_history_missing'; END IF;
  r:=club.write_admin_training_drills(da,g,p,3,'[]'::jsonb);
  IF (r->>'version')::int<>4 OR jsonb_array_length(r->'drills')<>0 THEN RAISE EXCEPTION 'clear_sequence_failed'; END IF;
  BEGIN
    PERFORM club.write_admin_training_drills(da,g,p,4,items);
    RAISE EXCEPTION 'archived_catalogue_reattached';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  -- Complete review of the new exact version, using only explicit
  -- member-safe text. The original schedule-only route stays blocked.
  r:=club.write_admin_training_drills(da,g,p,4,jsonb_build_array(items->1));
  IF (r->>'version')::int<>5 THEN RAISE EXCEPTION 'review_setup_version_wrong'; END IF;
  BEGIN
    PERFORM club.approve_admin_training_drill_plan(db,g,p,5,summary,true);
    RAISE EXCEPTION 'foreign_complete_review_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.approve_admin_training_drill_plan(da,g,p,5,summary,false);
    RAISE EXCEPTION 'unconfirmed_complete_review_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM club.approve_admin_training_drill_plan(da,g,p,4,summary,true);
    RAISE EXCEPTION 'stale_complete_review_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  r:=club.approve_admin_training_drill_plan(da,g,p,5,summary,true);
  IF r->>'published'<>'false' OR r->>'member_summary'<>summary OR
    NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals
      WHERE plan_id=p AND plan_version=5 AND plan_snapshot->>'review_kind'='complete_drill_plan'
        AND plan_snapshot->'drill_snapshots'->0->>'instructions'='Jog safely')
    THEN RAISE EXCEPTION 'complete_review_snapshot_missing'; END IF;
  INSERT INTO club.roster_import_state(id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at)
    VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),'development',0,1,1,0,clock_timestamp());
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,team_id,primary_academy_group_id,image_url,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(player,owner,'Synthetic member',17,'Midfielder','Academy',NULL,g,NULL,true,false,false,true);
  INSERT INTO club.calendar_import_state(id,digest,roster_digest,source_commit,environment,time_zone,is_complete,
    exported_at,applied_at,counts_json,manifest_bytes)
    VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',40),'development','Asia/Nicosia',true,
      clock_timestamp(),clock_timestamp(),'{"quarantined_rows":0}'::jsonb,decode('7b7d','hex'));
  BEGIN
    PERFORM club.preview_admin_academy_training_distribution(db,owner,p,5);
    RAISE EXCEPTION 'foreign_complete_preview_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  r:=club.preview_admin_academy_training_distribution(da,owner,p,5);
  IF r->>'member_summary'<>summary OR r ? 'drill_snapshots' OR r ? 'notes' OR (r->>'eligible_player_count')::int<>1
    THEN RAISE EXCEPTION 'unsafe_complete_preview'; END IF;
  BEGIN
    PERFORM club.publish_admin_academy_training_plan(db,owner,p,5,true);
    RAISE EXCEPTION 'foreign_complete_publication_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  r:=club.publish_admin_academy_training_plan(da,owner,p,5,true);
  event_id:=r->>'event_id';
  IF r->>'published'<>'true' OR NOT EXISTS(
    SELECT 1 FROM club.calendar_items c JOIN club.calendar_item_scopes s ON s.kind=c.kind AND s.event_id=c.id
    WHERE c.id=event_id AND c.organization_id=owner AND s.record_id=g AND s.scope_kind='group'
      AND c.member_projection_json->>'summary'=summary
      AND NOT (c.member_projection_json ?| ARRAY['notes','drill_snapshots','instructions','review_kind'])
      AND position('Jog safely' in c.member_projection_json::text)=0)
    THEN RAISE EXCEPTION 'unsafe_complete_publication'; END IF;
  IF NOT club.os_native_training_primary_member(owner,event_id,player)
    THEN RAISE EXCEPTION 'native_member_scope_missing'; END IF;
  BEGIN
    PERFORM club.write_admin_training_drills(da,g,p,5,'[]'::jsonb);
    RAISE EXCEPTION 'published_sequence_edit_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    PERFORM club.cancel_admin_academy_training_publication(db,owner,p,5,'Synthetic cancellation',true);
    RAISE EXCEPTION 'foreign_complete_cancel_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM club.cancel_admin_academy_training_publication(da,owner,p,5,'Synthetic cancellation',true);
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp() WHERE token_digest=da;
  BEGIN
    PERFORM club.read_admin_training_drills(da,g,p,5);
    RAISE EXCEPTION 'revoked_access_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $verify$;
ROLLBACK;
