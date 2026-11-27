\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic, rollback-only test: no catalogue or account is retained.
BEGIN;
DO $verify$
DECLARE
  actors uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid()];
  tokens text[]:=ARRAY[]::text[];
  roles text[]:=ARRAY['super_admin','manager','manager','coach','manager','accountant'];
  city_a uuid:=gen_random_uuid();city_b uuid:=gen_random_uuid();
  loc_b uuid:=gen_random_uuid();drill uuid:=gen_random_uuid();
  title text:='CI Drill '||drill::text;
  i integer;result jsonb;shared jsonb;
  proposed uuid:=gen_random_uuid();other_coach uuid:=gen_random_uuid();other_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  staff_a text:='ci_propose_'||substr(md5(gen_random_uuid()::text),1,12);staff_b text:='ci_propose_'||substr(md5(gen_random_uuid()::text),1,12);
BEGIN
  FOR i IN 1..6 LOOP
    tokens:=array_append(tokens,md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text));
    INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
      VALUES(actors[i],'ci-drill-'||substr(md5(actors[i]::text),1,16),'CI Drill',roles[i],true,'$2b$12$'||repeat('A',53),false,false);
    INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
      VALUES(actors[i],CASE WHEN i=5 THEN 'lefteria_fc' ELSE 'lefteria_fc_academy' END);
    INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
      VALUES(gen_random_uuid(),tokens[i],actors[i],repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day');
  END LOOP;
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(city_a,'CI Drill A','drill-a-'||substr(md5(city_a::text),1,12)),
      (city_b,'CI Drill B','drill-b-'||substr(md5(city_b::text),1,12));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES(loc_b,city_b,'CI Drill Location','drill-location');
  INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role) VALUES(actors[2],city_a,'city_manager');
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role) VALUES(actors[3],loc_b,'location_manager');

  result:=club.write_central_academy_drill(tokens[1],drill,0,'create',title,'technical','u9-u11',15,'Passing',E'First step\nSecond step');
  IF result->>'status'<>'draft' OR (result->>'version')::int<>1 THEN RAISE EXCEPTION 'drill_create_failed'; END IF;
  FOR i IN 2..4 LOOP
    result:=club.read_central_academy_drills(tokens[i],title,'all','any','approved',0);
    IF jsonb_array_length(result->'drills')<>0 THEN RAISE EXCEPTION 'draft_visible_to_staff'; END IF;
    BEGIN
      PERFORM club.read_central_academy_drills(tokens[i],title,'all','any','draft',0);
      RAISE EXCEPTION 'staff_draft_read_allowed';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
      PERFORM club.write_central_academy_drill(tokens[i],drill,1,'approve',NULL,NULL,NULL,NULL,NULL,NULL);
      RAISE EXCEPTION 'staff_central_write_allowed';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  END LOOP;
  result:=club.write_central_academy_drill(tokens[1],drill,1,'approve',NULL,NULL,NULL,NULL,NULL,NULL);
  IF result->>'status'<>'approved' OR (result->>'version')::int<>2 THEN RAISE EXCEPTION 'drill_approval_failed'; END IF;
  shared:=club.read_central_academy_drills(tokens[1],title,'technical','u9-u11','approved',0);
  IF jsonb_array_length(shared->'drills')<>1 OR shared->'drills'->0->>'id'<>drill::text THEN RAISE EXCEPTION 'approved_drill_missing'; END IF;
  FOR i IN 2..4 LOOP
    result:=club.read_central_academy_drills(tokens[i],title,'technical','u9-u11','approved',0);
    IF result IS DISTINCT FROM shared THEN RAISE EXCEPTION 'cross_city_catalogue_differs'; END IF;
  END LOOP;
  FOR i IN 5..6 LOOP
    BEGIN
      PERFORM club.read_central_academy_drills(tokens[i],title,'all','any','approved',0);
      RAISE EXCEPTION 'foreign_role_catalogue_read_allowed';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  END LOOP;
  BEGIN
    PERFORM club.write_central_academy_drill(tokens[1],drill,1,'archive',NULL,NULL,NULL,NULL,NULL,NULL);
    RAISE EXCEPTION 'stale_drill_write_allowed';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  result:=club.write_central_academy_drill(tokens[1],drill,2,'edit',title,'technical','u9-u11',20,'Passing revised','New instruction');
  IF result->>'status'<>'draft' OR (result->>'version')::int<>3 THEN RAISE EXCEPTION 'edit_skipped_reapproval'; END IF;
  result:=club.read_central_academy_drills(tokens[3],title,'all','any','approved',0);
  IF jsonb_array_length(result->'drills')<>0 THEN RAISE EXCEPTION 'unreviewed_edit_visible'; END IF;
  PERFORM club.write_central_academy_drill(tokens[1],drill,3,'approve',NULL,NULL,NULL,NULL,NULL,NULL);
  PERFORM club.write_central_academy_drill(tokens[1],drill,4,'archive',NULL,NULL,NULL,NULL,NULL,NULL);
  result:=club.write_central_academy_drill(tokens[1],drill,5,'restore',NULL,NULL,NULL,NULL,NULL,NULL);
  IF result->>'status'<>'draft' OR (result->>'version')::int<>6 THEN RAISE EXCEPTION 'restore_skipped_reapproval'; END IF;
  IF (SELECT count(*) FROM club.academy_coaching_drill_versions WHERE drill_id=drill)<>6
    OR NOT EXISTS(SELECT 1 FROM club.academy_coaching_drill_versions WHERE drill_id=drill AND version=2 AND snapshot->>'status'='approved' AND snapshot->>'duration_minutes'='15')
    THEN RAISE EXCEPTION 'drill_history_missing'; END IF;
  INSERT INTO club.staff(id,name,job_role,team_type,is_active,is_archived,has_explicit_active,has_explicit_archived) VALUES
    (staff_a,'CI Proposal Coach','Youth Coach','Academy',true,false,true,true),
    (staff_b,'CI Other Coach','Youth Coach','Academy',true,false,true,true);
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
    VALUES(other_coach,'ci_proposal_'||left(other_coach::text,8),'CI Other Coach','coach',true,'ci_not_real');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id) VALUES(other_coach,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),other_token,other_coach,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO identity.admin_staff_links(admin_account_id,staff_id) VALUES(actors[4],staff_a),(other_coach,staff_b);
  result:=club.submit_coach_drill_proposal(tokens[4],proposed,'CI Proposal '||proposed::text,'technical','all',15,'Passing','Pass then run');
  IF result->>'status'<>'draft' OR (result->>'version')::integer<>1 THEN RAISE EXCEPTION 'coach_proposal_not_private'; END IF;
  IF jsonb_array_length(club.read_coach_drill_proposals(tokens[4],0)->'drills')<>1
    OR jsonb_array_length(club.read_coach_drill_proposals(other_token,0)->'drills')<>0
    THEN RAISE EXCEPTION 'proposal_owner_isolation_failed'; END IF;
  IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_coach_drill_proposals(tokens[1],0)->'drills') x WHERE x->>'id'=proposed::text)
    THEN RAISE EXCEPTION 'central_review_queue_missing'; END IF;
  IF jsonb_array_length(club.read_central_academy_drills(other_token,'CI Proposal '||proposed::text,'all','any','approved',0)->'drills')<>0
    THEN RAISE EXCEPTION 'unreviewed_proposal_visible'; END IF;
  BEGIN
    PERFORM club.write_central_academy_drill(tokens[4],proposed,1,'approve',NULL,NULL,NULL,NULL,NULL,NULL);
    RAISE EXCEPTION 'coach_proposal_self_approved';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.submit_coach_drill_proposal(tokens[2],gen_random_uuid(),'CI wrong role','technical','all',10,'Passing','Pass');
    RAISE EXCEPTION 'manager_used_coach_proposal_route';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM club.write_central_academy_drill(tokens[1],proposed,1,'approve',NULL,NULL,NULL,NULL,NULL,NULL);
  IF jsonb_array_length(club.read_central_academy_drills(other_token,'CI Proposal '||proposed::text,'all','any','approved',0)->'drills')<>1
    OR club.read_coach_drill_proposals(tokens[4],0)->'drills'->0->>'status'<>'approved'
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_coach_drill_proposals(tokens[1],0)->'drills') x WHERE x->>'id'=proposed::text)
    THEN RAISE EXCEPTION 'proposal_approval_not_propagated'; END IF;
  UPDATE club.staff SET is_active=false WHERE id=staff_a;
  BEGIN
    PERFORM club.submit_coach_drill_proposal(tokens[4],gen_random_uuid(),'CI disabled coach','technical','all',10,'Passing','Pass');
    RAISE EXCEPTION 'inactive_coach_proposal_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp() WHERE token_digest=tokens[4];
  BEGIN
    PERFORM club.read_central_academy_drills(tokens[4],title,'all','any','approved',0);
    RAISE EXCEPTION 'revoked_coach_catalogue_read_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $verify$;
ROLLBACK;
