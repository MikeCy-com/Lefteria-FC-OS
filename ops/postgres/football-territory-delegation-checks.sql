\set ON_ERROR_STOP on
\set ECHO none
-- Actual PostgreSQL authorization and data fixtures; everything rolls back.
BEGIN;
DO $checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_local uuid:=gen_random_uuid();
  v_coach uuid:=gen_random_uuid();
  v_editor uuid:=gen_random_uuid();
  v_club_manager uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_loc_a1 uuid:=gen_random_uuid();
  v_loc_a2 uuid:=gen_random_uuid();
  v_loc_b uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_team_scope uuid:=gen_random_uuid();
  v_team text:='ci-team-'||substr(md5(gen_random_uuid()::text),1,24);
  v_group_a1 text:='ci-ga1-'||substr(md5(gen_random_uuid()::text),1,24);
  v_group_a2 text:='ci-ga2-'||substr(md5(gen_random_uuid()::text),1,24);
  v_group_b text:='ci-gb-'||substr(md5(gen_random_uuid()::text),1,24);
  v_player_a1 text:='ci-pa1-'||substr(md5(gen_random_uuid()::text),1,24);
  v_player_a2 text:='ci-pa2-'||substr(md5(gen_random_uuid()::text),1,24);
  v_player_b text:='ci-pb-'||substr(md5(gen_random_uuid()::text),1,24);
  v_token_super text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_token_manager text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_token_local text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_data jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-grants-super-'||substr(md5(v_super::text),1,8),'CI Owner','super_admin',
      true,'$2b$12$'||repeat('A',53),false,false),
    (v_manager,'ci-grants-city-'||substr(md5(v_manager::text),1,8),'CI City Manager','manager',
      true,'$2b$12$'||repeat('B',53),false,false),
    (v_local,'ci-grants-local-'||substr(md5(v_local::text),1,8),'CI Local Manager','manager',
      true,'$2b$12$'||repeat('C',53),false,false),
    (v_coach,'ci-grants-coach-'||substr(md5(v_coach::text),1,8),'CI Academy Coach','coach',
      true,'$2b$12$'||repeat('D',53),false,false),
    (v_editor,'ci-grants-editor-'||substr(md5(v_editor::text),1,8),'CI Staff Editor','editor',
      true,'$2b$12$'||repeat('E',53),false,false),
    (v_club_manager,'ci-grants-club-'||substr(md5(v_club_manager::text),1,8),'CI Club Manager','manager',
      true,'$2b$12$'||repeat('F',53),false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES
    (v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_manager,'lefteria_fc_academy'),
    (v_local,'lefteria_fc_academy'),
    (v_coach,'lefteria_fc_academy'),
    (v_editor,'lefteria_fc_academy'),
    (v_club_manager,'lefteria_fc');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_token_super,v_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_token_manager,v_manager,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_token_local,v_local,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO club.football_cities(id,name,slug)
  VALUES
    (v_city_a,'CI Grants City A','ci-grants-a-'||substr(md5(v_city_a::text),1,8)),
    (v_city_b,'CI Grants City B','ci-grants-b-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug)
  VALUES
    (v_loc_a1,v_city_a,'Alpha North','north'),
    (v_loc_a2,v_city_a,'Alpha South','south'),
    (v_loc_b,v_city_b,'Beta East','east');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
  VALUES
    (v_scope_a,'lefteria_fc_academy',v_city_a),
    (v_scope_b,'lefteria_fc_academy',v_city_b);
  INSERT INTO club.teams(id,organization_id,name)
  VALUES(v_team,'lefteria_fc','CI Scoped First Team');
  INSERT INTO club.football_operational_scopes(id,organization_id,team_id)
  VALUES(v_team_scope,'lefteria_fc',v_team);
  INSERT INTO club.academy_groups(id,organization_id,name)
  VALUES
    (v_group_a1,'lefteria_fc_academy','CI North U10'),
    (v_group_a2,'lefteria_fc_academy','CI South U12'),
    (v_group_b,'lefteria_fc_academy','CI Beta U10');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
  VALUES
    ('lefteria_fc_academy',v_group_a1,v_city_a,v_loc_a1,v_scope_a),
    ('lefteria_fc_academy',v_group_a2,v_city_a,v_loc_a2,v_scope_a),
    ('lefteria_fc_academy',v_group_b,v_city_b,v_loc_b,v_scope_b);
  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,is_active,is_archived,
    is_transferred,has_explicit_picker_fields)
  VALUES
    (v_player_a1,'lefteria_fc_academy','CI Player North',11,'Forward','Academy',true,false,false,true),
    (v_player_a2,'lefteria_fc_academy','CI Player South',12,'Forward','Academy',true,false,false,true),
    (v_player_b,'lefteria_fc_academy','CI Player Beta',13,'Forward','Academy',true,false,false,true);
  INSERT INTO club.player_academy_groups(organization_id,player_id,academy_group_id)
  VALUES
    ('lefteria_fc_academy',v_player_a1,v_group_a1),
    ('lefteria_fc_academy',v_player_a2,v_group_a2),
    ('lefteria_fc_academy',v_player_b,v_group_b);

  v_data:=club.read_admin_football_access(v_token_super,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'accounts')<>4
    OR jsonb_array_length(v_data->'city_grants')<>0
    OR jsonb_array_length(v_data->'team_grants')<>0
  THEN RAISE EXCEPTION 'superadmin_academy_access_projection_invalid'; END IF;
  v_data:=club.read_admin_football_access(v_token_super,'lefteria_fc');
  IF jsonb_array_length(v_data->'accounts')<>1
    OR jsonb_array_length(v_data->'city_grants')<>0
  THEN RAISE EXCEPTION 'first_team_access_projection_invalid'; END IF;
  BEGIN
    PERFORM club.read_admin_football_access(v_token_manager,'lefteria_fc_academy');
    RAISE EXCEPTION 'manager_read_delegated_grants';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_football_access(v_token_manager,'lefteria_fc_academy',
      v_local,'location',v_loc_a1,'location_manager',true);
    RAISE EXCEPTION 'manager_delegated_another_manager';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
      v_club_manager,'location',v_loc_a1,'location_manager',true);
    RAISE EXCEPTION 'foreign_organization_admin_granted_academy_location';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
      v_coach,'city',v_city_a,'city_manager',true);
    RAISE EXCEPTION 'coach_promoted_to_city_manager';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
      v_manager,'team',v_team_scope,'team_manager',true);
    RAISE EXCEPTION 'academy_manager_received_first_team_grant';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_manager,'city',v_city_a,'city_manager',true);
  IF (v_data->>'changed')::boolean IS NOT TRUE OR
    NOT EXISTS(SELECT 1 FROM club.football_city_admin_grants
      WHERE admin_account_id=v_manager AND city_id=v_city_a)
  THEN RAISE EXCEPTION 'valid_city_grant_not_persisted'; END IF;
  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_local,'location',v_loc_a1,'location_manager',true);
  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_coach,'location',v_loc_a1,'coach',true);
  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_editor,'location',v_loc_a1,'staff',true);
  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc',
    v_club_manager,'team',v_team_scope,'team_manager',true);
  IF (SELECT count(*) FROM club.football_location_admin_grants
    WHERE location_id=v_loc_a1)<>3
  THEN RAISE EXCEPTION 'site_roles_not_persisted'; END IF;
  IF NOT EXISTS(SELECT 1 FROM club.football_team_admin_grants
      WHERE scope_id=v_team_scope AND admin_account_id=v_club_manager)
  THEN RAISE EXCEPTION 'first_team_manager_not_delegated'; END IF;

  -- Location manager may access only their assigned North site, while
  -- the city manager sees North+South and neither can inspect Beta.
  v_data:=club.read_admin_football_location_metrics(
    v_token_local,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'locations')<>1
    OR (v_data->'locations'->0->>'location_id')::uuid<>v_loc_a1
    OR (v_data->'locations'->0->>'groups')::int<>1
    OR (v_data->'locations'->0->>'active_players')::int<>1
  THEN RAISE EXCEPTION 'local_manager_metrics_not_exact'; END IF;
  v_data:=club.read_admin_football_location_metrics(
    v_token_manager,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'locations')<>2
    OR (SELECT count(*) FROM jsonb_array_elements(v_data->'locations') x
      WHERE (x->>'groups')::int=1 AND (x->>'active_players')::int=1)<>2
  THEN RAISE EXCEPTION 'city_manager_metrics_misallocated'; END IF;

  -- Real private-goal boundary: manager cannot read a sibling location's
  -- record; city manager can read only within assigned city; moving a
  -- player to a new city never imports the old site's historical notes.
  INSERT INTO club.player_development_goals(
    id,organization_id,player_id,title,category,created_by,updated_by)
  VALUES
    (gen_random_uuid(),'lefteria_fc_academy',v_player_a1,'North private goal',
      'technical',v_super,v_super),
    (gen_random_uuid(),'lefteria_fc_academy',v_player_a2,'South private goal',
      'tactical',v_super,v_super);
  v_data:=club.read_admin_player_development_goals_manager(
    v_token_local,'lefteria_fc_academy',v_player_a1);
  IF (v_data->>'total')::int<>1 OR
    (v_data->'goals'->0->>'title')<>'North private goal' OR
    (v_data->'goals'->0->>'source_evaluation_id') IS NOT NULL
  THEN RAISE EXCEPTION 'local_manager_goal_projection_incorrect'; END IF;
  BEGIN
    PERFORM club.read_admin_player_development_goals_manager(
      v_token_local,'lefteria_fc_academy',v_player_a2);
    RAISE EXCEPTION 'sibling_location_private_goal_exposed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  v_data:=club.read_admin_player_development_goals_manager(
    v_token_manager,'lefteria_fc_academy',v_player_a2);
  IF (v_data->>'total')::int<>1
  THEN RAISE EXCEPTION 'city_manager_goal_projection_incorrect'; END IF;
  BEGIN
    PERFORM club.read_admin_player_development_goals_manager(
      v_token_super,'lefteria_fc_academy',v_player_a1);
    RAISE EXCEPTION 'delegated_goal_reader_accepted_superadmin_role';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  DELETE FROM club.player_academy_groups
    WHERE organization_id='lefteria_fc_academy'
      AND player_id=v_player_a1 AND academy_group_id=v_group_a1;
  INSERT INTO club.player_academy_groups(
    organization_id,player_id,academy_group_id)
  VALUES('lefteria_fc_academy',v_player_a1,v_group_b);
  BEGIN
    PERFORM club.read_admin_player_development_goals_manager(
      v_token_manager,'lefteria_fc_academy',v_player_a1);
    RAISE EXCEPTION 'old_city_manager_read_transferred_player';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_manager,'city',v_city_b,'city_manager',true);
  v_data:=club.read_admin_player_development_goals_manager(
    v_token_manager,'lefteria_fc_academy',v_player_a1);
  IF (v_data->>'total')::int<>0 OR jsonb_array_length(v_data->'goals')<>0
  THEN RAISE EXCEPTION 'new_city_manager_inherited_old_private_goals'; END IF;
  PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_manager,'city',v_city_b,'city_manager',false);
  DELETE FROM club.player_academy_groups
    WHERE organization_id='lefteria_fc_academy'
      AND player_id=v_player_a1 AND academy_group_id=v_group_b;
  INSERT INTO club.player_academy_groups(
    organization_id,player_id,academy_group_id)
  VALUES('lefteria_fc_academy',v_player_a1,v_group_a1);

  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_local,'location',v_loc_a1,'location_manager',false);
  IF (v_data->>'changed')::boolean IS NOT TRUE THEN
    RAISE EXCEPTION 'site_manager_revocation_not_recorded'; END IF;
  v_data:=club.read_admin_football_location_metrics(v_token_local,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'locations')<>0
  THEN RAISE EXCEPTION 'revoked_site_grant_leaked_private_metrics'; END IF;
  v_data:=club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
    v_manager,'city',v_city_a,'city_manager',false);
  v_data:=club.read_admin_football_location_metrics(v_token_manager,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'locations')<>0
  THEN RAISE EXCEPTION 'revoked_city_grant_leaked_private_metrics'; END IF;

  -- Super admin sees all three verified locations across two cities.
  v_data:=club.read_admin_football_location_metrics(v_token_super,'lefteria_fc_academy');
  IF jsonb_array_length(v_data->'locations')<>3
  THEN RAISE EXCEPTION 'superadmin_location_statistics_incomplete'; END IF;
  v_data:=club.read_admin_football_location_metrics(v_token_super,'lefteria_fc');
  IF jsonb_array_length(v_data->'locations')<>0
  THEN RAISE EXCEPTION 'first_team_received_academy_private_metrics'; END IF;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_token_super;
  BEGIN
    PERFORM club.write_admin_football_access(v_token_super,'lefteria_fc_academy',
      v_local,'location',v_loc_a2,'location_manager',true);
    RAISE EXCEPTION 'revoked_superadmin_granted_site';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $checks$;
ROLLBACK;
