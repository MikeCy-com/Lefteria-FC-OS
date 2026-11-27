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
  r jsonb;items jsonb;v_event text;
  coach uuid:=gen_random_uuid();dc text:=repeat('9',64);cp uuid:=gen_random_uuid();
  coach_staff text:='ci_coach_'||left(replace(gen_random_uuid()::text,'-',''),14);
  other_group text:='ci_other_'||left(replace(gen_random_uuid()::text,'-',''),14);
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
  -- Canonical staff assignment, not just an Academy organization grant.
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
    VALUES(coach,'ci_coach_'||left(coach::text,8),'CI Assigned Coach','coach',true,'ci_not_real');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) VALUES(coach,owner);
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),dc,coach,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,has_explicit_active,has_explicit_archived)
    VALUES(coach_staff,'CI Coach','Youth Coach','Academy',true,false,true,true);
  INSERT INTO identity.admin_staff_links(admin_account_id,staff_id) VALUES(coach,coach_staff);
  INSERT INTO club.staff_academy_groups(staff_id,academy_group_id,organization_id,from_plural,from_singular,from_group_coach,from_admin)
    VALUES(coach_staff,g,owner,false,false,false,true);
  INSERT INTO club.academy_groups(id,organization_id,name) VALUES(other_group,owner,'CI Other Group');
  INSERT INTO club.academy_group_location_assignments(organization_id,group_id,city_id,location_id,scope_id)
    VALUES(owner,other_group,cb,lb,sb);
  r:=club.read_admin_coach_training_groups(dc);
  IF jsonb_array_length(r->'groups')<>1 OR r->'groups'->0->>'id'<>g THEN RAISE EXCEPTION 'coach_directory_scope_invalid'; END IF;
  BEGIN
    PERFORM club.read_admin_academy_training_plans(dc,owner,other_group,day,day+1);
    RAISE EXCEPTION 'coach_foreign_group_read_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_academy_training_plan(dc,owner,gen_random_uuid(),other_group,day,TIME '20:00',60,'Foreign draft','Pitch','',0,false);
    RAISE EXCEPTION 'coach_foreign_group_write_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  r:=club.write_admin_academy_training_plan(dc,owner,cp,g,day+1,TIME '18:00',60,'Coach draft','Pitch','',0,false);
  IF (r->>'version')::int<>1 THEN RAISE EXCEPTION 'coach_draft_create_failed'; END IF;
  r:=club.write_admin_training_drills(dc,g,cp,1,jsonb_build_array(jsonb_build_object('drill_id',d,'drill_version',1,'duration_minutes',20)));
  IF (r->>'version')::int<>2 THEN RAISE EXCEPTION 'coach_drill_save_failed'; END IF;
  BEGIN
    PERFORM club.approve_admin_training_drill_plan(dc,g,cp,2,'Coach must not approve',true);
    RAISE EXCEPTION 'coach_self_approval_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.approve_admin_academy_training_plan(dc,owner,cp,2);
    RAISE EXCEPTION 'coach_legacy_approval_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.preview_admin_academy_training_distribution(dc,owner,cp,2);
    RAISE EXCEPTION 'coach_publication_preview_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.publish_admin_academy_training_plan(dc,owner,cp,2,true);
    RAISE EXCEPTION 'coach_publish_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.submit_admin_training_plan(dc,g,cp,2,false);
    RAISE EXCEPTION 'coach_unconfirmed_submit_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  r:=club.submit_admin_training_plan(dc,g,cp,2,true);
  IF r->>'submitted'<>'true' OR r->>'published'<>'false' THEN RAISE EXCEPTION 'coach_submit_result_invalid'; END IF;
  IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_admin_academy_training_plans(da,owner,g,day,day+1)->'plans') x
    WHERE x->>'id'=cp::text AND (x->>'submitted_version')::int=2)
    THEN RAISE EXCEPTION 'manager_submission_missing'; END IF;
  r:=club.write_admin_academy_training_plan(dc,owner,cp,g,day+1,TIME '18:00',60,'Coach revised draft','Pitch','',2,false);
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_admin_academy_training_plans(da,owner,g,day,day+1)->'plans') x
    WHERE x->>'id'=cp::text AND x->>'submitted_version' IS NOT NULL)
    THEN RAISE EXCEPTION 'stale_coach_submission_visible'; END IF;
  PERFORM club.submit_admin_training_plan(dc,g,cp,3,true);
  PERFORM club.approve_admin_academy_training_plan(da,owner,cp,3);
  IF NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=cp AND plan_version=3
    AND plan_snapshot->>'review_kind'='schedule_only' AND NOT (plan_snapshot ?| ARRAY['notes','drill_snapshots','member_summary']))
    THEN RAISE EXCEPTION 'schedule_only_approval_missing'; END IF;
  r:=club.write_admin_training_delivery(dc,g,cp,3,0,jsonb_build_array(jsonb_build_object('drill_id',d,'drill_version',1,'duration_minutes',25)));
  IF (r->>'sequence_version')::int<>1 OR (r->>'version')::int<>3 OR (r->'drills'->0->>'duration_minutes')::int<>25
    THEN RAISE EXCEPTION 'coach_independent_delivery_failed'; END IF;
  IF NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=cp AND plan_version=3)
    THEN RAISE EXCEPTION 'coach_delivery_invalidated_schedule_approval'; END IF;
  BEGIN
    PERFORM club.write_admin_training_delivery(dc,g,cp,3,0,'[]'::jsonb);
    RAISE EXCEPTION 'stale_delivery_write_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_delivery(dc,other_group,cp,3,1,'[]'::jsonb);
    RAISE EXCEPTION 'foreign_delivery_group_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_delivery(dc,g,cp,3,1,jsonb_build_array(jsonb_build_object('drill_id',e,'drill_version',1,'duration_minutes',61)));
    RAISE EXCEPTION 'delivery_duration_overflow_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_training_drills(dc,g,cp,3,'[]'::jsonb);
    RAISE EXCEPTION 'coach_approved_edit_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  DELETE FROM club.staff_academy_groups WHERE staff_id=coach_staff AND academy_group_id=g;
  BEGIN
    PERFORM club.read_admin_training_drills(dc,g,cp,3);
    RAISE EXCEPTION 'revoked_coach_assignment_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.read_admin_training_delivery(dc,g,cp,3);
    RAISE EXCEPTION 'revoked_delivery_access_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF jsonb_array_length(club.read_admin_coach_training_groups(dc)->'groups')<>0 THEN RAISE EXCEPTION 'revoked_coach_directory_stale'; END IF;
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
  PERFORM club.approve_admin_academy_training_plan(da,owner,p,2);
  IF NOT EXISTS(SELECT 1 FROM club.academy_training_plan_approvals WHERE plan_id=p AND plan_version=2
    AND plan_snapshot->>'review_kind'='schedule_only' AND NOT (plan_snapshot ?| ARRAY['notes','drill_snapshots','member_summary']))
  THEN RAISE EXCEPTION 'schedule_only_approval_missing'; END IF;
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
  -- member-safe text. This optional summary remains separately reviewed.
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
  v_event:=r->>'event_id';
  IF r->>'published'<>'true' OR NOT EXISTS(
    SELECT 1 FROM club.calendar_items c JOIN club.calendar_item_scopes s ON s.kind=c.kind AND s.event_id=c.id
    WHERE c.id=v_event AND c.organization_id=owner AND s.record_id=g AND s.scope_kind='group'
      AND c.member_projection_json->>'summary'=summary
      AND NOT (c.member_projection_json ?| ARRAY['notes','drill_snapshots','instructions','review_kind'])
      AND position('Jog safely' in c.member_projection_json::text)=0)
    THEN RAISE EXCEPTION 'unsafe_complete_publication'; END IF;
  IF NOT club.os_native_training_primary_member(owner,v_event,player)
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
  -- Publish the schedule-only version with private delivery selection.
  -- A coach can still update the sequence without changing the calendar.
  r:=club.preview_admin_academy_training_distribution(da,owner,cp,3);
  IF r ?| ARRAY['member_summary','drill_snapshots','notes','drills'] THEN RAISE EXCEPTION 'delivery_leaked_to_preview'; END IF;
  r:=club.publish_admin_academy_training_plan(da,owner,cp,3,true);
  IF r->>'published'<>'true' OR NOT EXISTS(SELECT 1 FROM club.calendar_items c
    WHERE c.id='os-training-'||cp::text AND c.member_projection_json->>'summary' IS NULL)
    THEN RAISE EXCEPTION 'schedule_only_publication_failed'; END IF;
  INSERT INTO club.staff_academy_groups(staff_id,academy_group_id,organization_id,from_plural,from_singular,from_group_coach,from_admin)
    VALUES(coach_staff,g,owner,false,false,false,true);
  r:=club.write_admin_training_delivery(dc,g,cp,3,1,jsonb_build_array(items->1));
  IF (r->>'version')::int<>3 OR (r->>'sequence_version')::int<>2 OR NOT EXISTS(
    SELECT 1 FROM club.academy_training_plan_publications WHERE plan_id=cp AND plan_version=3)
    OR (SELECT count(*) FROM club.academy_training_delivery_revisions WHERE plan_id=cp)<>2
    OR NOT EXISTS(SELECT 1 FROM club.calendar_items WHERE id='os-training-'||cp::text AND member_projection_json->>'summary' IS NULL)
    THEN RAISE EXCEPTION 'published_delivery_change_mutated_schedule'; END IF;
  PERFORM club.cancel_admin_academy_training_publication(da,owner,cp,3,'Synthetic cancellation',true);
  BEGIN
    PERFORM club.write_admin_training_delivery(dc,g,cp,3,2,'[]'::jsonb);
    RAISE EXCEPTION 'cancelled_delivery_write_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp() WHERE token_digest=da;
  BEGIN
    PERFORM club.read_admin_training_drills(da,g,p,5);
    RAISE EXCEPTION 'revoked_access_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $verify$;
ROLLBACK;
