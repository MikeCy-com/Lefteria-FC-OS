\set ON_ERROR_STOP on
\set ECHO none
-- Rollback-only PostgreSQL acceptance. Covers session-bound manager grants,
-- city-shared opposition/pitches, location-only groups/bases and team isolation.
BEGIN;
DO $verify$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_city_manager uuid:=gen_random_uuid();
  v_location_manager uuid:=gen_random_uuid();
  v_team_manager uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_loc_a1 uuid:=gen_random_uuid();
  v_loc_a2 uuid:=gen_random_uuid();
  v_loc_b1 uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_scope_team uuid:=gen_random_uuid();
  v_team text:='grp_'||md5(gen_random_uuid()::text);
  v_group_a1 text:='grp_'||md5(gen_random_uuid()::text);
  v_group_a2 text:='grp_'||md5(gen_random_uuid()::text);
  v_opp uuid:=gen_random_uuid();
  v_field uuid:=gen_random_uuid();
  v_token_super text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_token_city text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_token_loc text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_token_team text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-super-'||substr(md5(v_super::text),1,12),'CI Super','super_admin',
      true,'$2b$12$'||repeat('A',53),false,false),
    (v_city_manager,'ci-city-'||substr(md5(v_city_manager::text),1,12),'CI City','manager',
      true,'$2b$12$'||repeat('A',53),false,false),
    (v_location_manager,'ci-loc-'||substr(md5(v_location_manager::text),1,12),'CI Location','manager',
      true,'$2b$12$'||repeat('A',53),false,false),
    (v_team_manager,'ci-team-'||substr(md5(v_team_manager::text),1,12),'CI Team','manager',
      true,'$2b$12$'||repeat('A',53),false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES(v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_city_manager,'lefteria_fc_academy'),
    (v_location_manager,'lefteria_fc_academy'),
    (v_team_manager,'lefteria_fc');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_token_super,v_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_token_city,v_city_manager,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_token_loc,v_location_manager,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_token_team,v_team_manager,repeat('d',64),clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO club.football_cities(id,name,slug)
  VALUES(v_city_a,'CI Alpha','ci-alpha-'||substr(md5(v_city_a::text),1,8)),
    (v_city_b,'CI Beta','ci-beta-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
  VALUES(v_scope_a,'lefteria_fc_academy',v_city_a),
    (v_scope_b,'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_locations(id,city_id,name,slug)
  VALUES(v_loc_a1,v_city_a,'North','north'),
    (v_loc_a2,v_city_a,'South','south'),
    (v_loc_b1,v_city_b,'East','east');
  INSERT INTO club.teams(id,organization_id,name)
  VALUES(v_team,'lefteria_fc','CI First Team');
  INSERT INTO club.football_operational_scopes(id,organization_id,team_id)
  VALUES(v_scope_team,'lefteria_fc',v_team);
  INSERT INTO club.academy_groups(id,organization_id,name)
  VALUES(v_group_a1,'lefteria_fc_academy','CI U10 North'),
    (v_group_a2,'lefteria_fc_academy','CI U12 South');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
  VALUES('lefteria_fc_academy',v_group_a1,v_city_a,v_loc_a1,v_scope_a),
    ('lefteria_fc_academy',v_group_a2,v_city_a,v_loc_a2,v_scope_a);
  INSERT INTO club.football_opponents(id,organization_id,scope_id,name)
  VALUES(v_opp,'lefteria_fc_academy',v_scope_a,'City Alpha Shared Opponent');
  INSERT INTO club.football_playing_fields(id,organization_id,scope_id,name,google_maps_url)
  VALUES(v_field,'lefteria_fc_academy',v_scope_a,'City Alpha Ground',
    'https://www.google.com/maps/place/alpha');

  INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role)
  VALUES(v_city_manager,v_city_a,'city_manager');
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
  VALUES(v_location_manager,v_loc_a1,'location_manager');
  INSERT INTO club.football_team_admin_grants(admin_account_id,scope_id,role)
  VALUES(v_team_manager,v_scope_team,'team_manager');

  v_result:=club.read_admin_football_directory(v_token_city,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'cities')<>1
    OR jsonb_array_length(v_result->'locations')<>2
    OR jsonb_array_length(v_result->'groups')<>2
    OR jsonb_array_length(v_result->'opponents')<>1
    OR jsonb_array_length(v_result->'playing_fields')<>1
    OR jsonb_array_length(v_result->'unassigned_groups')<>0
  THEN RAISE EXCEPTION 'city_manager_read_scope_exceeded_or_missing'; END IF;
  v_result:=club.read_admin_football_directory(v_token_loc,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'cities')<>1
    OR jsonb_array_length(v_result->'locations')<>1
    OR (v_result->'locations'->0->>'id')::uuid<>v_loc_a1
    OR jsonb_array_length(v_result->'groups')<>1
    OR jsonb_array_length(v_result->'opponents')<>1
    OR jsonb_array_length(v_result->'playing_fields')<>1
  THEN RAISE EXCEPTION 'location_manager_read_scope_exceeded_or_missing'; END IF;
  v_result:=club.read_admin_football_directory(v_token_team,'lefteria_fc');
  IF jsonb_array_length(v_result->'cities')<>0
    OR jsonb_array_length(v_result->'locations')<>0
    OR jsonb_array_length(v_result->'scopes')<>1
    OR jsonb_array_length(v_result->'opponents')<>0
  THEN RAISE EXCEPTION 'first_team_cross_organization_read'; END IF;
  BEGIN
    PERFORM club.read_admin_football_directory(v_token_team,'lefteria_fc_academy');
    RAISE EXCEPTION 'first_team_token_read_academy';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','opponent',
      jsonb_build_object('scope_id',v_scope_a,'name','Unauthorized shared opponent'));
    RAISE EXCEPTION 'location_manager_modified_shared_city_opponents';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','training_base',
      jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a2,'name','Unauthorized sibling base'));
    RAISE EXCEPTION 'location_manager_modified_sibling_location_base';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_city,'lefteria_fc_academy','opponent',
      jsonb_build_object('scope_id',v_scope_b,'name','Unauthorized city B opponent'));
    RAISE EXCEPTION 'city_manager_modified_other_city_opponent';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_loc,'lefteria_fc','training_base',
      jsonb_build_object('scope_id',v_scope_team,'name','Unauthorized first team base'));
    RAISE EXCEPTION 'academy_session_modified_first_team_catalog';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_result:=club.create_admin_football_resource(v_token_city,'lefteria_fc_academy','opponent',
    jsonb_build_object('scope_id',v_scope_a,'name','Shared City New Opponent'));
  IF NOT EXISTS(SELECT 1 FROM club.football_opponents
      WHERE id=(v_result->>'id')::uuid AND scope_id=v_scope_a)
  THEN RAISE EXCEPTION 'city_manager_opponent_write_missing'; END IF;
  v_result:=club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','training_base',
    jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a1,
      'name','North-only Base','playing_field_id',v_field,
      'google_maps_url','https://maps.app.goo.gl/ci-north'));
  IF NOT EXISTS(SELECT 1 FROM club.football_training_bases
      WHERE id=(v_result->>'id')::uuid AND academy_location_id=v_loc_a1)
  THEN RAISE EXCEPTION 'location_manager_base_write_missing'; END IF;
  v_result:=club.read_admin_football_directory(v_token_loc,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'opponents')<>2
     OR jsonb_array_length(v_result->'training_bases')<>1
  THEN RAISE EXCEPTION 'shared_catalogue_or_local_base_read_mismatch'; END IF;

  v_result:=club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','match_draft',
    jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a1,
      'group_id',v_group_a1,'opponent_id',v_opp,'playing_field_id',v_field,
      'match_date','2027-04-10','kickoff_local','17:00'));
  IF NOT EXISTS(SELECT 1 FROM club.football_match_drafts
      WHERE id=(v_result->>'id')::uuid AND academy_location_id=v_loc_a1
        AND academy_group_id=v_group_a1)
  THEN RAISE EXCEPTION 'authorized_location_match_not_saved'; END IF;
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','match_draft',
      jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a2,
        'group_id',v_group_a2,'opponent_id',v_opp,'playing_field_id',v_field,
        'match_date','2027-04-10','kickoff_local','18:00'));
    RAISE EXCEPTION 'unauthorized_sibling_match_saved';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- An authorized request cannot double-book a shared city field.
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_city,'lefteria_fc_academy','match_draft',
      jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a2,
        'group_id',v_group_a2,'opponent_id',v_opp,'playing_field_id',v_field,
        'match_date','2027-04-10','kickoff_local','17:00'));
    RAISE EXCEPTION 'authorized_city_manager_double_booked_field';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  -- Location restrictions must not be bypassed by a collision.
  BEGIN
    PERFORM club.create_admin_football_resource(v_token_loc,'lefteria_fc_academy','match_draft',
      jsonb_build_object('scope_id',v_scope_a,'location_id',v_loc_a2,
        'group_id',v_group_a2,'opponent_id',v_opp,'playing_field_id',v_field,
        'match_date','2027-04-10','kickoff_local','17:00'));
    RAISE EXCEPTION 'location_manager_collision_bypassed_authority';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Match format belongs to each exact group/team, not the shared city.
  v_result:=club.read_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a1);
  IF (v_result->>'version')::integer<>0 OR v_result->'duration_minutes'<>'null'::jsonb
    THEN RAISE EXCEPTION 'missing_settings_fabricated'; END IF;
  v_result:=club.write_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a1,0,45,7);
  IF (v_result->>'duration_minutes')::integer<>45 OR (v_result->>'players_on_field')::integer<>7
    THEN RAISE EXCEPTION 'match_settings_not_saved'; END IF;
  BEGIN
    PERFORM club.write_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a1,0,40,5);
    RAISE EXCEPTION 'stale_match_settings_write_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    PERFORM club.read_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a2);
    RAISE EXCEPTION 'sibling_group_settings_exposed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a2,0,60,9);
    RAISE EXCEPTION 'sibling_group_settings_modified';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  v_result:=club.write_admin_group_match_timing(v_token_city,'lefteria_fc_academy',v_group_a2,0,60,9);
  IF (v_result->>'players_on_field')::integer<>9 THEN RAISE EXCEPTION 'city_manager_group_settings_not_saved'; END IF;
  v_result:=club.read_admin_group_match_timing(v_token_loc,'lefteria_fc_academy',v_group_a1);
  IF (v_result->>'players_on_field')::integer<>7 THEN RAISE EXCEPTION 'group_settings_crossed_location'; END IF;
  BEGIN
    PERFORM club.write_admin_group_match_timing(v_token_team,'lefteria_fc',v_team,0,90,7);
    RAISE EXCEPTION 'invalid_first_team_format_accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  v_result:=club.write_admin_group_match_timing(v_token_team,'lefteria_fc',v_team,0,90,11);
  IF (v_result->>'players_on_field')::integer<>11 THEN RAISE EXCEPTION 'first_team_settings_not_saved'; END IF;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
  WHERE token_digest=v_token_loc;
  BEGIN
    PERFORM club.read_admin_football_directory(v_token_loc,'lefteria_fc_academy');
    RAISE EXCEPTION 'revoked_session_read_catalog';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  DELETE FROM club.football_city_admin_grants
    WHERE admin_account_id=v_city_manager AND city_id=v_city_a;
  v_result:=club.read_admin_football_directory(v_token_city,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'cities')<>0 OR
    jsonb_array_length(v_result->'opponents')<>0 OR
    jsonb_array_length(v_result->'locations')<>0
  THEN RAISE EXCEPTION 'revoked_city_grant_still_allowed_read'; END IF;
END $verify$;
ROLLBACK;
