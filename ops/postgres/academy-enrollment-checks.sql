\set ON_ERROR_STOP on
-- All synthetic Academy minors, admins, consents and roster rows are rolled
-- back. Uses direct schema-owner setup; function calls use the actual digest.
BEGIN;
DO $checks$
DECLARE
  admin_id uuid:=gen_random_uuid();
  manager_id uuid:=gen_random_uuid();
  admin_digest text:=repeat('f',64);
  manager_digest text:=repeat('b',64);
  city uuid:=gen_random_uuid();
  location uuid:=gen_random_uuid();
  scope uuid:=gen_random_uuid();
  group_id text:='grp_'||replace(gen_random_uuid()::text,'-','');
  application uuid:=gen_random_uuid();
  request_id uuid:=gen_random_uuid();
  alternate_request uuid:=gen_random_uuid();
  player_id text:='ply_'||replace(request_id::text,'-','');
  payload jsonb;
  result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
    VALUES(admin_id,'crm_enroll_super_fixture','Fixture Super Admin','super_admin',true,'fixture_only'),
      (manager_id,'crm_enroll_manager_fixture','Fixture Manager','manager',true,'fixture_only');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(admin_id,'lefteria_fc_academy'),(manager_id,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),admin_digest,admin_id,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour'),
      (gen_random_uuid(),manager_digest,manager_id,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour');

  -- Fixture has a canonical imported roster (required before operational write).
  INSERT INTO club.roster_import_state(id,digest,source_repository,source_commit,
    environment,teams_count,academy_groups_count,players_count,memberships_count,applied_at)
    VALUES('initial',repeat('a',64),'MikeCy-com/Lefteria-FC',repeat('a',40),
      'staging',0,0,0,0,clock_timestamp()) ON CONFLICT(id) DO NOTHING;
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(city,'Fixture City','crm-enrollment-fixture-city');
  INSERT INTO club.football_locations(id,city_id,name,slug)
    VALUES(location,city,'Fixture Location','crm-enrollment-fixture-site');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
    VALUES(scope,'lefteria_fc_academy',city);
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(group_id,'lefteria_fc_academy','Fixture U12');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
    VALUES('lefteria_fc_academy',group_id,city,location,scope);
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
    VALUES(manager_id,location,'location_manager');

  INSERT INTO academy_intake.registrations(
    id,organization_id,applicant_first_name,applicant_last_name,
    applicant_birth_date,guardian_name,guardian_relationship,guardian_phone,
    guardian_email)
    VALUES(application,'lefteria_fc_academy','Synthetic','Enrollee',
      DATE '2016-01-01','Fixture Parent','Parent',
      '+35799123456','parent-fixture@example.invalid');
  INSERT INTO academy_intake.registration_sealed_evidence(
    registration_id,encryption_key_id,evidence_ciphertext,evidence_sha256)
    VALUES(application,'fixture-only',convert_to(repeat('e',64),'UTF8'),
      repeat('c',64));
  INSERT INTO academy_intake.registration_consents(
    registration_id,consent_kind,revision,granted,policy_version,evidence_digest)
    SELECT application,k,1,
      CASE WHEN k='media' THEN false ELSE true END,
      'fixture-policy',repeat('a',64)
    FROM unnest(ARRAY['participation','medical_authorization',
      'privacy_notice','media','communications','liability','financial']) AS k;

  -- Super Admin can move public application to "under review".
  PERFORM identity.admin_academy_office(admin_digest,'review',
    jsonb_build_object('id',application,'status','under_review','version',1));
  payload:=jsonb_build_object('id',application,'version',2,
    'request_id',request_id,'city_id',city,'location_id',location,
    'group_id',group_id,'position','Defender','number',14);

  -- Manager is scoped to the correct location but not the unassigned public
  -- application's guardian data. The SQL function must still deny enrollment.
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(manager_digest,payload);
    RAISE EXCEPTION 'Manager enrolled unassigned public registration';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Invalid location must not create a player, audit or membership.
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(admin_digest,
      jsonb_set(payload,'{location_id}',to_jsonb(gen_random_uuid()::text)));
    RAISE EXCEPTION 'Foreign location accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF EXISTS(SELECT 1 FROM club.players WHERE id=player_id) THEN
    RAISE EXCEPTION 'Failed enrollment created player'; END IF;

  result:=identity.enroll_admin_academy_registration(admin_digest,payload);
  SET CONSTRAINTS ALL IMMEDIATE;
  IF result->>'player_id'<>player_id OR result->>'group_id'<>group_id
      OR result->>'status'<>'approved'
      OR NOT EXISTS(SELECT 1 FROM club.players p WHERE p.id=player_id
        AND p.primary_academy_group_id=group_id AND p.position='Defender'
        AND p.number=14)
      OR NOT EXISTS(SELECT 1 FROM club.player_academy_groups m
        WHERE m.player_id=player_id AND m.academy_group_id=group_id)
      OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_placements rp
        WHERE rp.registration_id=application AND rp.city_id=city
          AND rp.location_id=location AND rp.group_id=group_id
          AND rp.player_id=player_id)
      OR NOT EXISTS(SELECT 1 FROM academy_intake.registrations a
        WHERE a.id=application AND a.status='approved'
          AND a.status_version=3 AND a.onboarded_player_id=player_id)
  THEN RAISE EXCEPTION 'Atomic approval and roster placement failed'; END IF;

  -- Lost-response retry with exact request and fields must be idempotent.
  result:=identity.enroll_admin_academy_registration(admin_digest,payload);
  IF result->>'player_id'<>player_id OR (SELECT count(*) FROM club.players
      WHERE id=player_id)<>1 THEN
    RAISE EXCEPTION 'Enrollment replay was not idempotent'; END IF;
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(admin_digest,
      jsonb_set(payload,'{request_id}',to_jsonb(alternate_request::text)));
    RAISE EXCEPTION 'Conflicting enrollment request was accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  -- Loss of Academy grant after success prevents idempotent response replay.
  DELETE FROM identity.admin_organization_grants WHERE admin_account_id=admin_id;
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(admin_digest,payload);
    RAISE EXCEPTION 'Revoked administrator retained enrollment';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $checks$;
ROLLBACK;
