\set ON_ERROR_STOP on
\set ECHO none
-- Disposable PostgreSQL verification only. All synthetic rows roll back.
BEGIN;
DO $learning$
DECLARE
  first_id uuid:=gen_random_uuid();
  second_id uuid:=gen_random_uuid();
  first_rev uuid:=gen_random_uuid();
  second_rev uuid:=gen_random_uuid();
  older_parent uuid:=gen_random_uuid();
  later_parent uuid:=gen_random_uuid();
  other_role uuid:=gen_random_uuid();
  result jsonb;
  denied boolean;
  first_child text:='ci_learning_'||left(replace(gen_random_uuid()::text,'-',''),22);
  second_child text:='ci_learning_'||left(replace(gen_random_uuid()::text,'-',''),22);
BEGIN
  INSERT INTO identity.mobile_identities
    (id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES
    (older_parent,'35799500101','parent','Synthetic earlier parent',true,'mobile',
      clock_timestamp()-interval '90 days'),
    (later_parent,'35799500102','parent','Synthetic new parent',true,'mobile',
      clock_timestamp()),
    (other_role,'35799500103','player','Synthetic player',true,'mobile',
      clock_timestamp());
  -- Reproduce canonical guardian relationships rather than bypassing
  -- the production authorization guard in this disposable transaction.
  INSERT INTO club.roster_import_state
    (id,digest,source_repository,source_commit,environment,
     teams_count,academy_groups_count,players_count,memberships_count,applied_at)
  VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
         'development',0,0,2,0,clock_timestamp());
  INSERT INTO migration.staff_relationship_import_state
    (id,digest,roster_digest,source_commit,environment,staff_count,
     team_assignments_count,group_assignments_count,group_coaches_count,
     contacts_count,applied_at)
  VALUES('initial',repeat('b',64),repeat('a',64),repeat('a',40),
         'development',0,0,0,0,0,clock_timestamp());
  INSERT INTO migration.account_import_state
    (id,digest,roster_digest,staff_digest,source_commit,environment,
     exported_at,applied_at,counts_json,manifest_bytes)
  VALUES('initial',repeat('c',64),repeat('a',64),repeat('b',64),
         repeat('a',40),'development',clock_timestamp(),clock_timestamp(),
         '{}'::jsonb,decode('00','hex'));
  INSERT INTO club.players
    (id,organization_id,name,number,position,team_type,team_id,
     primary_academy_group_id,image_url,is_active,is_archived,
     is_transferred,has_explicit_picker_fields)
  VALUES(first_child,'lefteria_fc_academy','Synthetic first learner child',1,
         'Midfielder','Academy',NULL,NULL,NULL,true,false,false,true),
        (second_child,'lefteria_fc_academy','Synthetic later learner child',2,
         'Midfielder','Academy',NULL,NULL,NULL,true,false,false,true);
  INSERT INTO identity.canonical_contact_bindings
    (collection,source_id,kind,normalized_phone,player_id,import_digest)
  VALUES('players',first_child,'guardian','35799500101',first_child,repeat('c',64)),
        ('players',second_child,'guardian','35799500102',second_child,repeat('c',64));
  INSERT INTO identity.mobile_record_links
    (identity_id,record_id,kind,role,normalized_phone,collection,is_primary)
  VALUES(older_parent,first_child,'guardian','parent','35799500101','players',true),
        (later_parent,second_child,'guardian','parent','35799500102','players',true);

  INSERT INTO hub.content
    (id,organization_id,kind,status,title,audience_parent,audience_player,
     provenance,version,approved_by,approved_at,published_at)
  VALUES
    (first_id,'lefteria_fc_academy','article','published',
      'First approved educational lesson',true,false,'human',1,
      gen_random_uuid(),clock_timestamp()-interval '92 days',
      clock_timestamp()-interval '91 days'),
    (second_id,'lefteria_fc_academy','article','published',
      'Second approved educational lesson',true,false,'human',1,
      gen_random_uuid(),clock_timestamp()-interval '32 days',
      clock_timestamp()-interval '31 days');
  INSERT INTO hub.revisions
    (id,content_id,revision,body_text,text_review,visual_review)
  VALUES (first_rev,first_id,1,'First approved educational body','passed','passed'),
         (second_rev,second_id,1,'Second approved educational body','passed','passed');

  result:=hub.read_mobile_next_education(
    older_parent,'35799500101','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (result->'next'->>'id')::uuid IS DISTINCT FROM first_id THEN
    RAISE EXCEPTION 'Existing parent did not start with first approved lesson';
  END IF;
  PERFORM hub.complete_mobile_next_education(
    older_parent,'35799500101','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy',first_id,first_rev);
  result:=hub.read_mobile_next_education(
    older_parent,'35799500101','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (result->'next'->>'id')::uuid IS DISTINCT FROM second_id THEN
    RAISE EXCEPTION 'Completed member did not advance independently';
  END IF;

  -- The 90-day-later registration still begins at lesson one.
  result:=hub.read_mobile_next_education(
    later_parent,'35799500102','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (result->'next'->>'id')::uuid IS DISTINCT FROM first_id THEN
    RAISE EXCEPTION 'Later registration skipped previously approved content';
  END IF;
  denied:=false;
  BEGIN
    PERFORM hub.complete_mobile_next_education(
      later_parent,'35799500102','parent',clock_timestamp()+interval '1 hour',
      'lefteria_fc_academy',second_id,second_rev);
  EXCEPTION WHEN serialization_failure THEN denied:=true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Subscriber could jump to a later lesson';
  END IF;

  -- An unlinked synthetic player must not receive even general player
  -- lessons: player role alone does not prove active academy membership.
  denied:=false;
  BEGIN
    PERFORM hub.read_mobile_next_education(
      other_role,'35799500103','player',clock_timestamp()+interval '1 hour',
      'lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Unlinked player bypassed canonical roster boundary';
  END IF;
  denied:=false;
  BEGIN
    PERFORM hub.read_mobile_next_education(
      later_parent,'35799500102','fan',clock_timestamp()+interval '1 hour',
      'lefteria_fc_academy');
  EXCEPTION WHEN insufficient_privilege THEN denied:=true;
  END;
  IF NOT denied THEN
    RAISE EXCEPTION 'Fan accessed unverified paid educational pathway';
  END IF;
END
$learning$;
ROLLBACK;
