\set ON_ERROR_STOP on
\set ECHO none
-- Disposable PostgreSQL only: exact creation-time territory, immutability
-- and fail-closed ambiguity. All fixtures are rolled back.
BEGIN;
DO $goal_origin_checks$
DECLARE
  v_actor uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_site_a1 uuid:=gen_random_uuid();
  v_site_a2 uuid:=gen_random_uuid();
  v_site_b uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_team_scope uuid:=gen_random_uuid();
  v_team text:='ci-goal-team-'||substr(md5(gen_random_uuid()::text),1,14);
  v_group_a1 text:='ci-goal-ga1-'||substr(md5(gen_random_uuid()::text),1,14);
  v_group_a1b text:='ci-goal-ga1b-'||substr(md5(gen_random_uuid()::text),1,14);
  v_group_a2 text:='ci-goal-ga2-'||substr(md5(gen_random_uuid()::text),1,14);
  v_group_b text:='ci-goal-gb-'||substr(md5(gen_random_uuid()::text),1,14);
  v_one text:='ci-goal-player-one-'||substr(md5(gen_random_uuid()::text),1,14);
  v_multi_same text:='ci-goal-player-same-'||substr(md5(gen_random_uuid()::text),1,14);
  v_multi_other text:='ci-goal-player-other-'||substr(md5(gen_random_uuid()::text),1,14);
  v_unmapped text:='ci-goal-player-empty-'||substr(md5(gen_random_uuid()::text),1,14);
  v_club text:='ci-goal-club-player-'||substr(md5(gen_random_uuid()::text),1,14);
  v_goal_one uuid:=gen_random_uuid();
  v_goal_same uuid:=gen_random_uuid();
  v_goal_mixed uuid:=gen_random_uuid();
  v_goal_unmapped uuid:=gen_random_uuid();
  v_goal_club uuid:=gen_random_uuid();
