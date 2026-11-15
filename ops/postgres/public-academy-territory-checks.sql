\set ON_ERROR_STOP on
\set ECHO none
-- Public geography is backed by canonical city/location assignments, not by
-- imported free-text. Disposable PostgreSQL only: all writes rolled back.
BEGIN;
DO $territory_checks$
DECLARE
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_loc_a1 uuid:=gen_random_uuid();
  v_loc_a2 uuid:=gen_random_uuid();
  v_loc_b1 uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_actor uuid:=gen_random_uuid();
  v_a1 text:='territory_a1_'||md5(gen_random_uuid()::text);
  v_a2 text:='territory_a2_'||md5(gen_random_uuid()::text);
  v_b1 text:='territory_b1_'||md5(gen_random_uuid()::text);
  v_legacy text:='territory_legacy_'||md5(gen_random_uuid()::text);
  v_directory jsonb;
  v_group jsonb;
BEGIN
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(v_city_a,'Verified City Alpha','ci-verified-alpha-'||substr(md5(v_city_a::text),1,8)),
          (v_city_b,'Verified City Beta','ci-verified-beta-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug)
    VALUES(v_loc_a1,v_city_a,'North Ground','north'),
          (v_loc_a2,v_city_a,'South Ground','south'),
          (v_loc_b1,v_city_b,'East Ground','east');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
    VALUES(v_scope_a,'lefteria_fc_academy',v_city_a),
          (v_scope_b,'lefteria_fc_academy',v_city_b);
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(v_a1,'lefteria_fc_academy','Verified U10 North'),
          (v_a2,'lefteria_fc_academy','Verified U12 South'),
          (v_b1,'lefteria_fc_academy','Verified U10 East'),
          (v_legacy,'lefteria_fc_academy','Pending territory');
  INSERT INTO club.academy_group_location_assignments(
      organization_id,group_id,city_id,location_id,scope_id)
    VALUES('lefteria_fc_academy',v_a1,v_city_a,v_loc_a1,v_scope_a),
          ('lefteria_fc_academy',v_a2,v_city_a,v_loc_a2,v_scope_a),
          ('lefteria_fc_academy',v_b1,v_city_b,v_loc_b1,v_scope_b);
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active)
    VALUES(v_actor,'territory-publisher-'||v_actor::text,'CI Publisher','manager',true);
  INSERT INTO club.academy_group_profiles(
      organization_id,group_id,age_range,description,training_schedule,
      max_players,season,banner_url,display_order,revision,updated_by)
    SELECT 'lefteria_fc_academy',id,'10–12','Verified publication',
      'Monday 17:00',25,'2026/27','',0,1,v_actor
    FROM club.academy_groups WHERE id IN (v_a1,v_a2,v_b1,v_legacy);
  INSERT INTO public_content.documents(kind,organization_id,id,data_json,display_order)
    VALUES('academy_group','lefteria_fc_academy',v_a1,
      jsonb_build_object('id',v_a1,'organization_id','lefteria_fc_academy','city','WRONG IMPORTED CITY',
        'location_name','WRONG IMPORTED LOCATION'),0),
      ('academy_group','lefteria_fc_academy',v_legacy,
      jsonb_build_object('id',v_legacy,'organization_id','lefteria_fc_academy','city','Unverified legacy city',
        'location_name','Unverified legacy location'),0);

  v_directory:=club.read_public_academy_group_directory();
  SELECT x.item INTO v_group FROM jsonb_array_elements(v_directory) x(item)
    WHERE x.item->>'id'=v_a1;
  IF v_group->>'city_id' IS DISTINCT FROM v_city_a::text OR
     v_group->>'location_id' IS DISTINCT FROM v_loc_a1::text OR
     v_group->>'city' IS DISTINCT FROM 'Verified City Alpha' OR
     v_group->>'location_name' IS DISTINCT FROM 'North Ground' OR
     v_group->>'location_slug' IS DISTINCT FROM 'north' THEN
    RAISE EXCEPTION 'Public directory did not prefer exact canonical city/location IDs and names';
  END IF;
  IF v_group::text LIKE '%WRONG IMPORTED%' THEN
    RAISE EXCEPTION 'Imported city/location labels overrode verified geography';
  END IF;
  IF (SELECT count(*) FROM jsonb_array_elements(v_directory) x(item)
      WHERE item->>'city_id'=v_city_a::text)<>2 THEN
    RAISE EXCEPTION 'Two locations within one Academy city were not grouped by the same city ID';
  END IF;
  IF (SELECT count(*) FROM jsonb_array_elements(v_directory) x(item)
      WHERE item->>'city_id'=v_city_b::text)<>1 THEN
    RAISE EXCEPTION 'Other Academy city was not isolated in public hierarchy';
  END IF;
  SELECT x.item INTO v_group FROM jsonb_array_elements(v_directory) x(item)
    WHERE x.item->>'id'=v_legacy;
  IF v_group->>'city_id' IS NOT NULL OR v_group->>'location_id' IS NOT NULL OR
     v_group->>'city_slug' IS NOT NULL OR v_group->>'location_slug' IS NOT NULL OR
     v_group->>'city' IS DISTINCT FROM 'Unverified legacy city' THEN
    RAISE EXCEPTION 'Unassigned legacy content was falsely promoted into verified geography';
  END IF;
  IF EXISTS(SELECT 1 FROM jsonb_object_keys(v_group) k(name)
    WHERE k.name NOT IN ('id','organization_id','name','age_range','city',
      'location_name','city_id','location_id','city_slug','location_slug',
      'training_schedule','description','banner_url','season','display_order')) THEN
    RAISE EXCEPTION 'Private Academy information leaked into public territory projection';
  END IF;

  UPDATE club.football_locations SET is_active=false WHERE id=v_loc_a1;
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_public_academy_group_directory()) x(item)
    WHERE x.item->>'id'=v_a1) THEN
    RAISE EXCEPTION 'Inactive location still publicly exposes its assigned group';
  END IF;
  UPDATE club.football_locations SET is_active=true WHERE id=v_loc_a1;
  UPDATE club.football_cities SET is_active=false WHERE id=v_city_a;
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_public_academy_group_directory()) x(item)
    WHERE x.item->>'id' IN (v_a1,v_a2)) THEN
    RAISE EXCEPTION 'Inactive city still publicly exposes assigned groups';
  END IF;
  UPDATE club.football_cities SET is_active=true WHERE id=v_city_a;
  UPDATE club.academy_group_profiles SET is_public=false WHERE group_id=v_b1;
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(club.read_public_academy_group_directory()) x(item)
    WHERE x.item->>'id'=v_b1) THEN
    RAISE EXCEPTION 'Unpublished group reappeared through city directory';
  END IF;
END
$territory_checks$;
ROLLBACK;
