\set ON_ERROR_STOP on
\set ECHO none
-- Real PostgreSQL relational acceptance, isolated CI only. Every row rolled back.
-- Academy opponents + playing fields are per CITY, not per location.
-- Academy bases, group membership and fixture administration stay per LOCATION.
BEGIN;
DO $scope_fixture$
DECLARE
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_loc_a1 uuid:=gen_random_uuid();
  v_loc_a2 uuid:=gen_random_uuid();
  v_loc_b1 uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_team_scope uuid:=gen_random_uuid();
  v_team text:='ci-team-'||substr(md5(gen_random_uuid()::text),1,20);
  v_group_a1 text:='ci-group-a1-'||substr(md5(gen_random_uuid()::text),1,20);
  v_group_a2 text:='ci-group-a2-'||substr(md5(gen_random_uuid()::text),1,20);
  v_group_b1 text:='ci-group-b1-'||substr(md5(gen_random_uuid()::text),1,20);
  v_opp_a uuid:=gen_random_uuid();
  v_field_a uuid:=gen_random_uuid();
  v_field_a2 uuid:=gen_random_uuid();
  v_constraint text;
  v_opp_b uuid:=gen_random_uuid();
  v_field_b uuid:=gen_random_uuid();
  v_opp_team uuid:=gen_random_uuid();
  v_field_team uuid:=gen_random_uuid();