BEGIN
  IF EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid='club.capture_player_goal_origin()'::regprocedure
      AND (NOT p.prosecdef OR EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE')))
  THEN RAISE EXCEPTION 'goal_origin_function_exposed'; END IF;
  IF (SELECT count(*) FROM pg_trigger
    WHERE tgrelid='club.player_development_goals'::regclass
      AND tgname IN ('tr_goal_origin_capture','tr_goal_origin_immutable')
      AND NOT tgisinternal)<>2
  THEN RAISE EXCEPTION 'goal_origin_triggers_missing'; END IF;

  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES(v_actor,'ci-goal-origin-'||substr(md5(v_actor::text),1,12),
    'CI Origin Owner','super_admin',true,'$2b$12$'||repeat('A',53),false,false);
  INSERT INTO club.football_cities(id,name,slug)
  VALUES(v_city_a,'CI Goals City A','ci-goal-a-'||substr(md5(v_city_a::text),1,8)),
        (v_city_b,'CI Goals City B','ci-goal-b-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug)
  VALUES(v_site_a1,v_city_a,'CI Goals North','ci-goal-north'),
        (v_site_a2,v_city_a,'CI Goals South','ci-goal-south'),
        (v_site_b,v_city_b,'CI Goals East','ci-goal-east');
  INSERT INTO club.teams(id,organization_id,name)
  VALUES(v_team,'lefteria_fc','CI Goal Team');
  INSERT INTO club.football_operational_scopes(
    id,organization_id,team_id,academy_city_id)
  VALUES(v_scope_a,'lefteria_fc_academy',NULL,v_city_a),
        (v_scope_b,'lefteria_fc_academy',NULL,v_city_b),
        (v_team_scope,'lefteria_fc',v_team,NULL);
  INSERT INTO club.academy_groups(id,organization_id,name)
  VALUES(v_group_a1,'lefteria_fc_academy','CI Goal North U10'),
        (v_group_a1b,'lefteria_fc_academy','CI Goal North U12'),
        (v_group_a2,'lefteria_fc_academy','CI Goal South U10'),
        (v_group_b,'lefteria_fc_academy','CI Goal East U10');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
  VALUES('lefteria_fc_academy',v_group_a1,v_city_a,v_site_a1,v_scope_a),
        ('lefteria_fc_academy',v_group_a1b,v_city_a,v_site_a1,v_scope_a),
        ('lefteria_fc_academy',v_group_a2,v_city_a,v_site_a2,v_scope_a),
        ('lefteria_fc_academy',v_group_b,v_city_b,v_site_b,v_scope_b);
  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields,team_id)
  VALUES
    (v_one,'lefteria_fc_academy','Goal One',11,'Forward','Academy',true,false,false,true,NULL),
    (v_multi_same,'lefteria_fc_academy','Goal Same Site',12,'Forward','Academy',true,false,false,true,NULL),
    (v_multi_other,'lefteria_fc_academy','Goal Cross Site',13,'Forward','Academy',true,false,false,true,NULL),
    (v_unmapped,'lefteria_fc_academy','Goal Unmapped',14,'Forward','Academy',true,false,false,true,NULL),
    (v_club,'lefteria_fc','Goal First Team',9,'Forward','First Team',true,false,false,true,v_team);
  INSERT INTO club.player_academy_groups(
    organization_id,player_id,academy_group_id)
  VALUES('lefteria_fc_academy',v_one,v_group_a1),
        ('lefteria_fc_academy',v_multi_same,v_group_a1),
        ('lefteria_fc_academy',v_multi_same,v_group_a1b),
        ('lefteria_fc_academy',v_multi_other,v_group_a1),
        ('lefteria_fc_academy',v_multi_other,v_group_a2);
  INSERT INTO club.player_development_goals(
    id,organization_id,player_id,title,category,created_by,updated_by)
  VALUES(v_goal_one,'lefteria_fc_academy',v_one,'Origin North','technical',v_actor,v_actor),
        (v_goal_same,'lefteria_fc_academy',v_multi_same,'Origin Shared','technical',v_actor,v_actor),
        (v_goal_mixed,'lefteria_fc_academy',v_multi_other,'Origin Ambiguous','technical',v_actor,v_actor),
        (v_goal_unmapped,'lefteria_fc_academy',v_unmapped,'Origin Unassigned','technical',v_actor,v_actor),
        (v_goal_club,'lefteria_fc',v_club,'Origin Club','technical',v_actor,v_actor);

  IF NOT EXISTS(SELECT 1 FROM club.player_development_goals g
    WHERE g.id=v_goal_one AND g.origin_scope_id=v_scope_a
      AND g.origin_city_id=v_city_a AND g.origin_location_id=v_site_a1
      AND g.origin_group_id=v_group_a1 AND g.origin_recorded_at IS NOT NULL)
  THEN RAISE EXCEPTION 'single_group_origin_incorrect'; END IF;
  IF NOT EXISTS(SELECT 1 FROM club.player_development_goals g
    WHERE g.id=v_goal_same AND g.origin_scope_id=v_scope_a
      AND g.origin_location_id=v_site_a1 AND g.origin_group_id IS NULL)
  THEN RAISE EXCEPTION 'same_site_multigroup_origin_incorrect'; END IF;
  IF EXISTS(SELECT 1 FROM club.player_development_goals g
    WHERE g.id IN (v_goal_mixed,v_goal_unmapped)
      AND (g.origin_scope_id IS NOT NULL OR g.origin_recorded_at IS NOT NULL))
  THEN RAISE EXCEPTION 'ambiguous_or_unmapped_goal_origin_inferred'; END IF;
  IF NOT EXISTS(SELECT 1 FROM club.player_development_goals g
    WHERE g.id=v_goal_club AND g.origin_scope_id=v_team_scope
      AND g.origin_city_id IS NULL AND g.origin_location_id IS NULL
      AND g.origin_group_id IS NULL AND g.origin_recorded_at IS NOT NULL)
  THEN RAISE EXCEPTION 'first_team_origin_incorrect'; END IF;

  -- A move to a sibling city/site must never rewrite the goal's birthplace.
  DELETE FROM club.player_academy_groups
    WHERE player_id=v_one AND academy_group_id=v_group_a1;
  INSERT INTO club.player_academy_groups(
    organization_id,player_id,academy_group_id)
  VALUES('lefteria_fc_academy',v_one,v_group_b);
  UPDATE club.player_development_goals SET
    progress=10,updated_at=clock_timestamp() WHERE id=v_goal_one;
  IF NOT EXISTS(SELECT 1 FROM club.player_development_goals
    WHERE id=v_goal_one AND origin_scope_id=v_scope_a
      AND origin_location_id=v_site_a1 AND progress=10)
  THEN RAISE EXCEPTION 'transfer_rewrote_historical_origin'; END IF;

  BEGIN
    UPDATE club.player_development_goals
      SET origin_location_id=v_site_a2 WHERE id=v_goal_one;
    RAISE EXCEPTION 'goal_origin_update_was_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  BEGIN
    INSERT INTO club.player_development_goals(
      id,organization_id,player_id,title,category,created_by,updated_by,
      origin_scope_id,origin_city_id,origin_location_id)
    VALUES(gen_random_uuid(),'lefteria_fc_academy',v_unmapped,'Spoof','technical',
      v_actor,v_actor,v_scope_b,v_city_b,v_site_b);
    RAISE EXCEPTION 'goal_origin_spoof_was_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- No manager/coach/client grant is created by this migration.
  IF EXISTS(SELECT 1 FROM pg_proc p
    JOIN pg_namespace n ON n.oid=p.pronamespace
    WHERE n.nspname='club' AND p.proname='capture_player_goal_origin'
      AND p.proacl IS NULL)
  THEN RAISE EXCEPTION 'goal_origin_implicit_public_execute'; END IF;
END $goal_origin_checks$;
ROLLBACK;
