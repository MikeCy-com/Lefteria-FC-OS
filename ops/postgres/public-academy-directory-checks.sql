\set ON_ERROR_STOP on
\set ECHO none

-- Runs only on disposable CI PostgreSQL. All synthetic rows are rolled back.
-- This confirms an imported-only ghost group is never published, an actual
-- canonical admin group appears, and manager public profile edits take effect.
BEGIN;
DO $canonical_directory_checks$
DECLARE
  v_id text := 'grp_' || md5(gen_random_uuid()::text);
  v_ghost text := 'ghost_' || md5(gen_random_uuid()::text);
  v_actor uuid := gen_random_uuid();
  v_directory jsonb;
  v_group jsonb;
BEGIN
  INSERT INTO club.roster_import_state (
    id,digest,source_repository,source_commit,environment,
    teams_count,academy_groups_count,players_count,memberships_count,applied_at
  ) VALUES (
    'initial',repeat('e',64),'disposable_ci',repeat('e',40),'development',
    0,0,0,0,clock_timestamp()
  ) ON CONFLICT(id) DO NOTHING;

  INSERT INTO club.academy_groups(id,organization_id,name)
  VALUES (v_id,'lefteria_fc_academy','Verified U12');

  INSERT INTO public_content.documents(
    kind,organization_id,id,data_json,display_order
  ) VALUES (
    'academy_group','lefteria_fc_academy',v_id,
    jsonb_build_object('id',v_id,'organization_id','lefteria_fc_academy',
      'name','Stale imported name','age_range','10–12','city','Limassol',
      'location_name','Verified legacy field','description','Old snapshot',
      'training_schedule','Old imported schedule','display_order',7),7
  ),(
    'academy_group','lefteria_fc_academy',v_ghost,
    jsonb_build_object('id',v_ghost,'organization_id','lefteria_fc_academy',
      'name','Ghost group from public import only'),0
  );
  SELECT club.read_public_academy_group_directory() INTO v_directory;
  IF jsonb_typeof(v_directory)!='array' THEN
    RAISE EXCEPTION 'Public Academy directory is not ready or returned wrong type';
  END IF;
  IF v_directory @> jsonb_build_array(jsonb_build_object('id',v_ghost)) THEN
    RAISE EXCEPTION 'Imported-only Academy group appeared without canonical creation';
  END IF;
  -- Even an imported CANONICAL group is not public until an authorized
  -- admin has created/edited its public profile. Canonical alone is not approval.
  IF EXISTS(
    SELECT 1 FROM jsonb_array_elements(v_directory) AS x(item)
    WHERE item->>'id'=v_id
  ) THEN RAISE EXCEPTION 'Canonical imported Academy group exposed without an admin-managed profile'; END IF;

  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active)
    VALUES(v_actor,'disposable-public-academy-'||v_actor::text,
      'Disposable preview manager','manager',true);
  -- Creating an admin profile is the publishing eligibility transition.
  INSERT INTO club.academy_group_profiles(
    organization_id,group_id,age_range,description,training_schedule,
    max_players,season,banner_url,display_order,revision,updated_by
  ) VALUES (
    'lefteria_fc_academy',v_id,'12–13','Manager updated public profile',
    'Monday 17:00',25,'2026/27','',2,1,v_actor
  );
  SELECT club.read_public_academy_group_directory() INTO v_directory;
  SELECT item INTO v_group
    FROM jsonb_array_elements(v_directory) AS x(item)
    WHERE item->>'id'=v_id;
  IF v_group IS NULL OR v_group->>'age_range'!='12–13'
     OR v_group->>'name'!='Verified U12'
     OR v_group->>'description'!='Manager updated public profile'
     OR v_group->>'training_schedule'!='Monday 17:00'
     OR v_group->>'display_order'!='2'
     OR v_group->>'location_name'!='Verified legacy field' THEN
    RAISE EXCEPTION 'Public directory does not reflect current authorized Academy profile edits';
  END IF;
  IF EXISTS(
    SELECT 1 FROM jsonb_object_keys(v_group) AS key(name)
    WHERE key.name NOT IN ('id','organization_id','name','age_range','city',
      'location_name','training_schedule','description','banner_url',
      'season','display_order','city_id','location_id','city_slug','location_slug')
  ) THEN RAISE EXCEPTION 'Private Academy fields leaked into public directory'; END IF;
  -- Private admin suppression must remove every public index/detail link
  -- without destroying canonical group, roster memberships or history.
  UPDATE club.academy_group_profiles SET is_public=false
    WHERE organization_id='lefteria_fc_academy' AND group_id=v_id;
  IF EXISTS(
    SELECT 1 FROM jsonb_array_elements(club.read_public_academy_group_directory()) x(item)
    WHERE item->>'id'=v_id
  ) THEN RAISE EXCEPTION 'Manager-unpublished Academy group still appears in public directory'; END IF;
  UPDATE club.academy_group_profiles SET is_public=true
    WHERE organization_id='lefteria_fc_academy' AND group_id=v_id;
  IF NOT EXISTS(
    SELECT 1 FROM jsonb_array_elements(club.read_public_academy_group_directory()) x(item)
    WHERE item->>'id'=v_id
  ) THEN RAISE EXCEPTION 'Republished Academy group did not return to public directory'; END IF;
END
$canonical_directory_checks$;
ROLLBACK;
