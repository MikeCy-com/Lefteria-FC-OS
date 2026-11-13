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
  IF (result->'next'->>'id')::uuid<>first_id THEN
    RAISE EXCEPTION 'Existing parent did not start with first approved lesson';
  END IF;
  PERFORM hub.complete_mobile_next_education(
    older_parent,'35799500101','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy',first_id,first_rev);
  result:=hub.read_mobile_next_education(
    older_parent,'35799500101','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (result->'next'->>'id')::uuid<>second_id THEN
    RAISE EXCEPTION 'Completed member did not advance independently';
  END IF;

  -- The 90-day-later registration still begins at lesson one.
  result:=hub.read_mobile_next_education(
    later_parent,'35799500102','parent',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF (result->'next'->>'id')::uuid<>first_id THEN
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

  result:=hub.read_mobile_next_education(
    other_role,'35799500103','player',clock_timestamp()+interval '1 hour',
    'lefteria_fc_academy');
  IF result->'next' <> 'null'::jsonb THEN
    RAISE EXCEPTION 'Player received a parent-only curriculum';
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
