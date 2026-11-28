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
  fixture_group_id text:='grp_'||replace(gen_random_uuid()::text,'-','');
  application uuid:=gen_random_uuid();
  request_id uuid:=gen_random_uuid();
  alternate_request uuid:=gen_random_uuid();
  proof_request uuid:=gen_random_uuid();
  otp_challenge uuid:=gen_random_uuid();
  original_parent_id uuid:=gen_random_uuid();
  second_guardian_id uuid:=gen_random_uuid();
  root_guardian_grant uuid:=gen_random_uuid();
  nested_guardian_grant uuid:=gen_random_uuid();
  renewed_guardian_grant uuid:=gen_random_uuid();
  unrelated_parent_id uuid:=gen_random_uuid();
  fee_for_child uuid:=gen_random_uuid();
  verified_fee_issue uuid:=gen_random_uuid();
  denied_fee_issue uuid:=gen_random_uuid();
  fee_for_other_child uuid:=gen_random_uuid();
  unrelated_player_id text:='ply_other_'||replace(gen_random_uuid()::text,'-','');
  otp_digest bytea:=decode(repeat('a',64),'hex');
  otp_issued_at timestamptz;
  confirmed int;
  children_count int;
  proof_body jsonb;
  sibling_registration uuid;
  sibling_escrow uuid;
  sibling_request uuid;
  sibling_result jsonb;
  sibling_player text;
  sibling_index integer;
  sibling_ids uuid[]:=ARRAY[]::uuid[];
  primary_escrow uuid:=gen_random_uuid();
  primary_id_front uuid:=gen_random_uuid();
  primary_id_back uuid:=gen_random_uuid();
  second_city uuid:=gen_random_uuid();
  second_location uuid:=gen_random_uuid();
  second_scope uuid:=gen_random_uuid();
  second_group text:='grp_other_'||replace(gen_random_uuid()::text,'-','');
  second_player text;
  document_status text;
  primary_photo uuid:=gen_random_uuid();
  processed integer:=1;
  fixture_player_id text:='ply_'||replace(request_id::text,'-','');
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
    VALUES(fixture_group_id,'lefteria_fc_academy','Fixture U12');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
    VALUES('lefteria_fc_academy',fixture_group_id,city,location,scope);
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

  -- Private parent evidence fixture: one child may supply identity front
  -- and back; each sibling has its own independent birth certificate.
  -- The test data is synthetic ciphertext with the correct format, not
  -- customer documents. No public upload grant is activated by the schema.
  INSERT INTO academy_intake.registration_document_escrow(
    id,registration_id,organization_id,document_slot,document_type,
    expiry_date,mime_type,size_bytes,encryption_key_id,sha256,
    ciphertext,nonce,auth_tag)
  VALUES
    (primary_id_front,application,'lefteria_fc_academy','id_front',
      'id_passport',DATE '2032-01-01','image/png',32,'fixture-vault',
      repeat('a',64),decode(repeat('ab',32),'hex'),
      decode(repeat('11',12),'hex'),decode(repeat('22',16),'hex')),
    (primary_id_back,application,'lefteria_fc_academy','id_back',
      'id_passport',DATE '2032-01-01','image/png',32,'fixture-vault',
      repeat('b',64),decode(repeat('cd',32),'hex'),
      decode(repeat('33',12),'hex'),decode(repeat('44',16),'hex'));
  -- Parent-upload grant is unique per child and never grants access to
  -- another sibling. The server supplies the 256-bit token digest and an
  -- AES-GCM sealed file; SQL never stores the raw bearer secret.
  INSERT INTO academy_intake.registration_upload_capabilities(
    registration_id,token_digest,expires_at)
  VALUES(application,repeat('7',64),clock_timestamp()+interval '30 minutes');
  BEGIN
    PERFORM academy_intake.store_public_registration_document(
      application,repeat('8',64),primary_photo,'passport_photo',
      NULL,'image/png',32,'fixture-vault',repeat('f',64),
      decode(repeat('ed',32),'hex'),decode(repeat('aa',12),'hex'),
      decode(repeat('bb',16),'hex'));
    RAISE EXCEPTION 'Wrong document token accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  document_status:=academy_intake.store_public_registration_document(
    application,repeat('7',64),primary_photo,'passport_photo',
    NULL,'image/png',32,'fixture-vault',repeat('f',64),
    decode(repeat('ed',32),'hex'),decode(repeat('aa',12),'hex'),
    decode(repeat('bb',16),'hex'));
  IF document_status<>'stored' THEN
    RAISE EXCEPTION 'Valid private document capability did not store'; END IF;
  -- Server-generated registration form uses the same exact-child vault but
  -- is not one of the browser upload slots. Synthetic encrypted fixture only.
  document_status:=academy_intake.store_public_registration_document(
    application,repeat('7',64),gen_random_uuid(),'registration_form',
    NULL,'application/pdf',32,'fixture-vault',repeat('c',64),
    decode(repeat('ce',32),'hex'),decode(repeat('aa',12),'hex'),
    decode(repeat('bb',16),'hex'));
  IF document_status<>'stored' THEN
    RAISE EXCEPTION 'Generated registration PDF did not store'; END IF;
  BEGIN
    PERFORM academy_intake.store_public_registration_document(
      application,repeat('7',64),gen_random_uuid(),'registration_form',
      NULL,'image/png',32,'fixture-vault',repeat('c',64),
      decode(repeat('ce',32),'hex'),decode(repeat('aa',12),'hex'),
      decode(repeat('bb',16),'hex'));
    RAISE EXCEPTION 'Non-PDF accepted into registration form slot';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  document_status:=academy_intake.store_public_registration_document(
    application,repeat('7',64),gen_random_uuid(),'passport_photo',
    NULL,'image/png',32,'fixture-vault',repeat('f',64),
    decode(repeat('ed',32),'hex'),decode(repeat('aa',12),'hex'),
    decode(repeat('bb',16),'hex'));
  IF document_status<>'replay' THEN
    RAISE EXCEPTION 'Private document retry was not idempotent'; END IF;
  BEGIN
    PERFORM academy_intake.store_public_registration_document(
      application,repeat('7',64),gen_random_uuid(),'passport_photo',
      NULL,'image/png',32,'fixture-vault',repeat('0',64),
      decode(repeat('ed',32),'hex'),decode(repeat('aa',12),'hex'),
      decode(repeat('bb',16),'hex'));
    RAISE EXCEPTION 'Conflicting private document slot accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  -- Siblings 2-5 each have separate registration ownership. Neither a
  -- common guardian phone nor the same season can mix document storage.
  FOR sibling_index IN 2..5 LOOP
    sibling_registration:=gen_random_uuid();
    sibling_escrow:=gen_random_uuid();
    sibling_ids:=array_append(sibling_ids,sibling_registration);
    INSERT INTO academy_intake.registrations(
      id,organization_id,applicant_first_name,applicant_last_name,
      applicant_birth_date,guardian_name,guardian_relationship,guardian_phone,
      guardian_email)
    VALUES(sibling_registration,'lefteria_fc_academy','Child'||sibling_index,
      'Private',DATE '2016-01-01','Fixture Parent','Parent',
      '+35799123456','parent-fixture@example.invalid');
    INSERT INTO academy_intake.registration_sealed_evidence(
      registration_id,encryption_key_id,evidence_ciphertext,evidence_sha256)
    VALUES(sibling_registration,'fixture-only',
      convert_to(repeat('f',64),'UTF8'),repeat('c',64));
    INSERT INTO academy_intake.registration_consents(
      registration_id,consent_kind,revision,granted,policy_version,evidence_digest)
    SELECT sibling_registration,k,1,
      CASE WHEN k='media' THEN false ELSE true END,
      'fixture-policy',repeat('a',64)
    FROM unnest(ARRAY['participation','medical_authorization',
      'privacy_notice','media','communications','liability','financial']) AS k;
    INSERT INTO academy_intake.registration_document_escrow(
      id,registration_id,document_slot,document_type,mime_type,size_bytes,
      encryption_key_id,sha256,ciphertext,nonce,auth_tag)
    VALUES(sibling_escrow,sibling_registration,'birth_certificate',
      'birth_certificate','image/png',32,'fixture-vault',
      repeat('a',64),decode(repeat('ef',32),'hex'),
      decode(repeat('55',12),'hex'),decode(repeat('66',16),'hex'));
    PERFORM identity.admin_academy_office(admin_digest,'review',
      jsonb_build_object('id',sibling_registration,'status','under_review',
        'version',1));
  END LOOP;

  BEGIN
    PERFORM academy_intake.store_public_registration_document(
      sibling_ids[1],repeat('7',64),gen_random_uuid(),'passport_photo',
      NULL,'image/png',32,'fixture-vault',repeat('f',64),
      decode(repeat('ef',32),'hex'),decode(repeat('ab',12),'hex'),
      decode(repeat('bc',16),'hex'));
    RAISE EXCEPTION 'Child 1 document token accessed child 2';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Alternate location deliberately has no manager grant for original city.
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(second_city,'Alternate Fixture City','crm-enrollment-alt-city');
  INSERT INTO club.football_locations(id,city_id,name,slug)
    VALUES(second_location,second_city,'Alternate Fixture Location',
      'crm-enrollment-alt-site');
  INSERT INTO club.football_operational_scopes(
    id,organization_id,academy_city_id)
    VALUES(second_scope,'lefteria_fc_academy',second_city);
  INSERT INTO club.academy_groups(id,organization_id,name)
    VALUES(second_group,'lefteria_fc_academy','Fixture Alternate U12');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
    VALUES('lefteria_fc_academy',second_group,second_city,
      second_location,second_scope);

  -- Super Admin can move public application to "under review".
  PERFORM identity.admin_academy_office(admin_digest,'review',
    jsonb_build_object('id',application,'status','under_review','version',1));
  -- The old status-only endpoint is not allowed to approve a public
  -- application without a verified roster placement.
  BEGIN
    PERFORM identity.admin_academy_office(admin_digest,'review',
      jsonb_build_object('id',application,'status','approved','version',2));
    RAISE EXCEPTION 'Status-only public approval bypassed roster enrollment';
  EXCEPTION WHEN check_violation THEN NULL; END;

  -- A pending/under-review public application is not enough to begin
  -- attesting guardian identity: it must first be added to the roster.
  BEGIN
    PERFORM identity.admin_academy_guardian_proof(admin_digest,'read',
      jsonb_build_object('registration_id',application));
    RAISE EXCEPTION 'Unenrolled child admitted to guardian proof review';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  payload:=jsonb_build_object('id',application,'version',2,
    'request_id',request_id,'city_id',city,'location_id',location,
    'group_id',fixture_group_id,'position','Defender','number',14);

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
  IF EXISTS(SELECT 1 FROM club.players WHERE id=fixture_player_id) THEN
    RAISE EXCEPTION 'Failed enrollment created player'; END IF;

  IF EXISTS(SELECT 1 FROM academy_intake.placement_workflow_outbox
      WHERE registration_id=application) THEN
    RAISE EXCEPTION 'Denied enrollment reserved an outbound event';
  END IF;
  result:=identity.enroll_admin_academy_registration(admin_digest,payload);
  SET CONSTRAINTS ALL IMMEDIATE;
  IF (SELECT count(*) FROM academy_intake.placement_workflow_outbox
      WHERE registration_id=application AND player_id=fixture_player_id
        AND group_id=fixture_group_id AND stage='welcome'
        AND delivery_state='blocked_verification'
        AND consent_verified=false AND provider_enabled=false)<>1 THEN
    RAISE EXCEPTION 'Approved placement did not reserve exactly one blocked welcome event';
  END IF;
  IF result->>'player_id'<>fixture_player_id OR result->>'group_id'<>fixture_group_id
      OR result->>'status'<>'approved'
      OR NOT EXISTS(SELECT 1 FROM club.players p WHERE p.id=fixture_player_id
        AND p.primary_academy_group_id=fixture_group_id AND p.position='Defender'
        AND p.number=14)
      OR NOT EXISTS(SELECT 1 FROM club.player_academy_groups m
        WHERE m.player_id=fixture_player_id AND m.academy_group_id=fixture_group_id)
      OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_placements rp
        WHERE rp.registration_id=application AND rp.city_id=city
          AND rp.location_id=location AND rp.group_id=fixture_group_id
          AND rp.player_id=fixture_player_id)
      OR NOT EXISTS(SELECT 1 FROM academy_intake.registrations a
        WHERE a.id=application AND a.status='approved'
          AND a.status_version=3 AND a.onboarded_player_id=fixture_player_id)
  THEN RAISE EXCEPTION 'Atomic approval and roster placement failed'; END IF;

  -- Two identity-card sides for child 1 are promoted to that real
  -- player's encrypted Documents in the enrollment transaction.
  IF (SELECT count(*) FROM academy_private.player_documents
      WHERE organization_id='lefteria_fc_academy'
        AND player_id=fixture_player_id)<>4
    OR EXISTS(
      SELECT 1 FROM academy_intake.registration_document_escrow
      WHERE registration_id=application AND
        (promoted_player_id IS DISTINCT FROM fixture_player_id OR
         promoted_document_id IS NULL OR ciphertext IS NOT NULL OR
         nonce IS NOT NULL OR auth_tag IS NOT NULL))
    OR (SELECT count(*) FROM academy_intake.registration_document_promotions
      WHERE registration_id=application AND player_id=fixture_player_id)<>4
    OR EXISTS(SELECT 1 FROM academy_intake.registration_document_escrow
      WHERE registration_id=ANY(sibling_ids) AND promoted_at IS NOT NULL)
  THEN RAISE EXCEPTION 'Child 1 document promotion leaked sibling evidence'; END IF;
  IF NOT EXISTS(SELECT 1 FROM academy_private.player_documents d
    JOIN academy_private.encrypted_document_objects o ON o.id=d.storage_object_id
    JOIN academy_intake.registration_document_promotions t ON t.player_document_id=d.id
    WHERE d.player_id=fixture_player_id AND d.document_type='registration_form'
      AND d.mime_type='application/pdf' AND o.scan_status='quarantined'
      AND t.registration_id=application)
  THEN RAISE EXCEPTION 'Generated form did not reach the exact child vault'; END IF;
  -- The pending sibling documents are NOT in child 1's folder.
  IF EXISTS(SELECT 1 FROM academy_private.player_documents d
    JOIN academy_intake.registration_document_escrow e
      ON e.id=d.storage_object_id
    WHERE e.registration_id=ANY(sibling_ids) AND d.player_id=fixture_player_id)
  THEN RAISE EXCEPTION 'Unapproved sibling evidence attached to child 1'; END IF;

  -- Enroll siblings 2-5; verify after counts 2, 4 and 5, not just 3.
  -- Child 2 is assigned to a separate Academy CITY/LOCATION.
  -- Enrollment writes its audit event after the status update. Restore the
  -- deferrable audit constraint after the first fixture used IMMEDIATE.
  SET CONSTRAINTS ALL DEFERRED;
  FOR sibling_index IN 2..5 LOOP
    sibling_registration:=sibling_ids[sibling_index-1];
    sibling_request:=gen_random_uuid();
    sibling_result:=identity.enroll_admin_academy_registration(admin_digest,
      jsonb_build_object('id',sibling_registration,'version',2,
        'request_id',sibling_request,
        'city_id',CASE WHEN sibling_index=2 THEN second_city ELSE city END,
        'location_id',CASE WHEN sibling_index=2 THEN second_location ELSE location END,
        'group_id',CASE WHEN sibling_index=2 THEN second_group ELSE fixture_group_id END,
        'position','Defender','number',30+sibling_index));
    sibling_player:=sibling_result->>'player_id';
    IF sibling_index=2 THEN second_player:=sibling_player; END IF;
    processed:=processed+1;
    IF sibling_result->>'status'<>'approved'
      OR (SELECT count(*) FROM academy_private.player_documents
        WHERE organization_id='lefteria_fc_academy'
          AND player_id=sibling_player)<>1
      OR NOT EXISTS(SELECT 1 FROM academy_intake.registration_document_promotions t
        JOIN academy_intake.registration_document_escrow e ON e.id=t.escrow_id
        JOIN academy_private.player_documents d ON d.id=t.player_document_id
        JOIN academy_private.encrypted_document_objects o
          ON o.id=d.storage_object_id
        WHERE t.registration_id=sibling_registration
          AND t.player_id=sibling_player AND e.promoted_player_id=sibling_player
          AND e.ciphertext IS NULL AND e.nonce IS NULL
          AND d.player_id=sibling_player AND o.player_id=sibling_player
          AND o.scan_status='quarantined')
      OR EXISTS(SELECT 1 FROM academy_private.player_documents d
        JOIN academy_intake.registration_document_escrow e
          ON e.id=d.storage_object_id
        WHERE d.player_id=sibling_player
          AND e.registration_id<>sibling_registration)
    THEN RAISE EXCEPTION 'Sibling % document promotion or ownership failed',
       sibling_index; END IF;
    IF processed IN (2,4,5)
      AND (SELECT count(*) FROM academy_intake.registration_document_promotions
        WHERE registration_id=application OR registration_id=ANY(sibling_ids))<>processed+3
    THEN RAISE EXCEPTION 'Family % child documents count wrong',processed; END IF;
  END LOOP;
  SET CONSTRAINTS ALL IMMEDIATE;
  BEGIN
    PERFORM academy_private.read_admin_player_document_metadata(
      manager_digest,'lefteria_fc_academy',second_player);
    RAISE EXCEPTION 'Original location manager read another city child Documents';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF (SELECT count(DISTINCT registration_id)
      FROM academy_intake.registration_document_promotions)<>5 THEN
    RAISE EXCEPTION '1-5 child family documents not isolated'; END IF;

  -- Player 360 uses the original approved application, not name or phone
  -- matching. Confirm the local manager's allowed view and cross-city denial.
  result:=club.read_admin_academy_player_profile(admin_digest,fixture_player_id);
  IF result->>'player_id' IS DISTINCT FROM fixture_player_id
    OR result#>>'{registration,id}' IS DISTINCT FROM application::text
    OR result#>>'{registration,applicant_birth_date}' IS DISTINCT FROM '2016-01-01'
    OR result#>>'{registration,guardian_name}' IS DISTINCT FROM 'Fixture Parent'
    OR result#>>'{registration,guardian_phone}' IS DISTINCT FROM '+35799123456'
    OR NOT coalesce((result->'academy_group_ids') ? fixture_group_id,false)
    OR (result->'registration') ? 'evidence_ciphertext'
  THEN RAISE EXCEPTION 'Player 360 original registration source mismatch'; END IF;
  result:=club.read_admin_academy_player_profile(manager_digest,fixture_player_id);
  IF result#>>'{registration,id}' IS DISTINCT FROM application::text THEN
    RAISE EXCEPTION 'Assigned manager cannot access exact player registration'; END IF;
  BEGIN
    PERFORM club.read_admin_academy_player_profile(manager_digest,second_player);
    RAISE EXCEPTION 'Original location manager read another city child registration';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Lost-response retry with exact request and fields must be idempotent.
  result:=identity.enroll_admin_academy_registration(admin_digest,payload);
  IF result->>'player_id'<>fixture_player_id OR (SELECT count(*) FROM club.players
      WHERE id=fixture_player_id)<>1 THEN
    RAISE EXCEPTION 'Enrollment replay was not idempotent'; END IF;
  IF (SELECT count(*) FROM academy_intake.placement_workflow_outbox
      WHERE registration_id=application)<>1 THEN
    RAISE EXCEPTION 'Replay duplicated the post-enrollment welcome event'; END IF;
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(admin_digest,
      jsonb_set(payload,'{request_id}',to_jsonb(alternate_request::text)));
    RAISE EXCEPTION 'Conflicting enrollment request was accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  -- Independent guardian proof review is a human attestation ledger,
  -- NOT an OTP login or an app entitlement. The source phone is compared
  -- with the already approved public Academy application.
  result:=identity.admin_academy_guardian_proof(admin_digest,'read',
    jsonb_build_object('registration_id',application));
  IF result->>'status'<>'not_verified'
    OR result->>'app_access_enabled'<>'false' THEN
    RAISE EXCEPTION 'Unreviewed guardian received access'; END IF;
  proof_body:=jsonb_build_object(
    'registration_id',application,'phone','35799123456',
    'proof_method','in_person_document','case_reference','CASE-2026-0001',
    'request_id',proof_request,'attested',true);

  BEGIN
    PERFORM identity.admin_academy_guardian_proof(manager_digest,'verify',proof_body);
    RAISE EXCEPTION 'Location manager could attest guardian identity';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM identity.admin_academy_guardian_proof(admin_digest,'verify',
      jsonb_set(proof_body,'{phone}',to_jsonb('35799123457'::text)));
    RAISE EXCEPTION 'Wrong telephone was accepted as original guardian';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM identity.admin_academy_guardian_proof(admin_digest,'verify',
      jsonb_set(proof_body,'{attested}','false'::jsonb));
    RAISE EXCEPTION 'No human confirmation was accepted';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;

  result:=identity.admin_academy_guardian_proof(admin_digest,'verify',proof_body);
  IF result->>'status'<>'verified_proof'
    OR result->>'app_access_enabled'<>'false'
    OR result->>'player_id'<>fixture_player_id THEN
    RAISE EXCEPTION 'Guardian proof review not verified'; END IF;
  result:=identity.admin_academy_guardian_proof(admin_digest,'verify',proof_body);
  IF result->>'replay'<>'true' THEN
    RAISE EXCEPTION 'Guardian proof retry not idempotent'; END IF;
  BEGIN
    PERFORM identity.admin_academy_guardian_proof(admin_digest,'verify',
      jsonb_set(proof_body,'{request_id}',to_jsonb(gen_random_uuid()::text)));
    RAISE EXCEPTION 'Second guardian review overwrote first';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  result:=identity.admin_academy_guardian_proof(admin_digest,'read',
    jsonb_build_object('registration_id',application));
  IF result->>'status'<>'verified_proof'
     OR result->>'phone_hint'<>'••••3456'
     OR result ? 'normalized_phone'
     OR result ? 'case_reference'
     OR result->>'app_access_enabled'<>'false'
     OR EXISTS(SELECT 1 FROM identity.mobile_record_links
       WHERE record_id=fixture_player_id)
     OR identity.has_current_guardian_phone_grant('35799123456')
  THEN RAISE EXCEPTION 'Proof review exposed guardian data or app rights'; END IF;

  -- Public-form guardian phone and human proof alone grant no role.
  -- The OTP must have been issued after that human proof and must be
  -- cryptographically verified (not merely marked consumed by a caller).
  otp_issued_at:=clock_timestamp();
  INSERT INTO identity.otp_challenges(
    id,normalized_phone,code_digest,created_at,expires_at,
    verification_calls,is_consumed)
  VALUES(otp_challenge,'35799123456',otp_digest,otp_issued_at,
    otp_issued_at+interval '5 minutes',0,false);
  IF NOT identity.has_pending_academy_guardian_proof('35799123456',otp_challenge)
  THEN RAISE EXCEPTION 'Reviewed Academy guardian not eligible for OTP'; END IF;
  IF identity.has_pending_academy_guardian_proof('35799123457',otp_challenge)
  THEN RAISE EXCEPTION 'Different guardian phone accepted original OTP'; END IF;
  BEGIN
    PERFORM identity.confirm_academy_guardian_otp('35799123456',otp_challenge,
      decode(repeat('b',64),'hex'));
    RAISE EXCEPTION 'Invalid OTP HMAC created guardian claim';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF EXISTS(SELECT 1 FROM identity.academy_guardian_otp_claims
      WHERE registration_id=application)
    OR identity.has_current_guardian_phone_grant('35799123456')
  THEN RAISE EXCEPTION 'Unverified OTP created family access'; END IF;

  confirmed:=identity.confirm_academy_guardian_otp(
    '35799123456',otp_challenge,otp_digest);
  IF confirmed<>1
    OR NOT identity.has_current_guardian_phone_grant('35799123456')
  THEN RAISE EXCEPTION 'Correct OTP did not establish one scoped original guardian'; END IF;
  confirmed:=identity.confirm_academy_guardian_otp(
    '35799123456',otp_challenge,otp_digest);
  IF confirmed<>0 OR (SELECT count(*) FROM identity.academy_guardian_otp_claims
      WHERE registration_id=application)<>1
  THEN RAISE EXCEPTION 'OTP replay duplicated guardian claim'; END IF;

  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at,
    guardian_derived_parent)
  VALUES(original_parent_id,'35799123456','parent','Verified Parent',true,
    'ios',clock_timestamp(),true);
  SELECT count(*) INTO children_count
    FROM identity.current_phone_granted_children(
      original_parent_id,'35799123456','parent',
      clock_timestamp()+interval '1 hour')
    WHERE player_id=fixture_player_id
      AND organization_id='lefteria_fc_academy';
  IF children_count<>1 THEN
    RAISE EXCEPTION 'New original Parent cannot see exact authorized child'; END IF;
  IF EXISTS(SELECT 1 FROM identity.current_phone_granted_children(
      original_parent_id,'35799123457','parent',
      clock_timestamp()+interval '1 hour'))
  THEN RAISE EXCEPTION 'Guardian could change signed phone to steal children'; END IF;

  -- A parent's invoice is visible only if BOTH the payer identity and the
  -- currently verified exact child match. A payer reference alone must not
  -- expose a second active Academy player's private charge.
  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES(unrelated_player_id,'lefteria_fc_academy','Other private child',
    27,'Defender','Academy',true,false,false,true);
  INSERT INTO finance.training_fee_issues(
    id,organization_id,player_id,payer_identity_id,fee_month,
    amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
  VALUES
    (fee_for_child,'lefteria_fc_academy',fixture_player_id,original_parent_id,
      DATE '2026-11-01',4500,'+35799123456',
      'original-fixture@example.invalid','First','Guardian',admin_id),
    (fee_for_other_child,'lefteria_fc_academy',unrelated_player_id,original_parent_id,
      DATE '2026-11-01',6500,'+35799123456',
      'other-fixture@example.invalid','First','Guardian',admin_id);
  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES(unrelated_parent_id,'35799123457','parent','Other Parent',true,
    'ios',clock_timestamp());

  result:=finance.read_parent_training_fees(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour');
  IF jsonb_array_length(result->'fees')<>1
    OR result->'fees'->0->>'id'<>fee_for_child::text
    OR result->'fees'->0->>'player_id'<>fixture_player_id
  THEN RAISE EXCEPTION 'Scoped parent wallet includes an unrelated player fee'; END IF;
  result:=finance.read_parent_invoice_pdf(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour',ARRAY[fee_for_child]);
  IF jsonb_array_length(result->'items')<>1
    OR result->'items'->0->>'player_id'<>fixture_player_id
    OR result->'items'->0->>'payer_identity_id'<>original_parent_id::text
  THEN RAISE EXCEPTION 'Verified original guardian invoice PDF scope failed'; END IF;

  -- All-or-nothing PDF retrieval: one unauthorized item rejects the entire
  -- mixed batch even if a permitted child's invoice was also requested.
  BEGIN
    PERFORM finance.read_parent_invoice_pdf(original_parent_id,'35799123456',
      'parent',clock_timestamp()+interval '1 hour',
      ARRAY[fee_for_child,fee_for_other_child]);
    RAISE EXCEPTION 'Mixed child PDF batch exposed unrelated payer data';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_parent_invoice_pdf(original_parent_id,'35799123457',
      'parent',clock_timestamp()+interval '1 hour',ARRAY[fee_for_child]);
    RAISE EXCEPTION 'Wrong signed parent phone exposed invoice';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  result:=finance.read_parent_training_fees(unrelated_parent_id,'35799123457',
    'parent',clock_timestamp()+interval '1 hour');
  IF jsonb_array_length(result->'fees')<>0
  THEN RAISE EXCEPTION 'Other parent received original guardian wallet'; END IF;
  BEGIN
    PERFORM finance.read_parent_invoice_pdf(unrelated_parent_id,'35799123457',
      'parent',clock_timestamp()+interval '1 hour',ARRAY[fee_for_child]);
    RAISE EXCEPTION 'Other parent received original guardian invoice';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Production-critical Academy billing: a new enrolled child has no
  -- legacy imported guardian link. The Super Admin can deliberately issue
  -- ONE selected month only after original proof+post-review phone OTP.
  -- A delegated adult is not automatically made the financial payer.
  result:=finance.issue_admin_training_fee(admin_digest,
    'lefteria_fc_academy',verified_fee_issue,fixture_player_id,
    DATE '2027-03-01',5000,'parent-fixture@example.invalid',
    'Fixture','Parent');
  IF result->>'created'<>'true'
    OR result->>'player_id'<>fixture_player_id
    OR (SELECT payer_identity_id FROM finance.training_fee_issues
        WHERE id=verified_fee_issue)<>original_parent_id
    OR (SELECT payer_phone FROM finance.training_fee_issues
        WHERE id=verified_fee_issue)<>'+35799123456'
    OR (SELECT count(*) FROM finance.training_fee_issues
        WHERE player_id=fixture_player_id)<>2 THEN
    RAISE EXCEPTION 'New Academy original guardian could not receive an exact month fee';
  END IF;
  result:=finance.issue_admin_training_fee(admin_digest,
    'lefteria_fc_academy',verified_fee_issue,fixture_player_id,
    DATE '2027-03-01',5000,'parent-fixture@example.invalid',
    'Fixture','Parent');
  IF result->>'created'<>'false' THEN
    RAISE EXCEPTION 'Original guardian training fee replay created a duplicate';
  END IF;
  result:=finance.read_parent_training_fees(original_parent_id,
    '35799123456','parent',clock_timestamp()+interval '1 hour');
  IF jsonb_array_length(result->'fees')<>2
    OR NOT EXISTS(
      SELECT 1 FROM jsonb_array_elements(result->'fees') fee(value)
      WHERE fee.value->>'id'=verified_fee_issue::text
        AND fee.value->>'fee_month'='2027-03-01') THEN
    RAISE EXCEPTION 'Issued new Academy fee not visible to its exact verified payer';
  END IF;
  result:=finance.read_parent_invoice_pdf(original_parent_id,
    '35799123456','parent',clock_timestamp()+interval '1 hour',
    ARRAY[verified_fee_issue]);
  IF result->'items'->0->>'payer_identity_id'<>original_parent_id::text
    OR result->'items'->0->>'fee_month'<>'2027-03-01' THEN
    RAISE EXCEPTION 'New Academy payer invoice PDF verification failed';
  END IF;
  -- Removing the disposable fee here preserves previous wallet fixture counts.
  DELETE FROM finance.training_fee_issues WHERE id=verified_fee_issue;

  -- A new original guardian can now share ONE explicitly verified child
  -- with a second adult by phone; the phone alone is not a login.
  -- Existing OTP sign-in is required separately before Parent creation.
  IF identity.has_current_guardian_phone_grant('35799123459') THEN
    RAISE EXCEPTION 'Unissued second-guardian phone already had child access'; END IF;
  result:=identity.issue_guardian_permission(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour',gen_random_uuid(),
    fixture_player_id,'35799123459',root_guardian_grant);
  IF result->>'status'<>'granted' OR result->>'id'<>root_guardian_grant::text
    OR result->>'organization_id'<>'lefteria_fc_academy'
    OR NOT identity.has_current_guardian_phone_grant('35799123459')
  THEN RAISE EXCEPTION 'Verified Academy original guardian could not grant second parent'; END IF;
  IF (SELECT source_permission_id FROM identity.family_guardian_permissions
      WHERE id=root_guardian_grant) IS NOT NULL THEN
    RAISE EXCEPTION 'Original guardian shared through an invented ancestor'; END IF;
  result:=identity.issue_guardian_permission(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour',gen_random_uuid(),
    fixture_player_id,'35799123459',root_guardian_grant);
  IF result->>'replay'<>'true'
    OR (SELECT count(*) FROM identity.family_guardian_permissions
       WHERE id=root_guardian_grant)<>1 THEN
    RAISE EXCEPTION 'Original guardian grant replay is not idempotent'; END IF;
  IF EXISTS(
    SELECT 1 FROM identity.current_phone_granted_children(
      gen_random_uuid(),'35799123459','parent',
      clock_timestamp()+interval '1 hour'))
  THEN RAISE EXCEPTION 'An unverified phone became a logged-in parent'; END IF;
  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at,
    guardian_derived_parent)
  VALUES(second_guardian_id,'35799123459','parent','Second verified guardian',
    true,'ios',clock_timestamp(),true);
  IF (SELECT count(*) FROM identity.current_phone_granted_children(
      second_guardian_id,'35799123459','parent',
      clock_timestamp()+interval '1 hour')
      WHERE player_id=fixture_player_id AND organization_id='lefteria_fc_academy')<>1
  THEN RAISE EXCEPTION 'Second verified parent not linked to exact child'; END IF;

  -- A phone-delegated parent must reference their own permission ID. No
  -- caller, even the DB owner, may silently manufacture another root.
  BEGIN
    INSERT INTO identity.family_guardian_permissions(
      id,organization_id,player_id,issuer_identity_id,
      source_permission_id,target_phone,status)
    VALUES(gen_random_uuid(),'lefteria_fc_academy',fixture_player_id,
      second_guardian_id,NULL,'35799123461','granted');
    RAISE EXCEPTION 'Delegated guardian fabricated an unanchored root';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  result:=identity.issue_guardian_permission(second_guardian_id,'35799123459',
    'parent',clock_timestamp()+interval '1 hour',gen_random_uuid(),
    fixture_player_id,'35799123460',nested_guardian_grant);
  IF result->>'status'<>'granted'
    OR (SELECT source_permission_id FROM identity.family_guardian_permissions
      WHERE id=nested_guardian_grant)<>root_guardian_grant
    OR NOT identity.has_current_guardian_phone_grant('35799123460')
  THEN RAISE EXCEPTION 'Nested second guardian lost revocation lineage'; END IF;

  -- Parent can remove only a grant they issued; removal cascades through
  -- all descendants for this child, never across unrelated family scopes.
  IF NOT identity.revoke_issued_guardian_permission(original_parent_id,
      '35799123456','parent',clock_timestamp()+interval '1 hour',
      gen_random_uuid(),root_guardian_grant) THEN
    RAISE EXCEPTION 'Original guardian failed to revoke second parent'; END IF;
  IF EXISTS(SELECT 1 FROM identity.family_guardian_permissions
      WHERE id IN (root_guardian_grant,nested_guardian_grant)
        AND status='granted')
    OR identity.has_current_guardian_phone_grant('35799123459')
    OR identity.has_current_guardian_phone_grant('35799123460')
    OR EXISTS(SELECT 1 FROM identity.current_phone_granted_children(
      second_guardian_id,'35799123459','parent',
      clock_timestamp()+interval '1 hour'))
  THEN RAISE EXCEPTION 'Revoked second-guardian lineage remained authorized'; END IF;
  -- A deliberate regrant produces a NEW auditable lineage, not a replay
  -- of the old revoked grant. This one must die with the original proof.
  result:=identity.issue_guardian_permission(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour',gen_random_uuid(),
    fixture_player_id,'35799123459',renewed_guardian_grant);
  IF result->>'status'<>'granted'
    OR NOT identity.has_current_guardian_phone_grant('35799123459') THEN
    RAISE EXCEPTION 'Explicit second-guardian regrant failed'; END IF;
  IF EXISTS(SELECT 1 FROM identity.current_phone_granted_children(
      second_guardian_id,'35799123459','parent',
      clock_timestamp()+interval '1 hour')
      WHERE player_id=unrelated_player_id)
  THEN RAISE EXCEPTION 'Second guardian gained a child outside this claim'; END IF;

  result:=identity.admin_academy_guardian_proof(admin_digest,'revoke',
    jsonb_build_object('registration_id',application,'version',1));
  IF result->>'status'<>'revoked' OR result->>'version'<>'2'
    OR result->>'app_access_enabled'<>'false'
    OR (SELECT count(*) FROM academy_intake.guardian_proof_review_events
      WHERE registration_id=application)<>2 THEN
    RAISE EXCEPTION 'Guardian proof revoke failed'; END IF;
  BEGIN
    PERFORM identity.admin_academy_guardian_proof(admin_digest,'verify',proof_body);
    RAISE EXCEPTION 'Revoked guardian proof was silently reopened';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  -- Revoking the human attestation immediately removes the OTP claim's
  -- effective scope, without relying on login expiry or an app token.
  IF EXISTS(SELECT 1 FROM identity.current_phone_granted_children(
      original_parent_id,'35799123456','parent',
      clock_timestamp()+interval '1 hour'))
    OR identity.has_current_guardian_phone_grant('35799123456')
  THEN RAISE EXCEPTION 'Revoked original guardian retained child scope'; END IF;
  -- Original proof revocation also kills a freshly reissued grant:
  -- no stale mobile token or surviving delegated Parent identity can revive it.
  IF identity.has_current_guardian_phone_grant('35799123459')
    OR EXISTS(SELECT 1 FROM identity.valid_guardian_phone_grants()
      WHERE permission_id=renewed_guardian_grant)
    OR EXISTS(SELECT 1 FROM identity.current_phone_granted_children(
      second_guardian_id,'35799123459','parent',
      clock_timestamp()+interval '1 hour'))
  THEN RAISE EXCEPTION 'Original proof revoked but second guardian retained access'; END IF;
  BEGIN
    PERFORM identity.issue_guardian_permission(original_parent_id,'35799123456',
      'parent',clock_timestamp()+interval '1 hour',gen_random_uuid(),
      fixture_player_id,'35799123458',gen_random_uuid());
    RAISE EXCEPTION 'Revoked original guardian could issue a new grant';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  IF (SELECT count(*) FROM identity.academy_guardian_otp_claims
      WHERE registration_id=application)<>1 THEN
    RAISE EXCEPTION 'Revocation erased immutable OTP proof'; END IF;

  -- Even an already authenticated and designated payer loses wallet and
  -- PDF visibility the moment human guardianship proof is revoked.
  result:=finance.read_parent_training_fees(original_parent_id,'35799123456',
    'parent',clock_timestamp()+interval '1 hour');
  IF jsonb_array_length(result->'fees')<>0
  THEN RAISE EXCEPTION 'Revoked guardian retained a private fee wallet'; END IF;
  BEGIN
    PERFORM finance.read_parent_invoice_pdf(original_parent_id,'35799123456',
      'parent',clock_timestamp()+interval '1 hour',ARRAY[fee_for_child]);
    RAISE EXCEPTION 'Revoked guardian retained private PDF access';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Revoked guardianship proof blocks NEW billing as well as private reads;
  -- an old payer identity or existing phone claim is never sufficient.
  BEGIN
    PERFORM finance.issue_admin_training_fee(admin_digest,
      'lefteria_fc_academy',denied_fee_issue,fixture_player_id,
      DATE '2027-04-01',5000,'parent-fixture@example.invalid',
      'Fixture','Parent');
    RAISE EXCEPTION 'Revoked original guardian was assigned a new Academy fee';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF EXISTS(SELECT 1 FROM finance.training_fee_issues
      WHERE id=denied_fee_issue) THEN
    RAISE EXCEPTION 'Denied Academy fee issuance wrote a private record';
  END IF;

  -- Loss of Academy grant after success prevents idempotent response replay.
  DELETE FROM identity.admin_organization_grants WHERE admin_account_id=admin_id;
  BEGIN
    PERFORM identity.enroll_admin_academy_registration(admin_digest,payload);
    RAISE EXCEPTION 'Revoked administrator retained enrollment';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $checks$;
ROLLBACK;
