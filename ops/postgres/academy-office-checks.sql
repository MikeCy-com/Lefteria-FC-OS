\set ON_ERROR_STOP on
BEGIN;
DO $test$
DECLARE
  manager uuid:=gen_random_uuid();
  digest text:=repeat('c',64);
  registration uuid:=gen_random_uuid();
  order_id uuid:=gen_random_uuid();
  player text:='academy-office-fixture';
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
    VALUES(manager,'academy_office_fixture','Fixture Manager','manager',true,'fixture_only');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),digest,manager,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(player,'lefteria_fc_academy','Fixture Player',1,'Forward','Academy',true,false,false,true);

  PERFORM identity.admin_academy_office(digest,'register',jsonb_build_object('id',registration,'first_name','Fixture',
    'last_name','Applicant','birth_date','2016-01-01','guardian_name','Fixture Guardian',
    'guardian_relationship','Parent','guardian_phone','+35799123456','guardian_email','fixture@example.invalid'));
  IF NOT EXISTS(SELECT 1 FROM academy_intake.registrations WHERE id=registration AND status='pending' AND recorded_by=manager) THEN
    RAISE EXCEPTION 'Registration was not recorded';
  END IF;
  PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object('id',registration,'status','under_review','version',1));
  PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object('id',registration,'status','approved','version',2));
  SET CONSTRAINTS ALL IMMEDIATE;
  IF NOT EXISTS(SELECT 1 FROM academy_intake.registrations WHERE id=registration AND status='approved'
      AND decided_by=manager AND onboarded_player_id IS NULL) THEN
    RAISE EXCEPTION 'Registration review state was incorrect';
  END IF;
  IF jsonb_array_length(identity.admin_academy_office(digest,'players','{"search":"Fixture Player"}'))<>1 THEN
    RAISE EXCEPTION 'Academy player search failed';
  END IF;
  PERFORM identity.admin_academy_office(digest,'order',jsonb_build_object('id',order_id,'player_id',player,
    'item','Training kit','size','152','quantity',2));
  PERFORM identity.admin_academy_office(digest,'fulfilled',jsonb_build_object('id',order_id));
  IF NOT EXISTS(SELECT 1 FROM academy_intake.internal_orders WHERE id=order_id
      AND status='fulfilled' AND closed_by=manager AND quantity=2) THEN
    RAISE EXCEPTION 'Order was not fulfilled';
  END IF;
  BEGIN
    PERFORM identity.admin_academy_office(digest,'cancelled',jsonb_build_object('id',order_id));
    RAISE EXCEPTION 'Closed order changed';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  -- One CRM table must not imply one audience. Public intake belongs to
  -- Academy Super Admins until city/location delegation is implemented.
  DECLARE
    another_manager uuid:=gen_random_uuid();
    academy_super uuid:=gen_random_uuid();
    another_digest text:=repeat('d',64);
    super_digest text:=repeat('e',64);
    public_registration uuid:=gen_random_uuid();
    staff_registration uuid:=gen_random_uuid();
    inbox jsonb;
  BEGIN
    INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,password_hash)
      VALUES (another_manager,'academy_office_other_fixture','Other Fixture Manager','manager',true,'fixture_only'),
             (academy_super,'academy_office_super_fixture','Fixture Super Admin','super_admin',true,'fixture_only');
    INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
      VALUES (another_manager,'lefteria_fc_academy'),(academy_super,'lefteria_fc_academy');
    INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
      VALUES (gen_random_uuid(),another_digest,another_manager,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 hour'),
             (gen_random_uuid(),super_digest,academy_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 hour');

    INSERT INTO academy_intake.registrations(id,organization_id,applicant_first_name,applicant_last_name,
        applicant_birth_date,guardian_name,guardian_relationship,guardian_phone,guardian_email)
      VALUES(public_registration,'lefteria_fc_academy','Website','Child','2016-01-01',
        'Web Guardian','Parent','+35799123456','web-fixture@example.invalid');
    PERFORM identity.admin_academy_office(another_digest,'register',jsonb_build_object(
      'id',staff_registration,'first_name','Other','last_name','Child','birth_date','2016-01-01',
      'guardian_name','Other Guardian','guardian_relationship','Parent',
      'guardian_phone','+35799123456','guardian_email','other-fixture@example.invalid'));

    inbox:=identity.admin_academy_office(digest,'registrations','{}');
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(inbox) row
        WHERE row->>'id' IN (public_registration::text,staff_registration::text)) THEN
      RAISE EXCEPTION 'Manager accessed someone else''s or public intake';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(inbox) row
        WHERE row->>'id'=registration::text AND row->>'submission_source'='staff') THEN
      RAISE EXCEPTION 'Manager lost own registration';
    END IF;
    BEGIN
      PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object(
        'id',public_registration,'status','under_review','version',1));
      RAISE EXCEPTION 'Manager changed unassigned public intake';
    EXCEPTION WHEN unique_violation THEN NULL; END;
    BEGIN
      PERFORM identity.admin_academy_office(digest,'review',jsonb_build_object(
        'id',staff_registration,'status','under_review','version',1));
      RAISE EXCEPTION 'Manager changed another manager''s intake';
    EXCEPTION WHEN unique_violation THEN NULL; END;

    inbox:=identity.admin_academy_office(super_digest,'registrations','{}');
    IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(inbox) row
      WHERE row->>'id'=public_registration::text AND row->>'submission_source'='website') THEN
      RAISE EXCEPTION 'Super Admin could not review website application';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(inbox) row
      WHERE row->>'id'=staff_registration::text AND row->>'submission_source'='staff') THEN
      RAISE EXCEPTION 'Super Admin could not review staff application';
    END IF;
  END;
  DELETE FROM identity.admin_organization_grants WHERE admin_account_id=manager;
  BEGIN
    PERFORM identity.admin_academy_office(digest,'registrations','{}');
    RAISE EXCEPTION 'Revoked Academy authority retained access';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $test$;
ROLLBACK;