BEGIN
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(v_city_a,'CI City Alpha','test-alpha-'||substr(md5(v_city_a::text),1,9)),
          (v_city_b,'CI City Beta','test-beta-'||substr(md5(v_city_b::text),1,9));
  INSERT INTO club.football_locations(id,city_id,name,slug,google_maps_url)
    VALUES(v_loc_a1,v_city_a,'Alpha North','alpha-north','https://maps.app.goo.gl/abc'),
          (v_loc_a2,v_city_a,'Alpha South','alpha-south','https://www.google.com/maps/place/example'),
          (v_loc_b1,v_city_b,'Beta East','beta-east',NULL);
  INSERT INTO club.teams(id,organization_id,name)
    VALUES(v_team,'lefteria_fc','CI First Team');
  INSERT INTO club.football_operational_scopes(id,organization_id,team_id,academy_city_id)
    VALUES(v_scope_a,'lefteria_fc_academy',NULL,v_city_a),
          (v_scope_b,'lefteria_fc_academy',NULL,v_city_b),
          (v_team_scope,'lefteria_fc',v_team,NULL);
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(v_group_a1,'lefteria_fc_academy','CI U10 North'),
          (v_group_a2,'lefteria_fc_academy','CI U12 South'),
          (v_group_b1,'lefteria_fc_academy','CI U10 East');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id
  ) VALUES
    ('lefteria_fc_academy',v_group_a1,v_city_a,v_loc_a1,v_scope_a),
    ('lefteria_fc_academy',v_group_a2,v_city_a,v_loc_a2,v_scope_a),
    ('lefteria_fc_academy',v_group_b1,v_city_b,v_loc_b1,v_scope_b);

  INSERT INTO club.football_opponents(id,organization_id,scope_id,name)
    VALUES(v_opp_a,'lefteria_fc_academy',v_scope_a,'Alpha Opponent'),
          (v_opp_b,'lefteria_fc_academy',v_scope_b,'Beta Opponent'),
          (v_opp_team,'lefteria_fc',v_team_scope,'First Team Opponent');
  INSERT INTO club.football_playing_fields(id,organization_id,scope_id,name,google_maps_url)
    VALUES(v_field_a,'lefteria_fc_academy',v_scope_a,'Alpha City Ground','https://www.google.com/maps/place/alpha'),
          (v_field_a2,'lefteria_fc_academy',v_scope_a,'Alpha Second Ground',NULL),
          (v_field_b,'lefteria_fc_academy',v_scope_b,'Beta City Ground','https://maps.app.goo.gl/beta'),
          (v_field_team,'lefteria_fc',v_team_scope,'Club Team Ground',NULL);

  -- Both Academy locations in Alpha use EXACTLY the same opponent and pitch.
  INSERT INTO club.football_match_drafts(
    organization_id,scope_id,academy_group_id,academy_location_id,
    opponent_id,playing_field_id,match_date,kickoff_local
  ) VALUES
    ('lefteria_fc_academy',v_scope_a,v_group_a1,v_loc_a1,
      v_opp_a,v_field_a,DATE '2027-02-05',TIME '17:00'),
    ('lefteria_fc_academy',v_scope_a,v_group_a2,v_loc_a2,
      v_opp_a,v_field_a,DATE '2027-02-05',TIME '18:00');
  IF (SELECT count(*) FROM club.football_match_drafts
      WHERE scope_id=v_scope_a AND opponent_id=v_opp_a)=0 THEN
    RAISE EXCEPTION 'City-shared Academy opponent lookup failed';
  END IF;

  -- Same group, different pitch: the group itself cannot play twice.
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,academy_group_id,academy_location_id,
      opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc_academy',v_scope_a,v_group_a1,v_loc_a1,
              v_opp_a,v_field_a2,DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'duplicate_group_kickoff_accepted';
  EXCEPTION WHEN unique_violation THEN
    GET STACKED DIAGNOSTICS v_constraint=CONSTRAINT_NAME;
    IF v_constraint<>'uq_football_draft_academy_group_kickoff' THEN RAISE; END IF;
  END;
  -- Same shared pitch, other group/location: no second booking at that kickoff.
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,academy_group_id,academy_location_id,
      opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc_academy',v_scope_a,v_group_a2,v_loc_a2,
              v_opp_a,v_field_a,DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'duplicate_shared_field_kickoff_accepted';
  EXCEPTION WHEN unique_violation THEN
    GET STACKED DIAGNOSTICS v_constraint=CONSTRAINT_NAME;
    IF v_constraint<>'uq_football_draft_field_kickoff' THEN RAISE; END IF;
  END;
  -- Separate pitch and separate group are allowed at the same time.
  INSERT INTO club.football_match_drafts(
    organization_id,scope_id,academy_group_id,academy_location_id,
    opponent_id,playing_field_id,match_date,kickoff_local
  ) VALUES ('lefteria_fc_academy',v_scope_a,v_group_a2,v_loc_a2,
            v_opp_a,v_field_a2,DATE '2027-02-05',TIME '17:00');

  -- The First Team has its own catalog and no access to the Academy catalogs.
  INSERT INTO club.football_match_drafts(
    organization_id,scope_id,club_team_id,opponent_id,
    playing_field_id,match_date,kickoff_local
  ) VALUES ('lefteria_fc',v_team_scope,v_team,v_opp_team,
            v_field_team,DATE '2027-02-05',TIME '19:00');

  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,club_team_id,opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc',v_team_scope,v_team,v_opp_team,v_field_team,
              DATE '2027-02-05',TIME '19:00');
    RAISE EXCEPTION 'duplicate_club_kickoff_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  -- City B cannot use City A opponents or playing fields.
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,academy_group_id,academy_location_id,
      opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc_academy',v_scope_b,v_group_b1,v_loc_b1,
              v_opp_a,v_field_b,DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'Cross-city opponent bypass succeeded';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,academy_group_id,academy_location_id,
      opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc_academy',v_scope_b,v_group_b1,v_loc_b1,
              v_opp_b,v_field_a,DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'Cross-city field bypass succeeded';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;

  -- A location cannot schedule a sibling location's group, even in the
  -- same city. Shared opponents/fields do not imply shared roster visibility.
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,academy_group_id,academy_location_id,
      opponent_id,playing_field_id,match_date,kickoff_local
    ) VALUES ('lefteria_fc_academy',v_scope_a,v_group_a1,v_loc_a2,
              v_opp_a,v_field_a,DATE '2027-02-05',TIME '20:00');
    RAISE EXCEPTION 'Cross-location group bypass succeeded';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;

  -- An Academy pitch or opponent never becomes a Club team option.
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,club_team_id,opponent_id,playing_field_id,
      match_date,kickoff_local
    ) VALUES ('lefteria_fc',v_team_scope,v_team,v_opp_a,v_field_team,
      DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'Club-to-Academy opponent bypass succeeded';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;
  BEGIN
    INSERT INTO club.football_match_drafts(
      organization_id,scope_id,club_team_id,opponent_id,playing_field_id,
      match_date,kickoff_local
    ) VALUES ('lefteria_fc',v_team_scope,v_team,v_opp_team,v_field_a,
      DATE '2027-02-05',TIME '17:00');
    RAISE EXCEPTION 'Club-to-Academy field bypass succeeded';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;

  -- Training BASES, unlike opponents and fields, belong to the location.
  INSERT INTO club.football_training_bases(
    organization_id,scope_id,academy_city_id,academy_location_id,
    name,playing_field_id,google_maps_url
  ) VALUES
    ('lefteria_fc_academy',v_scope_a,v_city_a,v_loc_a1,
      'North training base',v_field_a,'https://maps.app.goo.gl/north'),
    ('lefteria_fc_academy',v_scope_a,v_city_a,v_loc_a2,
      'South training base',v_field_a,'https://maps.app.goo.gl/south');
  IF (SELECT count(*) FROM club.football_training_bases
      WHERE academy_city_id=v_city_a AND scope_id=v_scope_a)<>2 THEN
    RAISE EXCEPTION 'Two distinct location bases did not coexist';
  END IF;
  BEGIN
    INSERT INTO club.football_training_bases(
      organization_id,scope_id,academy_city_id,academy_location_id,name
    ) VALUES ('lefteria_fc_academy',v_scope_a,v_city_a,v_loc_b1,
      'Wrong city base');
    RAISE EXCEPTION 'Cross-city location/base mismatch accepted';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;
  BEGIN
    UPDATE club.academy_group_location_assignments
       SET location_id=v_loc_b1
     WHERE organization_id='lefteria_fc_academy' AND group_id=v_group_a1;
    RAISE EXCEPTION 'Existing group relocated across cities without a matching scope';
  EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END $scope_fixture$;
ROLLBACK;
